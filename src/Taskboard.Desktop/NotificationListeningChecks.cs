using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Headless integration checks: no hooks, no Windows setting writes, no mouse/keyboard automation.
internal static class NotificationListeningChecks
{
    public static async Task Run(BoardController controller)
    {
        if (!controller.Isolated) throw new InvalidOperationException("监听验证必须使用隔离数据目录");
        var root = controller.Store.DirectoryPath; var checks = new List<string>();
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        void Reject(Action action, string text) { try { action(); } catch (InvalidDataException) { checks.Add(text); return; } throw new InvalidOperationException(text); }
        var options = new NotificationListeningOptions();
        var codex = new NotificationSoundRule { AppName = "Codex", AppId = "sender.codex", AudioPath = "a.wav" };
        var chat = new NotificationSoundRule { AppName = "ChatGPT", AppId = "sender.chat", AudioPath = "b.mp3" };
        options.Rules.AddRange([codex, chat]);
        CapturedNotification N(string app, string id = "") => new(app, id, "标题", "正文", "测试");
        Check(NotificationSoundRules.Resolve(options, N("Codex")) is null, "总开关默认关闭");
        options.Enabled = true;
        Check(NotificationSoundRules.Resolve(options, N("codex.exe")) == codex, "大小写和exe后缀兼容");
        Check(NotificationSoundRules.Resolve(options, N("Codex CLI")) is null, "相似名称不误匹配");
        Check(NotificationSoundRules.Resolve(options, N("ChatGPT"))?.AudioPath == "b.mp3", "不同应用选择独立音频");
        Check(NotificationSoundRules.Resolve(options, N("改名", "SENDER.CODEX")) == codex, "Windows发送方身份优先且大小写兼容");
        Check(NotificationSoundRules.Resolve(options, N("Codex", "sender.other")) is null, "不同发送方不按名称误匹配");
        codex.Enabled = false;
        Check(NotificationSoundRules.Resolve(options, N("Codex", "sender.codex")) is null, "应用独立关闭立即停止匹配"); codex.Enabled = true;
        options.Rules.Add(new() { AppName = "Codex", AppId = "sender.other" });
        Check(NotificationSoundRules.Resolve(options, N("Codex")) is null, "辅助功能同名来源有歧义时不播放"); options.Rules.RemoveAt(2);
        options.Rules.Add(new() { AppName = "即时任务看板" });
        Check(NotificationSoundRules.Resolve(options, N("即时任务看板")) is null, "过滤本程序通知防止循环"); options.Rules.RemoveAt(2);
        Check(NotificationSoundRules.FromText(["标题", "正文"]) is null, "短通知不猜测来源");
        Check(NotificationSoundRules.FromText(["Codex", "完成"], knownApps: ["Codex"])?.Title == "完成", "已知应用的两行通知可识别");
        var combined = NotificationSoundRules.FromCombinedText(["Chrome", "标题一", "正文一", "Chrome", "标题二", "正文二"], ["Chrome"]);
        Check(combined.Length == 2 && combined[1].Title == "标题二", "同窗口浏览器通知分开捕获");
        Check(NotificationSoundRules.FromCombinedText(["Chrome", "Chrome", "标题"], ["Chrome"]).Length == 1, "无有效边界的通知不错误拆分");
        var dedup = new NotificationDeduplicator(); var now = DateTimeOffset.UtcNow;
        Check(dedup.Accept(N("Codex"), now) && !dedup.Accept(N("codex.exe") with { Source = "另一通道" }, now.AddMilliseconds(200)), "两通道同一通知只提醒一次");
        Check(dedup.Accept(N("ChatGPT"), now), "不同应用同内容分别提醒");
        Check(dedup.Accept(N("Codex"), now.AddSeconds(5)), "去重到期后允许相同通知");
        var duplicate = Copies.Of(options); duplicate.Rules.Add(new() { AppName = "另一个名称", AppId = "SENDER.CODEX" });
        Reject(() => NotificationSoundRules.Check(duplicate), "拒绝重复Windows发送方配置");
        var invalid = Copies.Of(options); invalid.Volume = double.NaN; Reject(() => NotificationSoundRules.Check(invalid), "拒绝无效音量");
        var old = JsonSerializer.Serialize(new BoardState(), Copies.Json).Replace("\"Listening\":{" + "\"Enabled\":false,\"CaptureMode\":0,\"Volume\":0.8,\"Rules\":[]},", "");
        var oldState = JsonSerializer.Deserialize<BoardState>(old, Copies.Json)!; Taskboard.Core.Validation.Check(oldState);
        Check(!oldState.Preferences.Listening.Enabled, "旧版本数据兼容且不自动启用监听");
        var state = new BoardState(); state.Preferences.Listening = options;
        var backupFile = Path.Combine(root, "listening-configuration.json"); ConfigurationBackup.Capture(state, root).Write(backupFile);
        var restored = ConfigurationBackup.Read(backupFile).Apply(new BoardState());
        Check(restored.Preferences.Listening.Rules[1].AudioPath == "b.mp3", "配置备份恢复逐应用音频规则");
        var journalFile = Path.Combine(root, "test-sound-restore.json"); var store = new FakeSoundStore();
        store.Values["app"] = new("app", false, "");
        var lease = new NotificationSoundLease(journalFile, store, "test-machine");
        var durableBeforeChange = false; store.BeforeSilence = () => durableBeforeChange = File.ReadAllText(journalFile).Contains("app");
        lease.Acquire("app"); Check(durableBeforeChange, "改变Windows声音前先持久保存原值");
        Check(store.Read("app") is { Existed: true, Value.Length: 0 }, "按应用关闭通知音");
        lease.Release("app"); Check(!store.Read("app").Existed, "原值缺失时恢复为缺失而非强制开启");
        store.Values["app"] = new("app", true, "original.wav"); lease.Acquire("app");
        var crashed = new NotificationSoundLease(journalFile, store, "test-machine");
        Check(crashed.RestoreAll().Length == 0 && store.Read("app").Value == "original.wav", "重启后恢复异常退出前的原音频");
        lease = new NotificationSoundLease(journalFile, store, "test-machine"); lease.Acquire("app"); store.Values["app"] = new("app", true, "user-change.wav");
        lease.Release("app"); Check(store.Read("app").Value == "user-change.wav", "保留用户在外部修改的通知声音");
        store.Values["app"] = new("app", true, ""); lease.Acquire("app"); lease.Release("app");
        Check(store.Read("app") is { Existed: true, Value.Length: 0 }, "原本静音的应用退出后保持静音");
        store.Values["app"] = new("app", false, ""); lease.Acquire("app"); store.FailRestore = true;
        Check(lease.RestoreAll().Length == 1 && File.ReadAllText(journalFile).Contains("app"), "恢复失败保留持久记录供重试");
        store.FailRestore = false; Check(lease.RestoreAll().Length == 0, "恢复失败可以重试");
        var wav = Path.Combine(root, "preview.wav"); CreateWav(wav);
        Check(await NotificationAudio.Validate(wav) is null, "实际Windows解码器验证WAV");
        var inputAudio = await Windows.Storage.StorageFile.GetFileFromPathAsync(wav);
        foreach (var extension in new[] { "mp3", "m4a" })
        {
            var encodedPath = Path.Combine(root, "preview." + extension); File.WriteAllBytes(encodedPath, []);
            var outputAudio = await Windows.Storage.StorageFile.GetFileFromPathAsync(encodedPath);
            var profile = extension == "mp3" ? Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp3(Windows.Media.MediaProperties.AudioEncodingQuality.Medium) :
                Windows.Media.MediaProperties.MediaEncodingProfile.CreateM4a(Windows.Media.MediaProperties.AudioEncodingQuality.Medium);
            var transcoder = new Windows.Media.Transcoding.MediaTranscoder();
            var prepared = await transcoder.PrepareFileTranscodeAsync(inputAudio, outputAudio, profile);
            if (!prepared.CanTranscode) throw new InvalidOperationException("系统无法生成格式验证文件：" + extension + " " + prepared.FailureReason);
            await prepared.TranscodeAsync();
            Check(await NotificationAudio.Validate(encodedPath) is null, "实际Windows解码器验证" + extension.ToUpperInvariant());
            using var formatAudio = new NotificationAudio(); Check(await formatAudio.Play(encodedPath, 0) is null, extension.ToUpperInvariant() + "异步播放完成（测试音量为零）");
        }
        var broken = Path.Combine(root, "broken.mp3"); File.WriteAllText(broken, "invalid audio");
        Check(await NotificationAudio.Validate(broken) is not null, "音频损坏时拒绝启用替换");
        Check(await NotificationAudio.Validate(Path.Combine(root, "missing.m4a")) is not null, "音频丢失时拒绝启用替换");
        using (var audio = new NotificationAudio()) Check(await audio.Play(wav, 0) is null, "实际音频异步播放完成（测试音量为零）");
        await controller.Listening.Apply();
        Check(controller.Listening.Status.Contains("隔离"), "隔离模式不启动系统监听及声音修改");
        await controller.Listening.RefreshApplications();
        Check(controller.Listening.Applications.Length > 0 && controller.Listening.Applications.All(x => x.Name.Length > 0 && x.Id.Length > 0), "只读枚举本机真实Windows通知发送方及显示名");
        controller.Engine.State.Preferences.Listening.Rules.Add(new() { AppName = "Codex", AppId = "sender.codex", AudioPath = wav });
        var settings = new SettingsSurface(controller); settings.SelectSection("监听");
        settings.Root.Measure(new Size(760, 680)); settings.Root.Arrange(new Rect(0, 0, 760, 680)); settings.Root.UpdateLayout();
        Check(SettingsSurface.Sections.Contains("监听") && Find<CheckBox>(settings.Root).Any(x => Equals(x.Content, "启用通知音替换")), "设置左侧监听入口和总开关");
        Check(Find<Button>(settings.Root).Any(x => Equals(x.Content, "选择音频")) && Find<Button>(settings.Root).Any(x => Equals(x.Content, "试听")), "逐应用选择音频及试听按钮");
        Check(Find<CheckBox>(settings.Root).Single(x => Equals(x.Content, "启用通知音替换")).IsEnabled == false, "隔离界面禁止修改真实系统声音");
        var add = Find<Button>(settings.Root).Single(x => Equals(x.Content, "＋ 添加应用")); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(controller.Engine.State.Preferences.Listening.Rules.Count == 2 && !controller.Engine.State.Preferences.Listening.Rules[1].Enabled, "添加多个应用且新条目默认不替换");
        settings.SelectSection("监听");
        settings.Root.Measure(new Size(760, 680)); settings.Root.Arrange(new Rect(0, 0, 760, 680)); settings.Root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(760, 680, 96, 96, PixelFormats.Pbgra32); bitmap.Render(settings.Root);
        using (var output = File.Create(Path.Combine(root, "listening-settings.png"))) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(output); }
        Find<ScrollViewer>(settings.Root).First().ScrollToVerticalOffset(480); settings.Root.UpdateLayout();
        var rulesBitmap = new RenderTargetBitmap(760, 680, 96, 96, PixelFormats.Pbgra32); rulesBitmap.Render(settings.Root);
        using (var output = File.Create(Path.Combine(root, "listening-rules.png"))) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(rulesBitmap)); png.Save(output); }
        settings.Destroy(); await controller.Flush();
        Check(AtomicStore.Read(controller.Store.MainPath).Preferences.Listening.Rules.Count == 2, "监听规则随隔离本地数据保存");
        File.WriteAllText(Path.Combine(root, "listening-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks,
            boundaries = new[] { "未修改真实Windows通知设置", "真实应用的横幅捕获和系统通知音抑制待桌面实测", "WAV/MP3/M4A已用真实文件在本机验证，其他电脑取决于其解码器" } }, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static void CreateWav(string file)
    {
        const int samples = 8000;
        using var writer = new BinaryWriter(File.Create(file)); writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / 16000) * 2000));
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class FakeSoundStore : INotificationSoundStore
    {
        public Dictionary<string, OriginalNotificationSound> Values { get; } = new();
        public Action? BeforeSilence { get; set; }
        public bool FailRestore { get; set; }
        public OriginalNotificationSound Read(string id) => Values[id];
        public void Silence(string id) { BeforeSilence?.Invoke(); Values[id] = new(id, true, ""); }
        public void Restore(OriginalNotificationSound original) { if (FailRestore) throw new IOException("模拟失败"); Values[original.AppId] = original; }
    }
}
