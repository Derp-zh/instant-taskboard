using Taskboard.Core;

internal static class ArchivePolicyRules
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var engine = new BoardEngine(new BoardState()); var prefs = engine.State.Preferences;
        check(prefs.AutoArchiveCompleted && prefs.AutoArchiveAfterDays == 5 && !prefs.AutoDeleteArchives, "归档默认五天开启、超40自动删除默认关闭");
        WorkItem Add(string title, Lane lane) { var s = engine.NewSession(lane); s.Working.Title = title; return engine.Submit(s); }
        var item = Add("完成计时", Lane.Done); var now = DateTime.UtcNow;
        check(item.CompletedSinceUtc is not null, "进入已完成列表记录独立完成时间");
        item.CompletedSinceUtc = now.AddDays(-4); var expected = item.CompletedSinceUtc;
        var edit = engine.Edit(item.Id); edit.Working.Title = "编辑不重置"; item = engine.Submit(edit);
        check(item.CompletedSinceUtc == expected, "编辑已完成任务不重置完成时间");
        engine.Move(item.Id, Lane.Action, null); check(item.CompletedSinceUtc is null, "离开已完成清除完成计时");
        engine.Move(item.Id, Lane.Done, null); check(item.CompletedSinceUtc >= now, "重新完成重新计时");
        item.CompletedSinceUtc = now.AddDays(-5);
        check(engine.DueArchives(now).Length == 0 && engine.DueArchives(now.AddTicks(1)).Contains(item.Id), "严格超过指定整天数才归档");
        prefs.AutoArchiveAfterDays = 7; check(engine.DueArchives(now.AddDays(1)).Length == 0, "自定义七天门槛生效");
        prefs.AutoArchiveAfterDays = 3; check(engine.DueArchives(now).Contains(item.Id), "调低天数使用原完成时间判断");
        prefs.AutoArchiveCompleted = false; check(engine.DueArchives(now).Length == 0, "关闭自动归档不返回到期任务"); prefs.AutoArchiveCompleted = true;
        edit = engine.Edit(item.Id); check(engine.DueArchives(now).Length == 0, "存在未提交编辑时暂不自动归档");
        engine.Discard(edit); check(engine.DueArchives(now).Contains(item.Id), "编辑副本处理后恢复到期判断");
        var paused = Add("暂搁置不自动归档", Lane.Paused); paused.CompletedSinceUtc = now.AddDays(-100);
        check(!engine.DueArchives(now).Contains(paused.Id), "自动归档只作用于已完成内置列表"); paused.CompletedSinceUtc = null;
        var noClock = Add("缺少计时", Lane.Done); noClock.CompletedSinceUtc = null; engine.InitializeCompletionTimes(now);
        check(noClock.CompletedSinceUtc == now && !engine.DueArchives(now).Contains(noClock.Id), "缺少完成计时时从当前计起，避免意外即时归档");
        engine.Archive(item.Id); var restored = engine.Unarchive(item.Id);
        check(restored.CompletedSinceUtc >= now && !engine.DueArchives(now).Contains(restored.Id), "取消归档回已完成后重新计时，不立刻再次归档");
        for (var i = 0; i < 45; i++) engine.Archive(Add("保留归档" + i, Lane.Done).Id);
        check(engine.State.Archives.Count == 45, "默认关闭自动删除，超过40也保留全部归档");
        Validation.Check(engine.State);
        var store = new AtomicStore(Path.Combine(root, "archive-policy")); await store.SaveAsync(engine.State);
        check(store.Load().Archives.Count == 45 && !store.Load().Preferences.AutoDeleteArchives, "超过40归档及可选策略跨重读完整保留");
        var oldest = engine.State.Archives.Take(5).Select(x => x.Item.Id).ToArray(); prefs.AutoDeleteArchives = true;
        check(engine.TrimArchiveHistory() == 5 && engine.State.Archives.Count == 40 && !engine.State.Archives.Any(x => oldest.Contains(x.Item.Id)), "开启后永久删除较早归档并保留最近40条");
        var newest = Add("第41条", Lane.Done); engine.Archive(newest.Id);
        check(engine.State.Archives.Count == 40 && engine.State.Archives.Any(x => x.Item.Id == newest.Id), "启用自动删除时新增归档仍保持最近40条");
        prefs.AutoDeleteArchives = false; engine.Archive(Add("关闭后新增", Lane.Done).Id);
        check(engine.State.Archives.Count == 41, "关闭自动删除后立即停止淘汰");
        prefs.AutoArchiveAfterDays = 9; await store.SaveAsync(engine.State);
        check(store.Load().Preferences.AutoArchiveAfterDays == 9 && store.Load().Archives.Count == 41, "自定义天数、关闭淘汰及全部归档一同持久化");
    }
}
