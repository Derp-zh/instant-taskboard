using System.Text.Json;
using System.Reflection;
using System.Runtime.Loader;
using Taskboard.Desktop;

internal static class LocalCatalogChecks
{
    internal static async Task Run(string output)
    {
        var root = Environment.GetEnvironmentVariable("CODEX_HOME") ?? throw new Exception("isolated CODEX_HOME required");
        if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new Exception("fixture must be under explicit --data-dir");
        var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        async Task Reject(string name, string reason)
        {
            try { await CodexLocalCatalog.Load(Path.Combine(root, name)); throw new Exception("unexpected success: " + name); }
            catch (IOException e) { Check(e.Message.Contains(reason), name + " 明确失败，无联网回退"); }
        }
        var data = await CodexLocalCatalog.Load();
        Check(data.Length == 205, "超过两页目录完整读取，过滤归档/子代理/无效编号");
        Check(data[0].Name == "手动命名 · 中文😀", "UTF8手动名称优先");
        Check(data[1].Name == "标题回退", "空名称回退标题");
        Check(data.Zip(data.Skip(1)).All(x => x.First.UpdatedAt >= x.Second.UpdatedAt), "更新时间降序");
        Check(data.Select(x => x.Id).Distinct().Count() == data.Length, "完整编号不重复");
        await using (var observer = new CodexObserver())
            Check((await observer.Catalog()).Select(x => x.Id).SequenceEqual(data.Select(x => x.Id)), "普通Observer默认目录走SQLite，无需启动CLI");
        if (Environment.GetEnvironmentVariable("TASKBOARD_PACKAGE_ASSEMBLY") is { Length: > 0 } package)
        {
            // Product uses package dependencies (including Json 9), while this
            // isolated runner uses framework Json 8. Keep their contexts apart.
            var context = new PackageContext(package);
            var assembly = context.LoadFromAssemblyPath(package);
            var expected = Environment.GetEnvironmentVariable("TASKBOARD_PACKAGE_VERSION") ?? "0.9.28.0";
            Check(assembly.GetName().Version!.ToString() == expected, "实际加载运行包程序集 " + expected);
            var type = assembly.GetType("Taskboard.Desktop.CodexObserver", true)!;
            var ctor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            var product = (IAsyncDisposable)ctor.Invoke(new object?[ctor.GetParameters().Length]);
            await using (product)
            {
                var load = (Task)type.GetMethod("Catalog", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(product, null)!;
                try { await load; }
                catch (Exception problem)
                {
                    Console.WriteLine("package-error-kind: " + problem.GetType().FullName);
                    Console.WriteLine("package-error-code: " + problem.HResult);
                    try { Console.WriteLine("package-error-message: " + problem.Message); } catch { }
                    throw new InvalidOperationException("Packaged catalog failed in reflection host; see stdout diagnostics");
                }
                var rows = (Array)load.GetType().GetProperty("Result")!.GetValue(load)!;
                Check(rows.Length == data.Length && (string)rows.GetValue(0)!.GetType().GetProperty("Id")!.GetValue(rows.GetValue(0))! == data[0].Id,
                    "完整运行包默认Observer.Catalog准确读取隔离SQLite，不启CLI");
            }
            context.Unload();
        }
        Check((await CodexLocalCatalog.Load(Path.Combine(root, "legacy"))).Single().Name == "旧结构标题", "已知结构缺少可选name仍可读");
        Check((await CodexLocalCatalog.Load(Path.Combine(root, "empty"))).Length == 0, "合法空目录成功");
        Check((await CodexLocalCatalog.Load(Path.Combine(root, "wal"))).Single().Name == "已提交仍在WAL", "正确读取已提交WAL，无immutable或主库复制");
        await Reject("missing", "尚未找到"); await Reject("corrupt", "只读");
        await Reject("schema", "结构暂不支持"); await Reject("future", "新版");
        Check((await CodexLocalCatalog.Load(Path.Combine(root, "custom"))).Single().Name == "自定义本机库", "尊重已有sqlite_home中文路径，不读取旧默认库");
        await Reject("custom-unsupported", "配置格式"); await Reject("custom-network", "不在本机");
        try { await CodexLocalCatalog.Load(@"\\127.0.0.1\never-touch\codex"); throw new Exception("network path accepted"); }
        catch (IOException e) { Check(e.Message.Contains("不在本机"), "UNC目录在文件访问前拒绝"); }
        var attempts = 0;
        await using (var catalog = new CodexCatalog(readLocal: () => ++attempts < 3 ? throw new IOException("locked") : Task.FromResult(data)))
        {
            try { await catalog.Load(); throw new Exception("expected failure"); } catch (IOException) { }
            Check(catalog.Failed && attempts == 2, "失败有界重试，不转CLI");
            Check((await catalog.Load()).Length == data.Length && !catalog.Failed, "下次操作恢复，不缓存失败");
        }
        await using (var catalog = new CodexCatalog())
            for (var i = 0; i < 5; i++) Check((await catalog.Load()).Length == data.Length, "连续本地读取 " + i);
        if (Environment.GetEnvironmentVariable("TASKBOARD_CATALOG_IDLE_PROBE") == "1")
            await Task.Delay(6000); // isolated lifetime observation only
        var report = new { passed = checks.Count, checks, catalogCount = data.Length, source = "production Observer default + native SQLite", desktopConnected = false, realAccountUsed = false, monitoringRetentionFixed = false };
        await File.WriteAllTextAsync(Path.Combine(output, "local-catalog-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    private sealed class PackageContext(string package) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is not ("Taskboard.Core" or "System.Text.Json" or "System.Text.Encodings.Web")) return null;
            var path = Path.Combine(Path.GetDirectoryName(package)!, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
