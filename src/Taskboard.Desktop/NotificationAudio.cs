using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal sealed class NotificationAudio : IDisposable
{
    private MediaPlayer? current;
    private TaskCompletionSource<string?>? completion;
    private static readonly Lazy<string> builtInFile = new(ExtractBuiltIn);
    internal static bool IsBuiltIn(string path) => string.IsNullOrWhiteSpace(path) || path == Taskboard.Core.NotificationSoundRule.BuiltInAudioPath;
    internal static string DisplayName(string path) => IsBuiltIn(path) ? "内置默认音 · Alarm04" : path;
    public static string Expand(string path) => IsBuiltIn(path) ? builtInFile.Value : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    private static string ExtractBuiltIn()
    {
        using var resource = typeof(NotificationAudio).Assembly.GetManifestResourceStream("Taskboard.DefaultNotification.wav")
            ?? throw new InvalidDataException("内置通知音缺失，请重新解压完整程序");
        using var memory = new MemoryStream(); resource.CopyTo(memory); var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var directory = Path.Combine(Path.GetTempPath(), "InstantTaskboard", "audio", hash);
        Directory.CreateDirectory(directory); var file = Path.Combine(directory, "Alarm04.wav");
        if (!File.Exists(file) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(file)), SHA256.HashData(bytes)))
        {
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, file, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return file;
    }
    public static async Task<string?> Validate(string path)
    {
        try
        {
            var full = Expand(path); if (!File.Exists(full)) return "音频文件不存在，请重新选择";
            using var audio = new NotificationAudio(); return await audio.Open(full, 0, false);
        }
        catch (Exception e) { return "音频无法读取：" + e.Message; }
    }
    public Task<string?> Play(string path, double volume) => Open(path, volume, true);
    private async Task<string?> Open(string path, double volume, bool play)
    {
        Stop(); var result = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = current = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
        player.MediaFailed += (_, e) => result.TrySetResult("音频解码失败：" + e.ErrorException.Message);
        player.MediaOpened += (_, _) =>
        {
            try
            {
                if (!player.HasAudio) { result.TrySetResult("所选文件没有可播放音频"); return; }
                if (play) player.Play(); else result.TrySetResult(null);
            }
            catch (Exception e) { result.TrySetResult("音频播放失败：" + e.Message); }
        };
        player.MediaEnded += (_, _) => result.TrySetResult(null);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(play ? 120 : 8) };
        timer.Tick += (_, _) => result.TrySetResult(play ? "音频超过两分钟，已停止播放" : "读取音频超时，请检查格式或系统解码器");
        timer.Start();
        try { player.Open(new Uri(Expand(path), UriKind.Absolute)); return await result.Task; }
        catch (Exception e) { return "音频播放失败：" + e.Message; }
        finally { timer.Stop(); player.Close(); if (current == player) { current = null; completion = null; } }
    }
    public void Stop() { current?.Close(); completion?.TrySetResult("播放已停止"); current = null; completion = null; }
    public void Dispose() => Stop();
}
