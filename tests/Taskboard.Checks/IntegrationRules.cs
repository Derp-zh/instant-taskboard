using Taskboard.Core;

internal static class IntegrationRules
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var engine = new BoardEngine(new BoardState());
        WorkItem Add(string title, Lane lane)
        { var draft = engine.NewSession(lane); draft.Working.Title = title; draft.Working.Stages[0].Text = "完整详情"; engine.AddStage(draft); draft.Working.Stages[1].Text = "另一阶段"; return engine.Submit(draft); }
        var first = Add("历史任务", Lane.Done); var id = first.Id;
        engine.Move(id, Lane.Paused, null); engine.Undo(); check(engine.CanRedo && engine.State.Tasks.Single().Lane == Lane.Done, "撤销跨列移动后可恢复");
        engine.Redo(); check(engine.State.Tasks.Single().Lane == Lane.Paused && engine.CanUndo && !engine.CanRedo, "恢复跨列移动并返回撤销栈");
        engine.Delete(id); engine.Undo(); engine.Redo(); check(engine.State.Tasks.Count == 0 && engine.State.Archives.Count == 0, "恢复删除不产生归档");
        engine.Undo(); var edit = engine.Edit(id); edit.Working.Title = "字段修改"; engine.Submit(edit); engine.Undo(); engine.Redo();
        check(engine.State.Tasks.Single().Title == "字段修改", "任务字段撤销与恢复成对工作");
        engine.Undo(); Add("新任务清空恢复", Lane.Running); check(!engine.CanRedo, "产生新正式任务清空恢复分支");
        engine.Archive(id); check(engine.State.Archives.Count == 1 && engine.State.Tasks.All(x => x.Id != id) && engine.State.Archives[0].Item.Stages.Count == 2, "归档移出活动列表并保存全部阶段");
        var column = engine.State.Preferences.Columns.Single(x => x.BuiltinLane == Lane.Paused); column.Deleted = true;
        var restored = engine.Unarchive(id); check(restored.Id == id && restored.Stages.Count == 2 && !column.Deleted && column.Visible, "取消归档恢复原身份、阶段及已删除原列表");
        engine.Archive(id); var oldest = id;
        for (var i = 0; i < 20; i++) engine.Archive(Add("归档" + i, Lane.Done).Id);
        check(engine.State.Archives.Count == 20 && engine.State.Archives.All(x => x.Item.Id != oldest), "第21条归档永久移除最早记录且维持20条");
        var running = engine.State.Tasks.Single(x => x.Lane == Lane.Running);
        try { engine.Archive(running.Id); throw new Exception("未拒绝非法归档"); } catch (InvalidOperationException) { check(true, "进行中任务不能归档"); }
        var archiveStore = new AtomicStore(Path.Combine(root, "archive-store")); await archiveStore.SaveAsync(Copies.Of(engine.State));
        check(archiveStore.Load().Archives.Count == 20, "归档历史独立持久保存并在重启恢复");
        var configuration = ConfigurationBackup.Capture(engine.State, root); var backupFile = Path.Combine(root, "archive-safe-config.json"); configuration.Write(backupFile);
        check(!File.ReadAllText(backupFile).Contains("归档19") && !File.ReadAllText(backupFile).Contains("另一阶段"), "配置备份不含历史归档任务或阶段正文");
        var unrelatedConfiguration = ConfigurationBackup.Capture(new BoardState(), root);
        check(unrelatedConfiguration.Apply(engine.State).Tasks.Count == engine.State.Tasks.Count, "不同默认列表编号的配置导入仍保留任务且不重复内置角色");
        var split = new AtomicStore(Path.Combine(root, "split-data"), Path.Combine(root, "split-config")); await split.SaveAsync(Copies.Of(engine.State));
        check(File.Exists(split.MainPath) && File.Exists(split.ConfigurationPath) && !File.ReadAllText(split.ConfigurationPath!).Contains("归档19"), "任务与配置分开保存且配置不含历史任务");
        check(split.Load().Archives.Count == 20 && split.Load().Tasks.Count == engine.State.Tasks.Count, "分离配置读取后活动任务和历史仍完整");
        var saved = ConfigurationBackup.Read(split.ConfigurationPath!); saved.PersistenceRevision = Guid.NewGuid(); saved.Preferences.GlassTransparency = 88; saved.Write(split.ConfigurationPath!);
        check(split.Load().Preferences.GlassTransparency == engine.State.Preferences.GlassTransparency && split.RecoveryMessage is not null, "中断两文件保存时优先任务内恢复配置不丢任务");
        await split.SaveAsync(Copies.Of(engine.State)); await split.SaveAsync(Copies.Of(engine.State)); File.WriteAllText(split.ConfigurationPath!, "corrupt");
        check(split.Load().Archives.Count == 20 && split.RecoveryMessage!.Contains("备份"), "配置损坏从配置备份恢复且历史任务不丢失");
        File.WriteAllText(split.ConfigurationPath!, "bad"); File.WriteAllText(split.ConfigurationPath! + ".bak", "bad backup");
        check(split.Load().Archives.Count == 20 && split.RecoveryMessage!.Contains("任务文件"), "两份配置都损坏时从完整任务副本恢复设置");
        var missing = new AtomicStore(Path.Combine(root, "missing-task"), Path.Combine(root, "missing-task-config")); await missing.SaveAsync(Copies.Of(engine.State)); File.Delete(missing.MainPath);
        try { _ = missing.Load(); throw new Exception("未阻止空任务替代"); } catch (InvalidDataException) { check(true, "已保存目录的任务和备份缺失时拒绝创建空数据替代"); }
        var rows = new List<StartupInstallation>(); var when = DateTime.UtcNow;
        StartupCatalog.Register(rows, "older.exe", new Version(0, 8, 2), when); StartupCatalog.Register(rows, "newer.exe", new Version(0, 9, 0), when);
        StartupCatalog.Register(rows, "older.exe", new Version(0, 8, 2), when.AddDays(1));
        check(StartupCatalog.Latest(rows, _ => true)!.Executable == "newer.exe", "再次登记旧版本不能降级最新自启目标");
        check(StartupCatalog.Latest(rows, x => x != "newer.exe")!.Executable == "older.exe", "最新文件已移除时选择仍存在的最高版本");
        StartupCatalog.Register(rows, "newest-build.exe", new Version(0, 9, 0), when.AddHours(1));
        check(StartupCatalog.Latest(rows, _ => true)!.Executable == "newest-build.exe", "相同版本优先较新构建");
        var ordered = new BoardEngine(new BoardState()); var a = ordered.NewSession(Lane.Action); a.Working.Title = "A"; ordered.Submit(a); var b = ordered.NewSession(Lane.Action); b.Working.Title = "B"; ordered.Submit(b);
        ordered.Move(a.Working.Id, Lane.Action, null); ordered.Undo(); ordered.Redo(); check(ordered.State.Tasks.Last().Id == a.Working.Id, "恢复列内排序恢复原落点");
        for (var i = 0; i < 25; i++) ordered.Move(a.Working.Id, i % 2 == 0 ? Lane.Running : Lane.Action, null);
        var reversals = 0; while (ordered.CanUndo) { ordered.Undo(); reversals++; }
        check(reversals == 20, "撤销最多保留本次运行20步"); var redos = 0; while (ordered.CanRedo) { ordered.Redo(); redos++; }
        check(redos == 20, "恢复与撤销保持相同20步运行内边界");
    }
}
