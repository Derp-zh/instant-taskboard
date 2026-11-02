using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

// Reads the verified state_5 directory schema only. No app-server, account,
// config execution, rollout/history reads, chat activation, or network fallback.
internal static class CodexLocalCatalog
{
    internal static Task<CodexThread[]> Load(string? home = null) => Task.Run(() => Read(home ??
        Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")));

    internal static string LocalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("Codex 本地数据目录必须是完整的本机路径，请检查 CODEX_HOME／sqlite_home。");
        path = Path.GetFullPath(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new IOException("Codex 数据目录不在本机磁盘，未访问网络目录。请检查 CODEX_HOME。");
        return path;
    }
    internal static string DatabaseHome(string home)
    {
        var config = Path.Combine(home, "config.toml");
        if (!File.Exists(config)) return home;
        // Only a top-level literal sqlite_home is relevant. Never execute config,
        // expand arbitrary variables, read auth, or guess profile-layer overrides.
        foreach (var raw in File.ReadLines(config))
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal)) break;
            var equals = line.IndexOf('=');
            if (equals < 0 || line[..equals].Trim() != "sqlite_home") continue;
            var value = line[(equals + 1)..].Trim(); string? path = null;
            if (value.StartsWith("'", StringComparison.Ordinal) && !value.StartsWith("'''", StringComparison.Ordinal))
            {
                var end = value.IndexOf('\'', 1);
                if (end > 0 && (value[(end + 1)..].Trim().Length == 0 || value[(end + 1)..].Trim().StartsWith("#", StringComparison.Ordinal))) path = value[1..end];
            }
            else if (value.StartsWith("\"", StringComparison.Ordinal) && !value.StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                    var reader = new Utf8JsonReader(bytes);
                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                    {
                        var tail = System.Text.Encoding.UTF8.GetString(bytes.AsSpan((int)reader.BytesConsumed)).Trim();
                        if (tail.Length == 0 || tail.StartsWith("#", StringComparison.Ordinal)) path = reader.GetString();
                    }
                }
                catch (JsonException) { }
            }
            if (string.IsNullOrWhiteSpace(path)) throw new IOException("sqlite_home 使用了尚不支持的配置格式，请查看诊断；不会读取其他目录或联网回退。");
            return LocalPath(path);
        }
        return home;
    }
    private static CodexThread[] Read(string home)
    {
        home = DatabaseHome(LocalPath(home));
        if (!Directory.Exists(home)) throw new IOException("尚未找到 Codex 本地目录。请正常打开 Codex 后重试；不会联网补取聊天。");
        if (Directory.EnumerateFiles(home, "state_*.sqlite", SearchOption.TopDirectoryOnly).Any(path =>
            int.TryParse(Path.GetFileNameWithoutExtension(path).AsSpan(6), out var version) && version > 5))
            throw new IOException("发现新版 Codex 本地目录结构，需要适配后读取；不会改用联网查询。");
        var path = Path.Combine(home, "state_5.sqlite");
        if (!File.Exists(path)) throw new IOException("尚未找到已支持的 Codex 本地聊天目录。请正常打开 Codex 后重试。");
        IntPtr db = IntPtr.Zero;
        try
        {
            // READONLY, never CREATE/READWRITE/immutable. WAL must remain visible.
            var code = Native.sqlite3_open_v2(path, out db, 1, IntPtr.Zero);
            if (code != 0) throw Error(code);
            Native.sqlite3_busy_timeout(db, 750);
            var columns = new HashSet<string>(StringComparer.Ordinal);
            Query(db, "PRAGMA table_info(threads)", row => columns.Add(Text(row, 1)));
            if (!new[] { "id", "title", "updated_at", "source", "archived" }.All(columns.Contains))
                throw new IOException("Codex 本地聊天目录结构暂不支持，请更新看板；未读取聊天正文或启动辅助服务。");
            var values = new List<CodexThread>();
            var name = columns.Contains("name") ? "COALESCE(NULLIF(name,''),title)" : "title";
            Query(db, "SELECT id," + name + ",updated_at FROM threads WHERE archived=0 AND source IN ('appServer','cli','vscode','exec','unknown') ORDER BY updated_at DESC,id ASC", row =>
            {
                var id = Text(row, 0);
                if (Guid.TryParseExact(id, "D", out _)) values.Add(new(id, Text(row, 1), Native.sqlite3_column_int64(row, 2)));
            });
            return values.ToArray();
        }
        finally { if (db != IntPtr.Zero) Native.sqlite3_close(db); }
    }

    private static void Query(IntPtr db, string sql, Action<IntPtr> consume)
    {
        IntPtr statement = IntPtr.Zero;
        try
        {
            var code = Native.sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero);
            if (code != 0) throw Error(code);
            while ((code = Native.sqlite3_step(statement)) == 100) consume(statement);
            if (code != 101) throw Error(code);
        }
        finally { if (statement != IntPtr.Zero) Native.sqlite3_finalize(statement); }
    }
    private static string Text(IntPtr statement, int column) => Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement, column)) ?? "";
    private static IOException Error(int code) => new($"Codex 本地聊天目录暂时无法只读打开（代码 {code}）。请稍后重试；未联网或修改目录。");
    private static class Native
    {
        private const string Library = "winsqlite3.dll";
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
