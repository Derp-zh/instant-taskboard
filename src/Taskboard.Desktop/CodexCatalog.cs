using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Normal directory reads are direct local SQLite. RPC injection is retained
// for isolated protocol checks; no production app-server/network fallback.
internal sealed class CodexCatalog(Func<Task<ICodexCatalogRpc>>? connect = null, Func<Task<CodexThread[]>>? readLocal = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ICodexCatalogRpc? connection;
    internal bool Failed { get; private set; }
    internal async Task<CodexThread[]> Load()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (connect is null)
                    {
                        var local = await (readLocal ?? (() => CodexLocalCatalog.Load()))().ConfigureAwait(false);
                        Failed = false; return local;
                    }
                    if (connection is { IsConnected: false }) { await connection.DisposeAsync().ConfigureAwait(false); connection = null; }
                    connection ??= await connect().ConfigureAwait(false);
                    var values = new List<CodexThread>(); string? cursor = null; var seen = new HashSet<string>(StringComparer.Ordinal);
                    do
                    {
                        var result = await connection.Call("thread/list", new { cursor, limit = 100, archived = false, sortKey = "updated_at", sortDirection = "desc",
                            sourceKinds = new[] { "appServer", "cli", "vscode", "exec", "unknown" }, useStateDbOnly = true }).ConfigureAwait(false);
                        var data = CodexState.Object(result, "data");
                        if (data.ValueKind != JsonValueKind.Array) throw new IOException("Codex 本地会话目录格式无效");
                        values.AddRange(data.EnumerateArray().Select(x => new CodexThread(CodexState.Text(x, "id"),
                            CodexState.Text(x, "name") is { Length: > 0 } name ? name : CodexState.Text(x, "title"),
                            CodexState.Object(x, "updatedAt").ValueKind == JsonValueKind.Number && CodexState.Object(x, "updatedAt").TryGetInt64(out var updated) ? updated : 0)));
                        cursor = CodexState.Text(result, "nextCursor");
                    } while (cursor.Length > 0 && seen.Add(cursor));
                    Failed = false; return values.DistinctBy(x => x.Id).ToArray();
                }
                catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or OperationCanceledException or
                    UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Net.WebSockets.WebSocketException)
                {
                    if (connection is not null) { await connection.DisposeAsync().ConfigureAwait(false); connection = null; }
                    if (attempt == 0) continue;
                    Failed = true; throw new IOException("Codex 会话列表暂不可用，已自动重试；请稍后再次点击绑定会话。", e);
                }
            }
        }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); connection = null; }
        finally { gate.Release(); }
    }
}
