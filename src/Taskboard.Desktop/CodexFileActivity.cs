using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Taskboard.Desktop;

// A projection of the Codex files that already exist on this machine. Reading it
// opens no socket, starts no app-server, loads no conversation and registers no
// stream follower, so the desktop's idle release is never affected.
internal sealed record CodexActivity(string TurnId, string Status, long Ordinal, bool ProjectionCaughtUp, long RolloutBytes, long RolloutWriteTicks);

internal interface ICodexActivitySource
{
    IReadOnlyDictionary<string, CodexActivity> Read(IReadOnlyCollection<string> threadIds);
}

internal sealed class CodexFileActivity(string? home = null) : ICodexActivitySource
{
    private static readonly string[] Supported = ["inProgress", "completed", "failed", "interrupted"];

    public IReadOnlyDictionary<string, CodexActivity> Read(IReadOnlyCollection<string> threadIds)
    {
        var result = new Dictionary<string, CodexActivity>(StringComparer.Ordinal);
        var ids = threadIds.Where(x => Guid.TryParseExact(x, "D", out _)).Select(x => x.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return result;
        var directory = CodexLocalCatalog.DatabaseHome(CodexLocalCatalog.LocalPath(home ??
            Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")));
        var state = Path.Combine(directory, "state_5.sqlite");
        var history = Path.Combine(directory, "thread_history_1.sqlite");
        if (!File.Exists(state)) throw new IOException("尚未找到已支持的 Codex 本地聊天目录。请正常打开 Codex 后重试；不订阅状态，也不联网补取。");
        if (!File.Exists(history)) throw new IOException("尚未找到 Codex 本地轮次记录，暂时无法在不订阅的情况下判断轮次状态；未改用订阅或联网查询。");
        var list = string.Join(",", ids.Select(x => "'" + x + "'"));
        var rollouts = new Dictionary<string, string>(StringComparer.Ordinal);
        var turns = new Dictionary<string, (string Turn, string Status, long Ordinal)>(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>(StringComparer.Ordinal);
        using (var db = Database.Open(state))
            db.Query($"SELECT id,rollout_path FROM threads WHERE id IN ({list})", row =>
            {
                var id = db.Text(row, 0).ToLowerInvariant(); var path = db.Text(row, 1);
                if (Guid.TryParseExact(id, "D", out _) && path.Length > 0) rollouts[id] = path;
            });
        using (var db = Database.Open(history))
        {
            // The turn and tailer watermark must belong to one committed read
            // snapshot. Separate autocommit statements can straddle a writer.
            db.Query("BEGIN", _ => { });
            db.Require("thread_turns", "thread_id", "turn_id", "status", "rollout_ordinal");
            db.Query($"SELECT thread_id,turn_id,status,rollout_ordinal FROM thread_turns WHERE thread_id IN ({list}) ORDER BY rollout_ordinal DESC", row =>
            {
                var id = db.Text(row, 0).ToLowerInvariant();
                if (!turns.ContainsKey(id)) turns[id] = (db.Text(row, 1), db.Text(row, 2), db.Number(row, 3));
            });
            db.Require("thread_history_projection_state", "thread_id", "next_rollout_byte_offset");
            db.Query($"SELECT thread_id,next_rollout_byte_offset FROM thread_history_projection_state WHERE thread_id IN ({list})", row =>
                offsets[db.Text(row, 0).ToLowerInvariant()] = db.Number(row, 1));
            db.Query("COMMIT", _ => { });
        }
        foreach (var (id, path) in rollouts)
        {
            // Unknown schema, unknown status or a missing rollout file stays unknown:
            // never guess a turn, never fall back to a subscription to fill the gap.
            if (!turns.TryGetValue(id, out var turn) || !Supported.Contains(turn.Status) || turn.Turn.Length == 0) continue;
            long bytes, ticks;
            try
            {
                var info = new FileInfo(CodexLocalCatalog.LocalPath(path));
                if (!info.Exists) continue;
                bytes = info.Length; ticks = info.LastWriteTimeUtc.Ticks;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { continue; }
            // An offset beyond EOF also indicates stale/truncated projection,
            // rather than proof that this file's current generation was read.
            result[id] = new(turn.Turn, turn.Status, turn.Ordinal, offsets.TryGetValue(id, out var offset) && offset == bytes, bytes, ticks);
        }
        return result;
    }

    // READONLY only, WAL visible, bounded busy wait, always closed before return.
    private sealed class Database : IDisposable
    {
        private IntPtr handle;
        private Database(IntPtr value) => handle = value;
        internal static Database Open(string path)
        {
            var code = Native.sqlite3_open_v2(path, out var db, 1, IntPtr.Zero);
            if (code != 0 || db == IntPtr.Zero)
            {
                if (db != IntPtr.Zero) Native.sqlite3_close(db);
                throw new IOException($"Codex 本地状态文件暂时无法只读打开（代码 {code}）。请稍后重试；未联网、未修改文件，也未改用订阅。");
            }
            Native.sqlite3_busy_timeout(db, 750);
            return new(db);
        }
        internal void Require(string table, params string[] columns)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            Query($"PRAGMA table_info({table})", row => found.Add(Text(row, 1)));
            if (found.Count == 0) throw new IOException($"Codex 本地状态缺少 {table} 表，需要适配后读取；不会改用订阅或联网查询。");
            if (columns.Any(x => !found.Contains(x))) throw new IOException($"Codex 本地状态 {table} 结构暂不支持，需要适配后读取；不会改用订阅或联网查询。");
        }
        internal void Query(string sql, Action<IntPtr> consume)
        {
            var code = Native.sqlite3_prepare_v2(handle, sql, -1, out var statement, IntPtr.Zero);
            if (code != 0) throw new IOException($"Codex 本地状态查询失败（代码 {code}）；未改用订阅或联网查询。");
            try
            {
                while ((code = Native.sqlite3_step(statement)) == 100) consume(statement);
                if (code != 101) throw new IOException($"Codex 本地状态暂时无法只读读取（代码 {code}）。请稍后重试；未联网或修改文件。");
            }
            finally { Native.sqlite3_finalize(statement); }
        }
        internal string Text(IntPtr statement, int column) => Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement, column)) ?? "";
        internal long Number(IntPtr statement, int column) => Native.sqlite3_column_int64(statement, column);
        public void Dispose()
        {
            if (handle != IntPtr.Zero) Native.sqlite3_close(handle);
            handle = IntPtr.Zero;
        }
        private const string Library = "winsqlite3.dll";
        private static class Native
        {
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern int sqlite3_close(IntPtr db);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern int sqlite3_busy_timeout(IntPtr db, int ms);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            internal static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern int sqlite3_step(IntPtr statement);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern int sqlite3_finalize(IntPtr statement);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern long sqlite3_column_int64(IntPtr statement, int column);
        }
    }
}
