using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record CodexThread(string Id, string Name, long UpdatedAt)
{ public string Caption => (Name.Length > 0 ? Name + " · " : "会话 · ") + Id; }
internal sealed record CodexWatch(Guid TaskId, string ThreadId, int Generation);
internal sealed class CodexObserver : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private CodexRpc? rpc;
    private Task? worker;
    private CodexWatch[] watches = [];
    private long sequence;
    private string epoch = "";
    internal string Epoch => epoch;
    internal bool Authoritative { get; private set; }
    internal string Status { get; private set; } = "尚未连接 · 状态待确认";
    internal event Action? Changed;
    internal event Action<CodexWatch, JsonElement, long, string, bool>? Event;
    internal void Watch(CodexWatch[] values) => Volatile.Write(ref watches, values);
    internal void Start(string executable, string endpoint)
    { if (worker is null) worker = Task.Run(() => Run(executable, endpoint)); }
    internal static string FindExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        { var candidate = Path.Combine(directory.Trim('"'), "codex.exe"); if (File.Exists(candidate)) return candidate; }
        var known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(known)) return Directory.EnumerateFiles(known, "codex.exe", SearchOption.AllDirectories).FirstOrDefault() ?? "";
        return "";
    }
    private void SetStatus(string value) { Status = value; Changed?.Invoke(); }
    private async Task Run(string executable, string endpoint)
    {
        if (executable.Length == 0 && endpoint.Length == 0) { SetStatus("未找到Codex · 状态待确认"); return; }
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                SetStatus("正在连接 · 状态待确认");
                try { rpc = await CodexRpc.Start(executable, true, endpoint).ConfigureAwait(false); Authoritative = true; }
                catch when (!lifetime.IsCancellationRequested && endpoint.Length == 0)
                { rpc = await CodexRpc.Start(executable, false, "").ConfigureAwait(false); Authoritative = false; }
                epoch = Guid.NewGuid().ToString("N"); sequence = 0;
                rpc.Notification += Receive;
                SetStatus(Authoritative ? "服务已连接 · 按会话核实实时状态" : "历史服务可读 · 实时状态待确认");
                while (!lifetime.IsCancellationRequested)
                {
                    var connection = rpc;
                    foreach (var watch in Volatile.Read(ref watches))
                    {
                        var result = await connection.Call("thread/read", new { threadId = watch.ThreadId, includeTurns = false }).ConfigureAwait(false);
                        var thread = CodexState.Object(result, "thread"); var status = CodexState.Object(thread, "status");
                        var snapshot = JsonSerializer.SerializeToElement(new { method = "probe/snapshot", @params = new { threadId = watch.ThreadId, status } });
                        Emit(watch, snapshot, Authoritative);
                    }
                    // Liveness is checked even when no tasks are bound.
                    if (Volatile.Read(ref watches).Length == 0) await connection.Call("thread/loaded/list", new { }).ConfigureAwait(false);
                    await Task.Delay(750, lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) when (!lifetime.IsCancellationRequested)
            {
                foreach (var watch in Volatile.Read(ref watches)) Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/disconnected", @params = new { threadId = watch.ThreadId } }), true);
                SetStatus("连接中断 · 状态待确认");
            }
            finally { if (rpc is not null) { await rpc.DisposeAsync(); rpc = null; } Authoritative = false; }
            if (!lifetime.IsCancellationRequested) await Task.Delay(5000, lifetime.Token).ConfigureAwait(false);
        }
    }
    private void Emit(CodexWatch watch, JsonElement message, bool authoritative)
        => Event?.Invoke(watch, message, Interlocked.Increment(ref sequence), epoch, authoritative);
    private void Receive(JsonElement message)
    {
        var parameters = CodexState.Object(message, "params"); var threadId = CodexState.Text(parameters, "threadId");
        if (threadId.Length == 0) threadId = CodexState.Text(CodexState.Object(parameters, "thread"), "id");
        foreach (var watch in Volatile.Read(ref watches).Where(x => x.ThreadId == threadId)) Emit(watch, message, Authoritative);
    }
    internal async Task<CodexThread[]> Catalog()
    {
        var current = rpc; if (current is null) return [];
        var result = await current.Call("thread/list", new { limit = 40, archived = false, sortKey = "updated_at", sortDirection = "desc",sourceKinds=new[]{"appServer","cli","vscode","exec","unknown"},useStateDbOnly=true }).ConfigureAwait(false);
        var data = CodexState.Object(result, "data"); if (data.ValueKind != JsonValueKind.Array) return [];
        return data.EnumerateArray().Select(x => new CodexThread(CodexState.Text(x, "id"), CodexState.Text(x, "name"),
            CodexState.Object(x, "updatedAt").ValueKind == JsonValueKind.Number && CodexState.Object(x, "updatedAt").TryGetInt64(out var updated) ? updated : 0)).ToArray();
    }
    public async ValueTask DisposeAsync()
    { lifetime.Cancel(); if (rpc is not null) await rpc.DisposeAsync(); if (worker is not null) { try { await worker; } catch (OperationCanceledException) { } } }
}
