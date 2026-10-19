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
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0910Checks
{
    internal static async Task Run(BoardController controller, bool showHint = false)
    {
        var checks = new List<string>(); var root = controller.Store.DirectoryPath;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.WriteAllText(Path.Combine(root, "ux0910-progress.json"), JsonSerializer.Serialize(checks)); }
        controller.SuppressCollapse = true;
        var editor = controller.NewTask(Lane.Action); await Settled(editor.Host!);
        var button = Children<Button>(editor).First(x => Equals(x.Tag, "StageRun"));
        var source = PresentationSource.FromVisual(button) as HwndSource;
        Check(source is not null && source.Handle == editor.Host!.ContentHandle && Window.GetWindow(button) is null, "真实编辑控件属于独立HwndSource，Window.GetWindow确实为空");
        var legacyThrows = false;
        try { _ = new WindowInteropHelper(Window.GetWindow(button)).Handle; }
        catch (ArgumentNullException) { legacyThrows = true; }
        Check(legacyThrows, "复现旧提示定位代码的空窗口异常，会被全局异常处理转为错误悬浮窗口");
        var beforeWindows = Application.Current.Windows.Count;
        var hint = Ui.CreateHint(button, BoardEngine.LastRunningStageMessage);
        Check(hint is not null && Application.Current.Windows.Count == beforeWindows, "新提示沿原生内容句柄定位，不抛异常、不创建通用悬浮窗口");
        hint!.ApplyTemplate(); hint.Measure(new Size(500, 100)); hint.Arrange(new Rect(hint.DesiredSize));
        Check(Children<Border>(hint).Count() == 1, "实际提示内容仍只有一个边框");
        var area = System.Windows.Forms.Screen.FromHandle(source!.Handle).WorkingArea;
        var placement = hint.CustomPopupPlacementCallback!(hint.DesiredSize, button.RenderSize, new Point())[0].Point;
        var screen = button.PointToScreen(new Point(placement.X + hint.DesiredSize.Width / 2, placement.Y));
        Check(Math.Abs(screen.X - (area.Left + area.Width / 2.0)) < 1 && Math.Abs(screen.Y - (area.Top + area.Height * .62)) < 1, "默认位置确实是当前屏幕工作区水平居中、62%高度");
        Check(Ui.CreateHint(new Button(), "未连接控件") is null, "未连接或已卸载的控件安静跳过，不生成错误窗口");
        controller.CloseDraftWindow(); await Task.Delay(200);
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "1";
        session.Working.Stages[0].Name = "阶段0"; session.Working.Stages[0].Text = "37";
        var second = controller.Engine.AddStage(session); second.IsRunning = true; second.Text = "另一阶段";
        var item = controller.Engine.Submit(session); await Task.Delay(100);
        var column = controller.Columns.Single(x => x.Lane == Lane.Action); var card = column.Locate(item.Id)!; card.UpdateLayout();
        var name = Children<Border>(card).First(x => Equals(x.Tag, "TaskStageName"));
        var marquee = (MarqueeText)name.Child;
        Check(name.HorizontalAlignment == HorizontalAlignment.Left && marquee.Overflow < .5 && name.ActualWidth < 100, "短阶段名称按内容紧凑占位，不拉成长胶囊");
        Check(marquee.Offset == 0, "可完整显示的短名称不滚动");
        var title = Children<MarqueeText>(card).Single(x => Equals(x.Label.Tag, "TaskTitle"));
        Check(title.HorizontalAlignment == HorizontalAlignment.Left && title.ActualWidth < 40, "短任务名称也恢复自然宽度");
        var line = Children<CardSeparator>(card).Single(x => Equals(x.Tag, "TaskTitleSeparator"));
        var details = Children<TextBlock>(card).First(x => Equals(x.Tag, "TaskStageDetails"));
        var pillTop = name.TransformToAncestor(card).Transform(new Point()).Y;
        var pillBottom = name.TransformToAncestor(card).Transform(new Point(0, name.ActualHeight)).Y;
        var lineBottom = line.TransformToAncestor(card).Transform(new Point()).Y + 1 / VisualTreeHelper.GetDpi(line).DpiScaleY;
        var textTop = details.TransformToAncestor(card).Transform(new Point()).Y + TaskCardLayout.DetailsInkTop(details);
        var above = pillTop - lineBottom; var below = textTop - pillBottom;
        Check(above > 3 && Math.Abs(above - below) < .1, "青线到气泡的留白与气泡到正文字形上沿的距离一致");
        Check(line.Height == 0, "横线本身继续不占布局高度，仅新增用户要求的留白");
        Render(card, Path.Combine(root, "compact-name-spacing.png"));
        var stateButton = Children<Button>(card).First(x => Equals(x.Tag, "TaskStageRun"));
        Check(Window.GetWindow(stateButton) is null && Ui.CreateHint(stateButton, BoardEngine.LastRunningStageMessage, card) is not null, "列表按钮同样适配子原生表面，可建立固定气泡");
        var edit = controller.EditTask(item.Id); await Settled(edit.Host!);
        var editButton = Children<Button>(edit).First(x => Equals(x.Tag, "StageRun"));
        Check(Window.GetWindow(editButton) is null && Ui.CreateHint(editButton, BoardEngine.LastRunningStageMessage) is not null, "编辑任务窗口提示也不依赖WPF Window父级");
        edit.Suspend(); await Task.Delay(180);
        item.Stages[0].Name = "这是需要滚动才能显示完整的很长阶段名称";
        item.Stages[0].Codex = new CodexBinding { ThreadId = "layout-check", ThreadName = "会话名称" };
        controller.Engine.Notify([item.Id]); await Task.Delay(100); card = column.Locate(item.Id)!; card.UpdateLayout();
        name = Children<Border>(card).First(x => Equals(x.Tag, "TaskStageName")); marquee = (MarqueeText)name.Child;
        Check(name.ActualWidth < card.ActualWidth && marquee.Overflow > .5, "长气泡被右侧名称和图标限制，超出空间时才滚动");
        await Task.Delay(750); Check(marquee.Offset < 0, "长阶段名称实际播放横向平移");
        if (showHint)
        {
            // Explicit display check: only this bubble is on screen; every
            // task window remains offscreen and no mouse/keyboard is injected.
            var liveHint = Ui.CreateHint(Children<Button>(card).First(x => Equals(x.Tag, "TaskStageRun")), BoardEngine.LastRunningStageMessage, card)!;
            var count = Application.Current.Windows.Count; Ui.OpenHint(liveHint); await Task.Delay(150);
            Check(Ui.ActiveHint == liveHint && liveHint.IsOpen && PresentationSource.FromVisual(liveHint) is HwndSource, "真实原生气泡已打开，不是通用悬浮窗口");
            Render(liveHint, Path.Combine(root, "hint-bubble.png"));
            Check(Application.Current.Windows.Count == count, "显示提示不会增加应用的悬浮窗口数量");
            await Task.Delay(1150);
            File.WriteAllText(Path.Combine(root, "hint-state.json"), JsonSerializer.Serialize(new { isOpen = liveHint.IsOpen, active = Ui.ActiveHint is not null, parent = LogicalTreeHelper.GetParent(liveHint)?.GetType().Name }));
            Check(Ui.ActiveHint is null && !liveHint.IsOpen, "真实气泡按1.2秒自动关闭并清理计时器");
        }
        File.WriteAllText(Path.Combine(root, "ux0910-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, legacyNullWindowReproduced = legacyThrows,
            gapAbove = above, gapBelow = below, hintDisplayed = showHint, mouseMoved = false, keyboardInjected = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject => StageFeatureChecks.Children<T>(root);
    private static async Task Settled(SurfaceWindow window)
    {
        for (var i = 0; i < 180 && (!window.IsVisible || window.IsRevealAnimating || window.IsContentSwapHandoff); i++) await Task.Delay(20);
        if (!window.IsVisible || window.IsRevealAnimating || window.IsContentSwapHandoff) throw new InvalidOperationException("隔离窗口未就绪");
    }
    private static void Render(FrameworkElement element, string path)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
