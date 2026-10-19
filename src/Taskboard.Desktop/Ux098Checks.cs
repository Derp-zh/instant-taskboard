using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows;
using System.Diagnostics;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux098Checks
{
    internal static async Task Run(BoardController controller)
    {
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.WriteAllText(System.IO.Path.Combine(controller.Store.DirectoryPath, "ux098-progress.json"), JsonSerializer.Serialize(checks)); }
        var bars = new StageRunIcon(true);
        Check(StageFeatureChecks.Children<Rectangle>(bars).Count() == 2 && bars.Angle == 0, "运行中为两个静态竖向矩形");
        Check(StageFeatureChecks.Children<StageRunIcon>(StageRunIcon.Caption(false)).Single().Opacity == .5 && new StageRunIcon(false).Opacity == 1, "仅编辑器暂无进程三角图标减半亮度");
        controller.SuppressCollapse = true;
        var editor = controller.NewTask(Lane.Action); editor.Session.Working.Title = "UX098检查";
        var stage = editor.Session.Working.Stages[0]; var picker = editor.StagePickers[stage.Id];
        var a = new CodexThread(Guid.NewGuid().ToString(), "草稿同步", 0); var b = new CodexThread(Guid.NewGuid().ToString(), "设计核对", 0);
        picker.SetCatalog([a, b]); picker.Input.Text = "同步";
        Check(picker.Matches.SequenceEqual([a]) && stage.Codex is null, "会话按名称包含匹配，输入搜索不改变关联");
        picker.Input.Text = a.Id;
        Check(picker.Matches.Length == 0 && stage.Codex is null, "会话编号不参与搜索，不会把输入误设为关联");
        picker.Select(a);
        Check(stage.Codex?.ThreadId == a.Id && stage.Codex.ThreadName == a.Name && ((TextBlock)picker.Trigger.Content).Text == a.Name && picker.Trigger.ToolTip.ToString()!.Contains(a.Id), "选择才绑定并保存名称，气泡显示名称，提示同时显示编号");
        var identity = stage.Codex!.BindingId; picker.Select(a);
        Check(stage.Codex.BindingId == identity, "重复选择同会话保留绑定身份");
        stage.Codex.Phase = CodexPhase.Running; var before = Copies.Of(stage.Codex); picker.Input.Text = "另一个搜索词";
        Check(Copies.Same(before, stage.Codex), "搜索过程保留已有会话及后台状态");
        var original = Copies.Of(editor.Session.Working); var live = Copies.Of(original); live.Stages[0].Codex!.Phase = CodexPhase.WaitingForInput;
        var edited = Copies.Of(original); edited.Stages[0].Codex!.ThreadName = "重命名"; var merged = BoardEngine.Merge(original, edited, live);
        Check(merged.Stages[0].Codex is { ThreadName: "重命名", Phase: CodexPhase.WaitingForInput } binding && binding.BindingId == identity, "会话名称编辑合并保留最新后台状态与绑定身份");
        var batch = new WorkspaceRevealBatch(); using var first = batch.Enroll(); using var second = batch.Enroll(); batch.Seal();
        var waiting = first.Ready(); Check(!waiting.IsCompleted, "窗口准备不齐时不提前播放");
        await second.Ready(); await waiting; Check(waiting.IsCompletedSuccessfully, "最后窗口准备完成后统一释放动画开始点");
        var cancelled = new WorkspaceRevealBatch(); using var survivor = cancelled.Enroll(); var failed = cancelled.Enroll(); cancelled.Seal();
        waiting = survivor.Ready(); failed.Dispose(); await waiting; Check(waiting.IsCompletedSuccessfully, "准备失败窗口释放屏障，不阻塞其余窗口");
        controller.SetExpanded(false); await Task.Delay(300); controller.SetExpanded(true); await Task.Delay(600);
        var targets = controller.VisibleColumns.Cast<SurfaceWindow>().Append(editor.Host!).ToArray();
        Check(targets.All(x => x.WorkspaceRevealGroup > 0) && targets.Select(x => x.WorkspaceRevealGroup).Distinct().Count() == 1, "整组恢复列表与既有悬浮窗口归属同一动画批次");
        var starts = targets.Select(x => x.MotionDisplayStartTicks).Where(x => x > 0).ToArray();
        var workspaceStartSkewMs = starts.Length == targets.Length ? (starts.Max() - starts.Min()) * 1000.0 / Stopwatch.Frequency : (double?)null;
        Check(editor.Session.Working.Title == "UX098检查" && editor.Host!.ShouldRestore, "同步展开保留原窗口与草稿信息");
        var engine = controller.Engine;
        var multiSession = engine.NewSession(Lane.Action); multiSession.Working.Title = "阶段保留测试"; var added = engine.AddStage(multiSession); added.IsRunning = true;
        var multi = engine.Submit(multiSession); var column = controller.Columns.Single(x => x.Lane == Lane.Action); await Task.Delay(100);
        var card = column.Locate(multi.Id)!;
        controller.SetStageRunningByUser(multi.Id, added.Id, false);
        Check(controller.VisibleCardStages(multi).Count == 2 && StageFeatureChecks.Children<StackPanel>(card).Single(x => Equals(x.Tag, "TaskStageSection") && x.DataContext is Stage s && s.Id == added.Id).Opacity == .45, "暂停阶段立即变暗但保留在列表");
        var topLine = StageFeatureChecks.Children<CardSeparator>(card).Single(x => Equals(x.Tag, "TaskTitleSeparator"));
        var middleLine = StageFeatureChecks.Children<CardSeparator>(card).Single(x => Equals(x.Tag, "TaskStageSeparator"));
        card.UpdateLayout(); Check(topLine.ActualWidth == middleLine.ActualWidth && topLine.Height == 0 && middleLine.Height == 1, "青色标题横线同宽同为一物理像素，标题线不占布局高度");
        RenderCard(card, "retained-stage.png");
        engine.Notify([multi.Id]);
        Check(controller.VisibleCardStages(multi).Count == 2, "后台刷新不整理暂留阶段");
        controller.SetStageRunningByUser(multi.Id, added.Id, true);
        Check(StageFeatureChecks.Children<StackPanel>(card).Where(x => Equals(x.Tag, "TaskStageSection")).All(x => x.Opacity == 1), "重新运行立即恢复正常亮度");
        controller.SetStageRunningByUser(multi.Id, added.Id, false); controller.VerifyCardAnimations = true;
        var clock = Stopwatch.StartNew(); await controller.CleanupRetainedStages(); var stageCollapseMs = clock.Elapsed.TotalMilliseconds;
        Check(stageCollapseMs >= 150 && card.StageCollapseCount == 1 && controller.VisibleCardStages(multi).Count == 1 && StageFeatureChecks.Children<StackPanel>(card).Count(x => Equals(x.Tag, "TaskStageSection")) == 1, "整理阶段经过180ms向上裁切动画后隐藏，无窗口白条");
        var line = StageFeatureChecks.Children<CardSeparator>(card).Single(x => Equals(x.Tag, "TaskTitleSeparator"));
        Check(line.Height == 0, "任务名称与阶段详情之间保留不占高度的青色分隔线");
        controller.SetStageRunningByUser(multi.Id, added.Id, true); controller.SetStageRunningByUser(multi.Id, added.Id, false);
        var otherSession = engine.NewSession(Lane.Action); otherSession.Working.Title = "其他删除任务"; var other = engine.Submit(otherSession); await Task.Delay(80);
        var otherCard = column.Locate(other.Id)!; controller.DeleteTask(other.Id);
        Check(engine.State.Tasks.Any(x => x.Id == other.Id), "删除动画结束前不提前移除任务");
        await Task.Delay(380);
        Check(!engine.State.Tasks.Any(x => x.Id == other.Id) && otherCard.TaskCollapseCount == 1 && controller.VisibleCardStages(multi).Count == 1, "删除其他任务也整理全部暂留阶段，整卡向上收起后删除");
        controller.SetStageRunningByUser(multi.Id, added.Id, true); controller.SetStageRunningByUser(multi.Id, added.Id, false);
        controller.SetExpanded(false); await Task.Delay(220); controller.SetExpanded(true);
        for (var i = 0; i < 100 && controller.VisibleCardStages(multi).Count > 1; i++) await Task.Delay(20);
        Check(controller.VisibleCardStages(multi).Count == 1, "下一次展开看板会在窗口恢复后收起暂留阶段");
        controller.SetStageRunningByUser(multi.Id, added.Id, true); controller.SetStageRunningByUser(multi.Id, added.Id, false);
        engine.Move(multi.Id, Lane.Running, null); await Task.Delay(80);
        Check(controller.VisibleCardStages(multi).Count == 2, "自动调整列表状态保留暂留阶段");
        controller.MoveTask(multi.Id, Lane.Action, null); await Task.Delay(380);
        Check(controller.VisibleCardStages(multi).Count == 1 && multi.Lane == Lane.Action, "人为移动任务整理全部暂留阶段");
        controller.SetStageRunningByUser(multi.Id, added.Id, true); controller.SetStageRunningByUser(multi.Id, added.Id, false);
        var saveSession = engine.NewSession(Lane.Action); saveSession.Working.Title = "保存其他任务";
        Check(await controller.SubmitSession(saveSession) && controller.VisibleCardStages(multi).Count == 1, "保存其他任务同样整理全部暂留阶段");
        var doneSession = engine.NewSession(Lane.Done); doneSession.Working.Title = "归档动画任务"; var doneItem = engine.Submit(doneSession); await Task.Delay(80);
        var doneCard = controller.Columns.Single(x => x.Lane == Lane.Done).Locate(doneItem.Id)!;
        controller.ArchiveTask(doneItem.Id); await Task.Delay(380);
        Check(engine.State.Archives.Any(x => x.Item.Id == doneItem.Id) && doneCard.TaskCollapseCount == 1, "归档同样先收起整张任务卡片");
        controller.VerifyCardAnimations = false;
        for (var i = 0; i < 44; i++) { var s = engine.NewSession(Lane.Done); s.Working.Title = "归档列表" + i; var saved = engine.Submit(s); engine.Archive(saved.Id); }
        controller.Settings(); var settings = Application.Current.Windows.OfType<SettingsSurface>().Single(); settings.SelectSection("归档"); await Task.Delay(200);
        Check(engine.State.Archives.Count == 45 && StageFeatureChecks.Children<TaskCard>(settings.Root).Count(x => x.ArchivedPreview) == 20, "归档无淘汰时保留45条，首次只构建最近20张预览");
        StageFeatureChecks.Children<Button>(settings.Root).Single(x => Equals(x.Tag, "LoadAllArchives")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        settings.UpdateLayout();
        Check(StageFeatureChecks.Children<TaskCard>(settings.Root).Count(x => x.ArchivedPreview) == 45, "加载更多一次展示全部归档");
        controller.SetExpanded(false); await Task.Delay(220); controller.SetExpanded(true); await Task.Delay(500); settings.UpdateLayout();
        Check(StageFeatureChecks.Children<TaskCard>(settings.Root).Count(x => x.ArchivedPreview) == 45, "整组收起恢复保持设置已加载内容，不重新打开或刷新页面");
        var archiveDays = StageFeatureChecks.Children<TextBox>(settings.Root).Single(x => Equals(x.Tag, "AutoArchiveDays")); archiveDays.Text = "9"; archiveDays.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(engine.State.Preferences.AutoArchiveAfterDays == 9, "归档设置输入自定义天数生效");
        settings.SelectSection("主设置"); settings.SelectSection("归档");
        settings.UpdateLayout();
        Check(StageFeatureChecks.Children<TaskCard>(settings.Root).Count(x => x.ArchivedPreview) == 20 && !engine.State.Preferences.AutoDeleteArchives, "重新进入归档恢复20条显示，自动删除仍默认关闭");
        controller.SetStageRunningByUser(multi.Id, added.Id, true); controller.SetStageRunningByUser(multi.Id, added.Id, false);
        var expiredSession = engine.NewSession(Lane.Done); expiredSession.Working.Title = "自动归档验证"; var expired = engine.Submit(expiredSession); expired.CompletedSinceUtc = DateTime.UtcNow.AddDays(-10);
        await controller.CheckAutomaticArchives(true);
        Check(engine.State.Archives.Any(x => x.Item.Id == expired.Id) && controller.VisibleCardStages(multi).Count == 2, "自动归档会执行到期移除，但不触发其他任务的阶段整理");
        await controller.CleanupRetainedStages();
        File.WriteAllText(System.IO.Path.Combine(controller.Store.DirectoryPath, "ux098-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, desktopShown = false, mouseMoved = false, keyboardInjected = false, stageCollapseMs, workspaceStartSkewMs, animationErrors = targets.Where(x => x.LastAnimationError is not null).Select(x => x.LastAnimationError).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        void RenderCard(TaskCard view, string file)
        {
            view.UpdateLayout(); var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using var output = File.Create(System.IO.Path.Combine(controller.Store.DirectoryPath, file)); encoder.Save(output);
        }
    }
}
