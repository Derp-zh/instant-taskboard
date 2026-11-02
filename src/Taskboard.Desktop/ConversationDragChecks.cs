using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class ConversationDragChecks
{
    internal static async Task Run(BoardController controller)
    {
        if (!controller.Smoke || !controller.Isolated || controller.ConversationPreviewMode) throw new InvalidOperationException("必须使用普通构建的隔离验证入口，不能启用预览模式");
        var checks = new List<string>();
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            checks.Add(name);
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "drag-binding-progress.json"), JsonSerializer.Serialize(checks));
        }
        const string oldId = "11111111-1111-4111-8111-111111111111", newId = "22222222-2222-4bbb-8aaa-222222222222";
        Check(controller.Engine.State.Tasks.Count == 0 && controller.Engine.State.Sessions.Count == 0 && !controller.Expanded,
            "普通初始化不注入两阶段示例、不强制展开列表");
        Check(!typeof(ConversationDragChecks).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.StartsWith("0.0.0-drag-binding-preview", StringComparison.Ordinal), "验证包保留主开发版本而非预览版本");
        Check(CodexConversationDropTarget.ParseThreadUri("codex://threads/" + newId) == newId, "只解析明确本机会话链接中的完整编号");
        Check(CodexConversationMenu.IsDeeplinkAction("复制深度链接 Alt+Ctrl+L"), "原Codex中文带快捷键菜单正确识别");
        Check(CodexConversationMenu.IsDeeplinkAction("Copy deeplink"), "英文深层链接菜单可识别");
        Check(!CodexConversationMenu.IsDeeplinkAction("复制为 Markdown"), "不会误触复制正文菜单");
        Check(!CodexConversationMenu.IsDeeplinkAction("永久删除"), "不会误触其他菜单操作");
        foreach (var value in new[] { newId, "同名会话", "https://example.com/" + newId, "codex://threads/" + newId + "?host=remote",
            "codex://threads/" + newId + "#other", "codex://threads/" + newId + "/extra", "codex://user@threads/" + newId, "codex://threads/not-an-id",
            "codex://threads//" + newId, "codex://threads/00000000-0000-0000-0000-000000000000" })
            Check(CodexConversationDropTarget.ParseThreadUri(value) is null, "拒绝非明确本机会话链接：" + value);

        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "拖动绑定隔离样本";
        session.Working.Stages[0].Codex = new CodexBinding { ThreadId = oldId, ThreadName = "原绑定", Generation = 7,
            Phase = CodexPhase.WaitingForApproval, AwaitingUserAction = true, PendingRequests = ["old-request"], SourceEpoch = "old-epoch" };
        var second = controller.Engine.AddStage(session); second.IsRunning = true; second.Text = "其他阶段不变";
        second.Codex = new CodexBinding { ThreadId = "33333333-3333-4333-8333-333333333333", ThreadName = "另一阶段", AwaitingUserAction = true, Phase = CodexPhase.WaitingForInput };
        var item = controller.Engine.Submit(session); var stage = item.Stages[0]; var binding = stage.Codex!;
        var secondSnapshot = Copies.Of(item.Stages[1]);
        var editor = controller.Engine.Edit(item.Id); editor.Working.Title = "编辑器尚未保存的输入";
        var before = Copies.Of(controller.Engine.State);
        var result = await controller.CompleteConversationBinding(item.Id, stage.Id, binding.BindingId, ConversationDropTarget.Unavailable("无准确编号"));
        Check(result.StartsWith("未绑定") && Copies.Same(before, controller.Engine.State), "无准确编号时正式任务与未提交编辑均不变化");
        Check(!controller.Engine.BindStageConversation(item.Id, stage.Id, Guid.NewGuid(), newId, "新会话"), "拒绝已过期拖动的绑定代次");
        Check(!controller.Engine.BindStageConversation(Guid.NewGuid(), stage.Id, binding.BindingId, newId, "新会话"), "任务已移除时拒绝提交");
        Check(!controller.Engine.BindStageConversation(item.Id, Guid.NewGuid(), binding.BindingId, newId, "新会话"), "阶段已移除时拒绝提交");
        result = await controller.CompleteConversationBinding(item.Id, stage.Id, binding.BindingId, new(newId, "明确编号样本", "合成准确编号"));
        Check(result.StartsWith("已绑定"), "准确编号通过实际控制器绑定与保存链（合成来源）");
        var rebound = item.Stages[0].Codex!;
        Check(rebound.ThreadId == newId && rebound.BindingId != binding.BindingId && rebound.Generation == 8, "改绑产生新身份与递增代次");
        Check(!rebound.AwaitingUserAction && rebound.PendingRequests.Count == 0 && rebound.Phase == CodexPhase.Unknown && rebound.SourceEpoch.Length == 0, "改绑不继承旧等待保护、请求和来源epoch");
        Check(rebound.PreviousThreads.Contains(oldId) && Copies.Same(secondSnapshot, item.Stages[1]), "保留必要历史且不改其他阶段的等待状态");
        Check(controller.Store.Load().Tasks.Single(x => x.Id == item.Id).Stages[0].Codex?.ThreadId == newId, "实际隔离文件重新读取到新编号");
        Check(!File.Exists(Path.Combine(controller.Store.DirectoryPath, "last-conversation-drop.json")), "普通业务绑定和保存不写测试回执");
        var snapshotBeforeStaleData = Copies.Of(controller.Engine.State);
        result = await controller.CompleteConversationBinding(item.Id, stage.Id, rebound.BindingId, new(oldId, "过期数据目标", "合成"), new object());
        Check(result.StartsWith("未绑定") && Copies.Same(snapshotBeforeStaleData, controller.Engine.State), "落点读取期间数据已切换时拒绝将结果绑定到新数据");
        Check(controller.Engine.State.Sessions.Contains(editor) && editor.Working.Title == "编辑器尚未保存的输入", "拖动绑定不提交或关闭既有编辑器");
        var merged = BoardEngine.Merge(editor.Original, editor.Working, item);
        Check(merged.Title == editor.Working.Title && merged.Stages[0].Codex?.ThreadId == newId, "旧编辑副本保留文本修改并合并最新绑定");
        rebound.AwaitingUserAction = true; rebound.Phase = CodexPhase.WaitingForInput; rebound.PendingRequests = ["new-pending"]; rebound.SourceEpoch = "current-epoch";
        Check(controller.Engine.BindStageConversation(item.Id, stage.Id, rebound.BindingId, newId.ToUpperInvariant(), "只改名称")
            && item.Stages[0].Codex!.BindingId == rebound.BindingId && rebound.AwaitingUserAction && rebound.PendingRequests.Contains("new-pending") && rebound.SourceEpoch == "current-epoch",
            "同一编号大小写及名称刷新不清除实际等待保护和来源epoch");
        controller.Engine.Undo();
        Check(controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Stages[0].Codex?.ThreadName == "明确编号样本", "绑定名称更新可以撤销");
        controller.Engine.Undo();
        item = controller.Engine.State.Tasks.Single(x => x.Id == item.Id);
        Check(item.Stages[0].Codex?.ThreadId == oldId && !item.Stages[0].Codex!.AwaitingUserAction, "改绑可撤销，恢复绑定按原规则重置实时保护");
        controller.Engine.Discard(editor);

        using var host = new HwndSource(new HwndSourceParameters("隔离拖动绑定控件检查") { PositionX = -18000, PositionY = -18000,
            Width = 240, Height = 400, WindowStyle = unchecked((int)0x90000000), ExtendedWindowStyle = 0x08000080 });
        var card = new TaskCard { Controller = controller, DataContext = item }; host.RootVisual = card;
        Layout(card, 220, 400);
        var buttons = Visual<ConversationDragButton>(card).ToArray();
        Check(buttons.Length == 2 && Visual<MarqueeText>(card).Count(x => Equals(x.Tag, "TaskConversationName")) == 2, "多阶段名称均为独立拖动按钮，原滚动文字保留");
        var button = buttons[0]; var hit = button.InputHitTest(new Point(1, button.ActualHeight / 2));
        Check(hit is not null && SurfaceWindow.IsInteractive(hit as DependencyObject), "名称文字之外的按钮空白也命中，且被整卡手势识别为交互控件");
        var previewWidth = Visual<MarqueeText>(button).Single().ActualWidth;
        controller.ConversationPreviewMode = false;
        var ordinary = new TaskCard { Controller = controller, DataContext = item }; host.RootVisual = ordinary; Layout(ordinary, 220, 400);
        Check(Visual<ConversationDragButton>(ordinary).Count() == 2, "普通功能无需任何预览开关即提供多阶段拖动按钮");
        Check(Math.Abs(Visual<MarqueeText>(ordinary).First(x => Equals(x.Tag, "TaskConversationName")).ActualWidth - previewWidth) < .1, "普通按钮保持原名称行宽度预算");
        var archive = new TaskCard { Controller = controller, ArchivedPreview = true, DataContext = item }; host.RootVisual = archive; Layout(archive, 220, 400);
        Check(!Visual<ConversationDragButton>(archive).Any(), "归档预览保持只读");
        Check(Math.Abs(Visual<MarqueeText>(archive).First(x => Equals(x.Tag, "TaskConversationName")).ActualWidth - previewWidth) < .1, "普通拖动按钮与原只读名称使用同样宽度预算");
        var inspiration = Copies.Of(item); inspiration.Lane = Lane.Inspiration;
        var inspirationCard = new TaskCard { Controller = controller, DataContext = inspiration }; host.RootVisual = inspirationCard; Layout(inspirationCard, 220, 400);
        Check(!Visual<ConversationDragButton>(inspirationCard).Any(), "灵感库会话名称隐藏例外保持");
        var single = Copies.Of(item); single.Stages.RemoveAt(1);
        var singleCard = new TaskCard { Controller = controller, DataContext = single }; host.RootVisual = singleCard; Layout(singleCard, 220, 400);
        Check(Visual<ConversationDragButton>(singleCard).Count() == 1, "单阶段任务原名称位置提供一个拖动按钮");

        for (var i = 0; i < controller.Columns.Count; i++) { controller.Columns[i].Left = -20000 - i * 300; controller.Columns[i].Top = -16000; }
        controller.Ball.Left = -24000; controller.Ball.Top = -16000;
        controller.SetExpanded(true); await Task.Delay(250);
        var source = controller.Columns.First(x => x.Lane == Lane.Running); var intentionallyHidden = controller.Columns.Last(); intentionallyHidden.Hide();
        var realButton = Visual<ConversationDragButton>(source.RenderedCard(item.Id)!).First();
        Check(Window.GetWindow(realButton) is null && ReferenceEquals(realButton.SourceColumn, source), "真实列表子宿主中正确定位来源窗口，不依赖空的GetWindow");
        var visibleCount = controller.Columns.Count(x => x.IsVisible);
        using (var drag = new ConversationBindingDrag(controller, source, item.Id, item.Stages[0].Id, item.Stages[0].Codex?.BindingId, "样本"))
        {
            drag.MoveTo(source.PointToScreen(new Point(20, 20)));
            Check(drag.HiddenCount == 0, "仍在来源列表内时其他窗口不隐藏");
            drag.MoveTo(new Point(-30000, -30000));
            Check(drag.HiddenCount == visibleCount - 1 && source.IsVisible && controller.Columns.Count(x => x.IsVisible) == 1, "拖出来源窗口后只隐藏其他原可见列表");
            Check(controller.SuppressCollapse && controller.Expanded, "临时隐藏不改变整组展开标记并抑制外点收起");
        }
        Check(controller.Columns.Count(x => x.IsVisible) == visibleCount && !intentionallyHidden.IsVisible && !controller.SuppressCollapse, "取消后只恢复原可见列表与原抑制状态");
        var capturedPress = Environment.TickCount64;
        controller.FinishConversationInput();
        controller.ObserveWorkspaceOutsidePress(true, pressedAt: capturedPress);
        Check(controller.Expanded, "取消前排队的外点迟到时不收起已恢复列表");
        controller.ObserveWorkspaceOutsidePress(true, pressedAt: Environment.TickCount64 + 1);
        Check(!controller.Expanded, "拖动结束后的新外点仍按原规则收起");
        controller.SetExpanded(true); await Task.Delay(250); intentionallyHidden.Hide();
        var delayed = new TaskCompletionSource<ConversationDropTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeCancellation = Copies.Of(controller.Engine.State);
        var cancelled = new ConversationBindingDrag(controller, source, item.Id, item.Stages[0].Id, item.Stages[0].Codex?.BindingId, "迟到的读取");
        cancelled.MoveTo(new Point(-30000, -30000));
        var completing = cancelled.Complete(new Point(-30000, -30000), _ => delayed.Task);
        cancelled.Dispose(); delayed.SetResult(new(newId, "迟到编号", "合成")); await completing;
        Check(Copies.Same(beforeCancellation, controller.Engine.State) && controller.Columns.Count(x => x.IsVisible) == visibleCount, "松开后异步读取期间取消，迟到编号不绑定且原可见列表恢复");
        using (var drag = new ConversationBindingDrag(controller, source, item.Id, item.Stages[0].Id, null, "样本"))
        {
            drag.MoveTo(new Point(-30000, -30000)); controller.SetExpanded(false);
            Check(controller.ConversationDrag is null && controller.Columns.Where(x => x != source).All(x => !x.IsVisible), "整组主动收起时取消拖动且不重新弹回其他列");
        }
        await Task.Delay(200); await controller.Flush();
        var diagnostic = Path.Combine(controller.Store.DirectoryPath, "last-conversation-drop.json"); Directory.CreateDirectory(diagnostic);
        controller.ConversationPreviewMode = true;
        result = await controller.CompleteConversationBinding(item.Id, item.Stages[0].Id, item.Stages[0].Codex?.BindingId, new(newId, "诊断失败不影响保存", "合成"));
        Check(result.StartsWith("已绑定") && controller.Store.Load().Tasks.Single(x => x.Id == item.Id).Stages[0].Codex?.ThreadId == newId, "测试回执写入失败不能掩盖实际绑定保存成功");
        controller.ConversationPreviewMode = false; Directory.Delete(diagnostic);
        var diskBeforeFailure = File.ReadAllBytes(controller.Store.MainPath);
        var failed = false;
        using (var locked = new FileStream(controller.Store.MainPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { await controller.CompleteConversationBinding(item.Id, item.Stages[0].Id, item.Stages[0].Codex?.BindingId, new(oldId, "保存失败样本", "合成")); }
            catch (InvalidOperationException e) when (e.Message.StartsWith("保存失败", StringComparison.Ordinal)) { failed = true; }
        }
        Check(failed && File.ReadAllBytes(controller.Store.MainPath).SequenceEqual(diskBeforeFailure) && item.Stages[0].Codex?.ThreadId == oldId, "实际文件锁导致保存失败，磁盘原件保持，内存更改保留可重试");
        await controller.Flush();
        Check(controller.Store.Load().Tasks.Single(x => x.Id == item.Id).Stages[0].Codex?.ThreadId == oldId, "保存失败后解除锁并重试可准确落盘");
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "drag-binding-report.json"), JsonSerializer.Serialize(new
        {
            passed = checks.Count, checks, scope = "合成准确编号、隔离实际保存、离屏内部命中与窗口生命周期",
            mouseMoved = false, realCodexDrop = false, previewModeRequired = false, productionDataChanged = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Layout(FrameworkElement view, double width, double height)
    { view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout(); }
    private static IEnumerable<T> Visual<T>(DependencyObject node) where T : DependencyObject
    { if (node is T found) yield return found; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Visual<T>(VisualTreeHelper.GetChild(node, i))) yield return child; }
}
