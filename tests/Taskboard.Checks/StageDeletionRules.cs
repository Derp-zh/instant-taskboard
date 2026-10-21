using System.Text.Json;
using Taskboard.Core;

internal static class StageDeletionRules
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var engine = new BoardEngine(new()); var session = engine.NewSession(Lane.Running); session.Working.Title = "阶段删除隔离样本";
        var first = session.Working.Stages[0];
        check(!engine.RemoveStage(session, first.Id, out var reason) && reason == BoardEngine.LastStageMessage && session.Working.Stages.Count == 1 && !engine.CanUndo, "最后一个阶段不能删除且不产生撤销记录");
        var second = engine.AddStage(session); second.Text = "保留详情"; second.Codex = new() { ThreadId = "retained", AwaitingUserAction = true };
        check(!engine.RemoveStage(session, first.Id, out reason) && reason == BoardEngine.LastRunningStageMessage && session.Working.Stages.Count == 2, "多阶段时不能删除唯一运行阶段，不自动替用户启动其他阶段");
        second.IsRunning = true;
        check(engine.RemoveStage(session, first.Id, out _) && session.Working.CurrentStageId == second.Id && second.Number == 1 && second.Text == "保留详情" && second.Codex!.ThreadId == "retained", "删除原定位阶段后仍有效定位，保留其他编号详情和会话");
        var third = engine.AddStage(session); third.IsRunning = true; third.Text = "后台保护"; third.Codex = new() { ThreadId = "background", Phase = CodexPhase.Running };
        var item = engine.Submit(session); var edit = engine.Edit(item.Id);
        check(engine.RemoveStage(edit, second.Id, out _) && engine.State.Tasks.Single().Stages.Count == 2, "编辑删除只改副本，未保存不影响正式阶段");
        engine.Discard(edit); check(engine.State.Tasks.Single().Stages.Count == 2, "取消编辑保留正式阶段");
        edit = engine.Edit(item.Id); engine.RemoveStage(edit, second.Id, out _);
        var live = engine.State.Tasks.Single().Stages.Single(x => x.Id == third.Id);
        live.Codex!.Phase = CodexPhase.WaitingForApproval; live.Codex.AwaitingUserAction = true; live.Codex.PendingRequests = ["approval-late"];
        var saved = engine.Submit(edit);
        check(saved.Stages.Count == 1 && saved.Stages[0].Id == third.Id && saved.Stages[0].Codex!.PendingRequests.SequenceEqual(new[] { "approval-late" }), "保存删除准确移除阶段且保留其他阶段后台晚到等待");
        File.WriteAllText(Path.Combine(root, "stage-deletion-before-save.json"), JsonSerializer.Serialize(engine.State));
        var store = new AtomicStore(Path.Combine(root, "stage-deletion")); await store.SaveAsync(engine.State); Validation.Check(store.Load());
        check(store.Load().Tasks.Single().Stages.Single().Id == third.Id, "删除阶段和剩余绑定实际落盘后独立回读");
        engine.Undo(); check(engine.State.Tasks.Single().Stages.Count == 2 && engine.State.Tasks.Single().Stages.Single(x => x.Id == second.Id).Codex?.Phase == CodexPhase.Unknown && engine.State.Tasks.Single().Stages.Single(x => x.Id == third.Id).Codex!.PendingRequests.Contains("approval-late"), "撤销删除重置恢复阶段的旧信任，保留未删阶段的后台等待");
        engine.Redo(); check(engine.State.Tasks.Single().Stages.Single().Id == third.Id, "恢复操作重做阶段删除");

        var old = new BoardState();
        foreach (var column in old.Preferences.Columns.Where(x => x.BuiltinLane is Lane.Refining or Lane.Paused or Lane.Done)) { column.Deleted = true; column.Visible = false; column.Name += "原名"; }
        var migrated = JsonSerializer.Deserialize<BoardState>(JsonSerializer.Serialize(old))!; Validation.Check(migrated);
        check(migrated.Preferences.Columns.Count(x => x.Protected && !x.Deleted) == 6 && migrated.Preferences.Columns.Where(x => x.BuiltinLane is Lane.Refining or Lane.Paused or Lane.Done).All(x => !x.Visible && x.Name.EndsWith("原名")), "旧版可删除内置列无损恢复为隐藏，保留名称和身份");
        foreach (var column in migrated.Preferences.Columns)
        {
            var refused = false; try { BoardColumns.Delete(migrated, column); } catch (InvalidOperationException) { refused = true; }
            check(refused && !column.Deleted, "全部内置列禁止删除：" + column.BuiltinLane);
        }
        var custom = new BoardColumn(); migrated.Preferences.Columns.Add(custom); BoardColumns.Delete(migrated, custom);
        check(custom.Deleted && !custom.Visible, "空自定义列表仍可删除");
        var protectedMissing = new BoardState(); protectedMissing.Preferences.Columns.RemoveAll(x => x.BuiltinLane == Lane.Running);
        var rejected = false; try { Validation.Check(JsonSerializer.Deserialize<BoardState>(JsonSerializer.Serialize(protectedMissing))!); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "新迁移不掩盖原受保护列缺失的损坏数据");
    }
}
