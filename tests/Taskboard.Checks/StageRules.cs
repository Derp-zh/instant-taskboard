using System.Text.Json;
using Taskboard.Core;

internal static class StageRules
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var engine = new BoardEngine(new BoardState());
        var first = engine.NewSession(Lane.Action);
        first.Working.AiIds = [engine.State.AiOptions[1].Id]; first.Working.PriorityId = engine.State.PriorityOptions[2].Id;
        first.Working.Reminder = new Reminder { Enabled = true, Repeat = Repeat.Weekly, Weekday = DayOfWeek.Friday, Time = TimeSpan.FromHours(12), NextLocal = DateTime.Today.AddDays(4).AddHours(12) };
        first.Working.Stages[0].Name = "命名阶段"; first.Working.Stages[0].Codex = new CodexBinding { ThreadId = Guid.NewGuid().ToString() };
        engine.StageSession(first);
        check(engine.State.Sessions.Count == 0, "状态、平台、优先级、提醒与会话本身不触发空任务暂存");
        first.Working.Title = "待操作草稿"; engine.StageSession(first);
        engine.AddStage(first).Text = "第二阶段详情"; first.Working.CurrentStageId = first.Working.Stages[1].Id;
        var expected = Copies.Of(first.Working);
        var second = engine.NewSession(Lane.Done); second.Working.Stages[0].Text = "已完成列表的新任务"; engine.StageSession(second);
        var store = new AtomicStore(Path.Combine(root, "stage-drafts")); await store.SaveAsync(engine.State);
        var loaded = store.Load();
        check(loaded.Sessions.Count == 2 && loaded.Sessions[1].Working.Lane == Lane.Done && Copies.Same(expected, loaded.Sessions[0].Working), "名称或详情启用暂存后，所有字段和两个列表的独立草稿跨重启完整保留");

        var old = new WorkItem { Title = "旧任务级关联", Lane = Lane.Done };
        old.Stages.Add(new Stage { Number = 1, Name = "阶段1" }); old.CurrentStageId = old.Stages[1].Id;
        old.Codex = new CodexBinding { ThreadId = "old-thread", Generation = 7, Phase = CodexPhase.WaitingForApproval, PreviousThreads = ["previous"], PendingRequests = ["approval"] };
        var oldJson = JsonSerializer.SerializeToNode(old)!;
        oldJson["Codex"]!.AsObject().Remove("BindingId");
        foreach (var stage in oldJson["Stages"]!.AsArray()) { stage!.AsObject().Remove("Codex"); stage.AsObject().Remove("IsRunning"); }
        var migrated = oldJson.Deserialize<WorkItem>()!;
        check(migrated.Codex is null && migrated.Stages[0].Codex is null && migrated.CurrentCodex is { ThreadId: "old-thread", Generation: 7, Phase: CodexPhase.WaitingForApproval } binding && binding.PreviousThreads.SequenceEqual(new[] { "previous" }) && binding.PendingRequests.SequenceEqual(new[] { "approval" }), "旧关联无损迁移到原当前阶段，不误绑阶段0");
        check(!JsonSerializer.Serialize(migrated).Contains("\"ColumnId\":null,\"Codex\":"), "迁移后的任务不再写回旧任务级关联");
        check(migrated.Stages[0].IsRunning == false && migrated.Stages[1].IsRunning == true, "旧当前阶段映射为运行中，其余阶段暂无进程");

        migrated.Stages[0].Codex = new CodexBinding { ThreadId = "stage-zero" };
        var signal = JsonSerializer.SerializeToElement(new { method = "turn/started", @params = new { threadId = "stage-zero", turn = new { id = "turn-noncurrent" } } });
        migrated.Lane = Lane.Action;
        check(CodexState.Apply(migrated, migrated.Stages[0].Id, signal, 1, 1, "test-stage", true) && migrated.Stages[0].Codex!.Phase == CodexPhase.Running && migrated.Lane == Lane.Action && migrated.CurrentCodex!.ThreadId == "old-thread", "暂无进程阶段更新自己的会话状态且不改变任务列表或其他阶段");
        migrated.Stages[0].IsRunning = true;
        var snapshot = JsonSerializer.SerializeToElement(new { method = "thread/status/changed", @params = new { threadId = "stage-zero", status = new { type = "active", activeFlags = Array.Empty<string>() } } });
        check(CodexState.Apply(migrated, snapshot, 2, 1, "test-stage", true) && migrated.Lane == Lane.Running, "标记运行后的可信新信号驱动任务列表；历史来源阶段状态不参与聚合");
        var wait = JsonSerializer.SerializeToElement(new { method = "tool/requestUserInput", @params = new { threadId = "old-thread" }, id = "question" });
        check(CodexState.Apply(migrated, migrated.Stages[1].Id, wait, 3, 7, "test-stage", true) && migrated.Lane == Lane.Action, "多个运行阶段中有一个等待回答，优先进入待操作");
        CodexState.Apply(migrated, snapshot, 4, 1, "test-stage", true);
        check(migrated.Lane == Lane.Action, "其他运行阶段继续执行时不会盖过已有的待操作请求");
        var completed = JsonSerializer.SerializeToElement(new { method = "turn/completed", @params = new { threadId = "old-thread", turn = new { id = "done-stage-1", status = "completed" } } });
        CodexState.Apply(migrated, migrated.Stages[1].Id, completed, 5, 7, "test-stage", true);
        CodexState.Apply(migrated, snapshot, 6, 1, "test-stage", true);
        check(migrated.Lane == Lane.Action && migrated.Stages[1].Codex!.Phase == CodexPhase.ReadyForReview, "一个阶段结束待检查时，另一阶段执行仍保持待操作优先");
        migrated.Stages[1].IsRunning = false;
        CodexState.Apply(migrated, snapshot, 7, 1, "test-stage", true);
        check(migrated.Lane == Lane.Running, "暂停阶段的待处理会话不再参与任务列表聚合");
        foreach (var stage in migrated.Stages) stage.IsRunning = false;
        var repaired = Copies.Of(migrated);
        check(repaired.Stages.Count(x => x.IsRunning == true) == 1 && repaired.Stages[1].IsRunning == true && repaired.Stages.Count == 2, "读取早期全暂停副本时恢复原定位阶段，至少保留一个运行阶段且不删信息");
        migrated.Stages[0].IsRunning = true;
        migrated.Title = ""; migrated.Stages[1].Text = "隐藏阶段不应泄露为自动标题";
        check(migrated.DisplayTitle == "未命名任务", "未填标题且只有暂停阶段有文字时，不把隐藏详情当作可见标题");
        migrated.Title = "旧任务级关联";
        var singlePaused = new WorkItem { Title = "单阶段" };
        check(!BoardEngine.CanSetStageRunning(singlePaused, singlePaused.Stages[0].Id, false) && BoardEngine.CardStages(singlePaused).Count == 1, "单阶段任务不能关闭唯一运行标记，详情入口始终保留");
        var many = new WorkItem { Stages = Enumerable.Range(0, 7).Select(x => new Stage { Number = x, IsRunning = true }).ToList() };
        many.CurrentStageId = many.Stages[0].Id;
        check(BoardEngine.CardStages(many).Count == 7, "运行阶段显示不再受旧四阶段摘要数量上限限制");
        migrated.Stages[0].IsRunning = true; migrated.Stages[1].IsRunning = true;
        migrated.CurrentStageId = migrated.Stages[1].Id; migrated.Lane = Lane.Done;
        engine.State.Tasks.Add(migrated);
        var edit = engine.Edit(migrated.Id);
        edit.Working.Stages[0].Text = "提交修改的详情";
        edit.Working.Stages[1].Name = "修改阶段名";
        migrated.Stages[0].Codex!.Phase = CodexPhase.Running;
        migrated.CurrentCodex!.Phase = CodexPhase.Failed;
        var result = engine.Submit(edit);
        check(result.Stages[0].Text == "提交修改的详情" && result.Stages[0].Codex!.Phase == CodexPhase.Running && result.CurrentCodex!.Phase == CodexPhase.Failed, "阶段详情和名称提交保留每个阶段最新后台状态");
        edit = engine.Edit(result.Id); edit.Working.Stages[0].Codex = new CodexBinding { ThreadId = "replacement" };
        result = engine.Submit(edit);
        check(result.Stages[0].Codex is { ThreadId: "replacement", Generation: 2, Phase: CodexPhase.Unknown } rebound && rebound.PreviousThreads.Contains("stage-zero") && result.CurrentCodex!.ThreadId == "old-thread", "改绑只重置对应阶段，保留旧会话历史且不改其他阶段");
        engine.Undo();
        check(engine.State.Tasks.Single().Stages[0].Codex!.ThreadId == "stage-zero" && engine.State.Tasks.Single().CurrentCodex!.ThreadId == "old-thread", "撤销阶段改绑恢复对应关联并保留其他阶段");
        engine.Redo();
        check(engine.State.Tasks.Single().Stages[0].Codex!.ThreadId == "replacement", "重做阶段改绑恢复新关联");
        engine.Archive(result.Id); var restored = engine.Unarchive(result.Id);
        check(restored.Stages.All(x => x.Codex is { Phase: CodexPhase.Unknown, SourceEpoch: "", LastSequence: -1 }), "归档恢复重置所有阶段的监听代次，保留关联内容");
        await store.SaveAsync(engine.State); var persisted = store.Load().Tasks.Single();
        check(persisted.Stages.Select(x => x.Codex!.ThreadId).SequenceEqual(new[] { "replacement", "old-thread" }), "多阶段关联通过正式保存、归档、取消归档和磁盘重读");
        var previousBindings = restored.Stages.Select(x => x.Codex!.BindingId).ToArray(); engine.Delete(restored.Id); engine.Undo();
        var recovered = engine.State.Tasks.Single();
        check(recovered.Stages.Select(x => x.Codex!.ThreadId).SequenceEqual(new[] { "replacement", "old-thread" }) && recovered.Stages.Select(x => x.Codex!.BindingId).Zip(previousBindings).All(x => x.First != x.Second), "撤销删除保留每阶段会话且更换监听身份，拒绝删除前的排队信号");
        edit = engine.Edit(recovered.Id); var added = engine.AddStage(edit); added.Codex = new CodexBinding { ThreadId = "new-stage-thread" };
        var addedIdentity = engine.Submit(edit).Stages[^1].Codex!.BindingId; engine.Undo(); engine.Redo();
        check(engine.State.Tasks.Single().Stages[^1].Codex is { ThreadId: "new-stage-thread" } revived && revived.BindingId != addedIdentity && revived.Phase == CodexPhase.Unknown, "重做已移除阶段时重新核实关联，旧阶段排队信号不可套用");
        edit = engine.Edit(recovered.Id); edit.Working.Title = "保留后台阶段开关";
        engine.SetStageRunning(recovered.Id, edit.Working.Stages[0].Id, false);
        check(engine.Submit(edit).Stages[0].IsRunning == false, "提交未改阶段开关的旧编辑副本时保留列表上最新的运行标记");
        var live = engine.State.Tasks.Single(); var beforeLast = Copies.Of(live); var last = live.Stages.Single(x => x.IsRunning == true);
        check(!engine.SetStageRunning(live.Id, last.Id, false) && Copies.Same(beforeLast, live), "正式卡片拒绝关闭最后运行阶段，不改内容或历史");
        edit = engine.Edit(live.Id); foreach (var stage in edit.Working.Stages) stage.IsRunning = false;
        try { engine.Submit(edit); throw new Exception("允许提交全暂停任务"); }
        catch (InvalidOperationException error) { check(error.Message == BoardEngine.LastRunningStageMessage && Copies.Same(beforeLast, engine.State.Tasks.Single()), "正式保存拒绝全暂停编辑，保留原任务"); }
        engine.Discard(edit);
        var both = Copies.Of(live); foreach (var stage in both.Stages) stage.IsRunning = stage.Number is 0 or 1;
        var editorVersion = Copies.Of(both); editorVersion.Stages[0].IsRunning = false;
        var listVersion = Copies.Of(both); listVersion.Stages[1].IsRunning = false;
        var merged = BoardEngine.Merge(both, editorVersion, listVersion);
        check(merged.Stages.Count(x => x.IsRunning == true) == 1 && merged.Stages[1].IsRunning == true, "编辑和列表并发关闭不同阶段时，合并仍保留编辑侧选定的运行阶段");
        var prefs = new Preferences();
        check(prefs.InspirationQuickEntry && prefs.Hotkey.Enabled && prefs.Hotkey.Modifiers == 2 && prefs.Hotkey.VirtualKey == 0xC0, "快捷卡片默认开启，默认快捷键按物理Ctrl和波浪键定义");
        check(!new BoardHotkey { Modifiers = 0 }.IsValid && !new BoardHotkey { VirtualKey = 0x7B }.IsValid && !new BoardHotkey { VirtualKey = 162 }.IsValid, "拒绝无修饰键、保留F12以及纯修饰键注册");
    }
}
