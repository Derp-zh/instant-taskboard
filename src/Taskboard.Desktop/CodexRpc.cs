using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

// Read-only port of codex_probe/rpc.py; no inference, resume, answer or approval
// methods exist on this connection. All I/O runs away from the UI dispatcher.
internal sealed class CodexRpc : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writes = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private Process? process;
    private ClientWebSocket? socket;
    private Task? reader;
    private int nextId;
    private int disposed;
    internal event Action<JsonElement>? Notification;
    internal static bool Allowed(string method) => method is "initialize" or "thread/list" or "thread/read" or "thread/loaded/list" or "thread/turns/list";
    internal static async Task<CodexRpc> Start(string executable, bool proxy, string endpoint)
    {
        var rpc = new CodexRpc();
        try
        {
            if (endpoint.Length > 0)
            {
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "ws") throw new InvalidOperationException("请填写本机 ws:// 服务地址");
                rpc.socket = new ClientWebSocket(); using var timeout = new CancellationTokenSource(3000);
                await rpc.socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
                start.ArgumentList.Add("app-server");
                if (proxy) start.ArgumentList.Add("proxy");
                else
                {
                    start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
                    foreach (var setting in new[] { "features.hooks=false", "analytics.enabled=false", "mcp_servers={}", "plugins={}" })
                    { start.ArgumentList.Add("-c"); start.ArgumentList.Add(setting); }
                    var config = Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"),"config.toml");
                    if (File.Exists(config)) foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(config),@"(?m)^\s*\[mcp_servers\.([A-Za-z0-9_-]+)\]\s*$"))
                    { start.ArgumentList.Add("-c"); start.ArgumentList.Add("mcp_servers."+match.Groups[1].Value+".enabled=false"); }
                }
                rpc.process = Process.Start(start) ?? throw new IOException("Codex服务未启动");
                _ = Task.Run(async () => { try { while (await rpc.process.StandardError.ReadLineAsync(rpc.lifetime.Token).ConfigureAwait(false) is not null) { } } catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { } }, rpc.lifetime.Token);
            }
            rpc.reader = Task.Run(rpc.Read, rpc.lifetime.Token);
            await rpc.Call("initialize", new { clientInfo = new { name = "instant_taskboard", title = "即时任务看板", version = "0.8.0" }, capabilities = new { experimentalApi = true } }, 4000).ConfigureAwait(false);
            await rpc.Send(new { method = "initialized" }).ConfigureAwait(false); return rpc;
        }
        catch { await rpc.DisposeAsync(); throw; }
    }
    internal async Task<JsonElement> Call(string method, object parameters, int timeoutMs = 4000)
    {
        if (!Allowed(method)) throw new InvalidOperationException("只读监听不允许修改Codex会话");
        var id = Interlocked.Increment(ref nextId); var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await Send(new { id, method, @params = parameters }).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), lifetime.Token).ConfigureAwait(false);
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task Send(object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message); await writes.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            if (socket is not null) await socket.SendAsync(bytes, WebSocketMessageType.Text, true, lifetime.Token).ConfigureAwait(false);
            else { await process!.StandardInput.WriteLineAsync(Encoding.UTF8.GetString(bytes)).ConfigureAwait(false); await process.StandardInput.FlushAsync(lifetime.Token).ConfigureAwait(false); }
        }
        finally { writes.Release(); }
    }
    private async Task Read()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                string? line;
                if (socket is null) line = await process!.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                else
                {
                    using var stream = new MemoryStream(); var buffer = new byte[8192]; ValueWebSocketReceiveResult read;
                    do
                    {
                        read = await socket.ReceiveAsync(buffer.AsMemory(), lifetime.Token).ConfigureAwait(false);
                        if (read.MessageType == WebSocketMessageType.Close) throw new IOException("连接已关闭");
                        stream.Write(buffer, 0, read.Count); if (stream.Length > 4 * 1024 * 1024) throw new IOException("事件过大");
                    } while (!read.EndOfMessage);
                    line = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
                }
                if (line is null) throw new IOException("连接已关闭");
                using var document = JsonDocument.Parse(line); var message = document.RootElement.Clone();
                if (message.TryGetProperty("method", out _)) Notification?.Invoke(message);
                else if (message.TryGetProperty("id", out var id) && id.TryGetInt32(out var number) && pending.TryRemove(number, out var completion))
                {
                    if (message.TryGetProperty("error", out _)) completion.TrySetException(new IOException("Codex只读请求失败"));
                    else completion.TrySetResult(message.GetProperty("result").Clone());
                }
            }
        }
        catch (Exception error)
        { foreach (var completion in pending.Values) completion.TrySetException(new IOException("Codex监听断开", error)); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed,1) != 0) return;
        lifetime.Cancel(); socket?.Abort(); socket?.Dispose();
        if (process is not null)
        { try { if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(200)) process.Kill(entireProcessTree: true); } } catch (InvalidOperationException) { } process.Dispose(); }
        if (reader is not null) { try { await reader.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        foreach (var completion in pending.Values) completion.TrySetCanceled(); pending.Clear();
    }
}
