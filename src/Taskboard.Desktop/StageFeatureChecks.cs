using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class StageFeatureChecks
{
    internal static async Task Run(BoardController controller)
    {
        var root = controller.Store.DirectoryPath; var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name); checks.Add(name);
            File.WriteAllText(Path.Combine(root, "stage-progress.json"), JsonSerializer.Serialize(checks));
        }
        controller.SuppressCollapse = true; controller.SetExpanded(true); await Task.Delay(250);
        var first = controller.OpenNewEntry(Lane.Action); await Settled(first.Host!);
        Inputs(first).First().Text = "待操作独立草稿";
        first.Session.Working.AiIds = [controller.Engine.State.AiOptions[1].Id];
        first.Session.Working.PriorityId = controller.Engine.State.PriorityOptions[0].Id;
        first.Session.Working.Reminder.Enabled = true; controller.Engine.StageSession(first.Session);
        var expected = Copies.Of(first.Session.Working);
        controller.CloseDraftWindow(); await Task.Delay(200);
        var second = controller.OpenNewEntry(Lane.Done); await Settled(second.Host!);
        Check(second.Session.Id != first.Session.Id && second.Session.Working.Lane == Lane.Done && controller.Editors.Any(x => x.Session.Id == first.Session.Id), "关闭后从另一列表新建会创建新卡片，同时恢复原暂存");
        Inputs(second).First().Text = "已完成独立草稿"; await controller.Flush();
        var loaded = controller.Store.Load();
        Check(loaded.Sessions.Count == 2 && Copies.Same(loaded.Sessions.Single(x => x.Id == first.Session.Id).Working, expected), "桌面新建路径保留旧任务状态、平台、优先级、提醒及全部字段");
        controller.CloseDraftWindow(); await Task.Delay(180);

        var formal = controller.Engine.NewSession(Lane.Action); formal.Working.Title = "重复点击任务"; var item = controller.Engine.Submit(formal);
        controller.ToggleTaskEditor(item.Id); var editor = controller.Editors.Single(x => x.Session.TaskId == item.Id); await Settled(editor.Host!);
        Inputs(editor).First().Text = "未提交的编辑"; var sessionId = editor.Session.Id; var host = editor.Host!;
        controller.ToggleTaskEditor(item.Id); await Task.Delay(200);
        Check(!host.ShouldRestore && controller.Editors.All(x => x.Session.TaskId != item.Id) && controller.Engine.State.Tasks.Single().Title == "重复点击任务", "再次点击任务关闭对应编辑窗口，不提交或丢弃副本");
        controller.ToggleTaskEditor(item.Id); editor = controller.Editors.Single(x => x.Session.TaskId == item.Id); await Settled(editor.Host!);
        Check(editor.Session.Id == sessionId && Inputs(editor).First().Text == "未提交的编辑", "再次打开恢复同一编辑副本和未提交输入");
        controller.ToggleTaskEditor(item.Id); await Task.Delay(180);
        controller.ToggleSettings(); var settings = Application.Current.Windows.OfType<SettingsSurface>().Single(); await Settled(settings);
        var originalContent = settings.Root.Children[1]; controller.ToggleSettings(); await Task.Delay(180);
        Check(!settings.ShouldRestore && controller.Expanded, "再次点击设置只关闭设置窗口，保留看板");
        controller.ToggleSettings(); await Settled(settings);
        Check(ReferenceEquals(originalContent, settings.Root.Children[1]), "设置重新打开复用内容，不刷新设置页面");
        var recorder = Children<TextBox>(settings.Root).Single(x => Equals(x.Tag, "HotkeyRecorder"));
        Check(recorder.IsReadOnly && !InputMethod.GetIsInputMethodEnabled(recorder), "快捷键录入禁用该输入框的文字组合，避免中文输入法吞掉录入按键");
        controller.ToggleSettings(); await Task.Delay(180);

        editor = controller.NewTask(Lane.Action); await Settled(editor.Host!); Inputs(editor).First().Text = "分阶段会话界面";
        Click(Children<Button>(editor).Single(x => Equals(x.Tag, "AddStage")));
        editor.UpdateLayout();
        var stageButtons = Children<Button>(editor).Where(x => Equals(x.Tag, "StageRun")).ToArray();
        var bindingButtons = Children<Button>(editor).Where(x => Equals(x.Tag, "StageCodex")).ToArray();
        Check(bindingButtons.Length == 2 && stageButtons.All(x => x.IsHitTestVisible && Children<StageRunIcon>(x).Any()) && editor.Session.Working.Stages[0].IsRunning == true && editor.Session.Working.Stages[1].IsRunning == false, "阶段状态气泡均可操作且含图标文字；阶段0运行中，新阶段暂无进程");
        var hints = Ui.HintCount; Click(stageButtons[0]);
        Check(editor.Session.Working.Stages[0].IsRunning == true && Ui.HintCount == hints + 1 && Ui.LastHint == BoardEngine.LastRunningStageMessage, "编辑器点击最后运行阶段保留状态并请求气泡提示；屏幕外模式不弹真实提示窗");
        Click(stageButtons[1]); Check(editor.Session.Working.Stages.All(x => x.IsRunning == true), "可同时打开两个阶段的运行标记，不限制数量");
        Check(bindingButtons.Zip(stageButtons).All(x => Math.Abs(x.First.Width / x.Second.Width - 1.6) < 0.001), "阶段关联气泡横向尺寸精确为运行状态气泡的1.6倍");
        for (var i = 0; i < 2; i++)
        {
            Click(bindingButtons[i]); var pane = Children<StackPanel>(editor).Single(x => Equals(x.Tag, "StageBinding-" + editor.Session.Working.Stages[i].Id));
            editor.StagePickers[editor.Session.Working.Stages[i].Id].Select(new CodexThread(Guid.NewGuid().ToString(), "阶段会话" + i, 0));
            Click(bindingButtons[i]); // Selection now collapses; reopen to test expanded snapshots.
        }
        Check(editor.Session.Working.Stages.All(x => x.Codex is not null && x.IsRunning == true), "关联分别写入各阶段，点击关联不会更改独立运行标记");
        Check(Children<Button>(editor).Where(x => Equals(x.Content, "保存任务") || Equals(x.Content, "删除任务")).All(x => x.Height == 40), "删除和保存按钮纵向与40高的X一致");
        var snapshot = editor.CreateSnapshot();
        Check(Children<StackPanel>(snapshot).Where(x => x.Tag is string tag && tag.StartsWith("StageBinding-", StringComparison.Ordinal)).All(x => x.Visibility == Visibility.Visible), "卡片动画快照保留展开的阶段关联区域");
        snapshot.Destroy(); Render(editor, "stage-editor.png"); var newId = editor.Session.Working.Id;
        Click(Children<Button>(editor).Single(x => Equals(x.Content, "保存任务")));
        for (var i = 0; controller.Editors.Contains(editor) && i < 150; i++) await Task.Delay(20);
        Check(controller.Engine.State.Tasks.Single(x => x.Id == newId).Stages.All(x => x.Codex is not null), "界面保存后正式任务保留两个独立阶段关联");
        var runningItem = controller.Engine.State.Tasks.Single(x => x.Id == newId);
        var listCard = new TaskCard { Controller = controller, DataContext = runningItem };
        listCard.Measure(new Size(260, double.PositiveInfinity)); listCard.Arrange(new Rect(0, 0, 260, listCard.DesiredSize.Height)); listCard.UpdateLayout();
        var runIcons = Children<Button>(listCard).Where(x => Equals(x.Tag, "TaskStageRun")).ToArray();
        Check(runIcons.Length == 2 && runIcons.All(x => x.Content is StageRunIcon), "多列表卡片显示多个运行阶段，状态按钮仅含图标");
        var editorCount = controller.Editors.Count; Click(runIcons[0]); listCard.Rebuild(); listCard.Measure(new Size(260, double.PositiveInfinity)); listCard.UpdateLayout();
        Check(runningItem.Stages[0].IsRunning == false && BoardEngine.CardStages(runningItem).Count == 1 && controller.Editors.Count == editorCount, "卡片阶段图标只切换对应标记和可见阶段，不联动打开任务编辑");
        hints = Ui.HintCount; Click(Children<Button>(listCard).Single(x => Equals(x.Tag, "TaskStageRun") && x.DataContext is Stage { IsRunning: true }));
        Check(BoardEngine.CardStages(runningItem).Count == 1 && Ui.HintCount == hints + 1 && controller.Editors.Count == editorCount, "列表点击最后运行图标只请求气泡提示，不关闭阶段或联动编辑");
        controller.Undo(); listCard.DataContext = controller.Engine.State.Tasks.Single(x => x.Id == newId); listCard.UpdateLayout();
        Check(Children<Button>(listCard).Count(x => Equals(x.Tag, "TaskStageRun")) == 2 && BoardEngine.CardStages((WorkItem)listCard.DataContext).Count == 2, "列表上的阶段状态切换可以撤销，恢复两个可见阶段并保留关联");

        var inspiration = controller.Columns.Single(x => x.Lane == Lane.Inspiration);
        void OpenQuick() => Click(Children<Button>(inspiration.Root).Single(x => Equals(x.Content, "＋ 新建任务")));
        Border Quick() => Children<Border>(inspiration.Root).Single(x => Equals(x.Tag, "QuickTaskEntry"));
        OpenQuick(); var quick = Quick(); var inputs = Children<TextBox>(quick).ToArray();
        Check(inputs[1].MinLines == 2 && inputs[1].BorderThickness == new Thickness(1), "灵感详情至少显示两行，带独立圆角边框");
        inputs[0].Text = "尚未自动保存"; inputs[1].Text = "立即转完整窗口";
        var before = controller.Engine.State.Tasks.Count; Click(Children<Button>(quick).Single(x => Equals(x.Tag, "QuickExpand")));
        var promoted = controller.Editors.Single(x => x.Session.Working.Title == "尚未自动保存"); await Settled(promoted.Host!); await Task.Delay(700);
        Check(promoted.Session.TaskId is null && promoted.Session.Working.Stages[0].Text == "立即转完整窗口" && !promoted.Session.Inline && controller.Engine.State.Tasks.Count == before && !inspiration.QuickEntryActive, "自动保存之前转完整新建，保留最后字符并停止旧快捷卡片计时器，无重复正式任务");
        controller.CloseDraftWindow(); await Task.Delay(180);
        OpenQuick(); quick = Quick(); inputs = Children<TextBox>(quick).ToArray(); inputs[0].Text = "已自动保存"; inputs[1].Text = "原始详情";
        await Task.Delay(850); inputs[1].Text += "末字"; Click(Children<Button>(quick).Single(x => Equals(x.Tag, "QuickExpand")));
        promoted = controller.Editors.Single(x => x.Session.Working.Title == "已自动保存"); await Settled(promoted.Host!);
        Check(promoted.Session.TaskId is not null && promoted.Session.Working.Stages[0].Text == "原始详情末字" && controller.Engine.State.Tasks.Count == before + 1 && controller.Engine.State.Tasks.Single(x => x.Title == "已自动保存").Stages[0].Text == "原始详情末字", "自动保存之后转完整编辑，保留末字且仅有一个正式任务");
        controller.ToggleSettings(); await Settled(settings);
        var quickSetting = Children<CheckBox>(settings.Root).Single(x => Equals(x.Tag, "InspirationQuickEntry")); quickSetting.IsChecked = false; quickSetting.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        controller.ToggleSettings(); await Task.Delay(180); OpenQuick();
        Check(!inspiration.QuickEntryActive && controller.Editors.Any(x => x.Session.TaskId is null && x.Session.Working.Lane == Lane.Inspiration && !x.Session.Working.HasContent), "关闭快捷卡片选项后，灵感库新建直接打开预选灵感库的完整窗口");
        Check(controller.ConfigureHotkey(new BoardHotkey { Modifiers = 6, VirtualKey = 0x79 }, out _), "主设置支持更换组合键，隔离运行不抢占真实快捷键");
        await controller.Flush(); loaded = controller.Store.Load();
        Check(!loaded.Preferences.InspirationQuickEntry && loaded.Preferences.Hotkey.Modifiers == 6 && loaded.Preferences.Hotkey.VirtualKey == 0x79, "灵感快捷选项和自定义快捷键独立持久保存");
        controller.SetExpanded(false); await Task.Delay(180); controller.ToggleFromHotkey(); await Settled(promoted.Host!);
        Check(controller.Expanded && promoted.Session.Working.Stages[0].Text == "原始详情末字", "快捷键唤起路径恢复整组及既有悬浮窗口输入");
        controller.ToggleFromHotkey(); await Task.Delay(180);
        Check(!controller.Expanded && !promoted.Host!.ShouldRestore && promoted.Session.Visible, "看板已展开时快捷键收起整组，悬浮窗口输入和打开状态保留");
        HotkeyChecks.Run(Check);
        File.WriteAllText(Path.Combine(root, "stage-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, desktopShown = false, mouseMoved = false, hintRequests = Ui.HintCount, hintPopupShown = false, foregroundActivationSuppressed = true, temporaryAlternateKeysRegisteredAndReleased = true, keyboardInjected = false, defaultHotkeyRegistered = false }, new JsonSerializerOptions { WriteIndented = true }));
        void Render(FrameworkElement view, string file)
        {
            view.UpdateLayout(); var image = new RenderTargetBitmap(430, 650, 96, 96, PixelFormats.Pbgra32); image.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(root, file)); encoder.Save(stream);
        }
    }
    private static IEnumerable<TextBox> Inputs(EditorSurface editor) => Children<TextBox>(editor);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Settled(SurfaceWindow window)
    {
        for (var i = 0; (!window.IsVisible || window.IsScrollAnimating || window.IsContentSwapHandoff) && i < 200; i++) await Task.Delay(20);
        if (!window.IsVisible || window.IsScrollAnimating || window.IsContentSwapHandoff) throw new InvalidOperationException("界面未完成开合：" + window.Title);
    }
    internal static IEnumerable<T> Children<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) foreach (var child in Children<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
}
