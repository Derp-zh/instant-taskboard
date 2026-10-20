using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Actual list cards in isolated, offscreen native windows; no input injection.
internal static class Ux0920Checks
{
    internal static async Task Run(BoardController controller)
    {
        if (!controller.Isolated || !controller.Smoke) throw new InvalidOperationException("仅允许离屏隔离数据检查");
        var root = controller.Store.DirectoryPath;
        var checks = new List<string>(); var failures = new List<string>(); var measurements = new List<object>();
        void Check(bool value, string text) { (value ? checks : failures).Add(text); }
        controller.SuppressCollapse = true; controller.SetExpanded(true); await Task.Delay(350);
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "阶段标题宽度检查";
        var stage = session.Working.Stages[0]; stage.Text = "阶段正文保持原位";
        stage.Codex = new CodexBinding { ThreadId = "isolated-layout", ThreadName = "会话" };
        var second = controller.Engine.AddStage(session); second.IsRunning = true; second.Name = "短"; second.Text = "第二阶段";
        var item = controller.Engine.Submit(session); stage = item.Stages[0];
        var column = controller.Columns.Single(x => x.Lane == Lane.Action);
        foreach (var width in new[] { 80.0, 180.0, 240.0, 300.0, 430.0, 240.0 })
        foreach (var conversation in new[] { "短", "中中中", "中中中中", "中中中中中", "中中中中中中", "iiiiii", "MMMMMMMM", "中a英文B混合名称", "这是一个很长的绑定会话名称需要滚动显示" })
        foreach (var title in new[] { "", "短", "阶段名称长度适中", "很长的阶段名称用于检查能否用尽所有剩余空间并完整滚动显示" })
        {
            column.Width = width; stage.Name = title; stage.Codex!.ThreadName = conversation; controller.Engine.Notify([item.Id]);
            column.Root.UpdateLayout(); var card = column.Locate(item.Id)!; card.UpdateLayout();
            var pill = Children<Border>(card).Single(x => Equals(x.Tag, "TaskStageName") && x.DataContext is Stage s && s.Id == stage.Id);
            var text = (MarqueeText)pill.Child;
            var row = (CardHeading)VisualTreeHelper.GetParent(pill);
            var name = Children<MarqueeText>(row).Single(x => Equals(x.Tag, "TaskConversationName"));
            var state = Children<Button>(row).Single();
            var stateX = state.TranslatePoint(new Point(), row).X; var nameX = name.TranslatePoint(new Point(), row).X;
            var pillX = pill.TranslatePoint(new Point(), row).X;
            var remaining = Math.Max(0, nameX - name.Margin.Left - pill.Margin.Right);
            var natural = Width(text.Label, title) + pill.Padding.Left + pill.Padding.Right + pill.BorderThickness.Left + pill.BorderThickness.Right;
            var expected = Math.Min(remaining, natural);
            var key = $"列{width}/会话{conversation}/阶段{title}";
            Check(Math.Abs(stateX + state.ActualWidth - row.ActualWidth) < .5, key + "：状态图标固定最右");
            Check(Math.Abs(name.ActualWidth - Math.Clamp(Width(name.Label, conversation), Width(name.Label, "中中中"), Width(name.Label, "中中中中中"))) < .5, key + "：会话宽度3至5个中文字宽");
            Check(Math.Abs(name.Overflow - Math.Max(0, Width(name.Label, conversation) - name.ActualWidth)) < .5, key + "：会话超过五个中文字宽才滚动，不按编码字节截断");
            Check(Math.Abs(nameX + name.ActualWidth + state.Margin.Left - stateX) < .5, key + "：会话紧邻图标左侧");
            var visiblePill = Math.Min(pill.ActualWidth, pill.Clip?.Bounds.Width ?? double.PositiveInfinity);
            Check(Math.Abs(pillX) < .5 && Math.Abs(visiblePill - expected) < 1, key + "：阶段靠左自然宽度且以全部剩余空间为上限");
            var viewport = Math.Max(0, expected - pill.Padding.Left - pill.Padding.Right - pill.BorderThickness.Left - pill.BorderThickness.Right);
            Check(Math.Abs(text.ActualWidth - viewport) < 1, key + "：气泡与滚动文字视口同步受限");
            Check(Math.Abs(text.Overflow - Math.Max(0, Width(text.Label, title) - viewport)) < 1, key + "：滚动范围包含完整阶段名称");
            measurements.Add(new { width, conversation, title, row = row.ActualWidth, stateX, nameX, nameWidth = name.ActualWidth,
                minimumNameWidth = Width(name.Label, "中中中"), maximumNameWidth = Width(name.Label, "中中中中中"),
                previousMinimumNameWidth = Width(name.Label, "MMMMMM"), previousMaximumNameWidth = Width(name.Label, "MMMMMMMMMM"),
                pillX, pillWidth = pill.ActualWidth, visiblePill, remaining, natural, expected, viewport = text.ActualWidth, text.Overflow });
            if (width == 300 && conversation.StartsWith("这是") && title.Length > 20) Render(card, Path.Combine(root, "long-stage-long-conversation.png"));
        }
        // Resize the same realized card, without a data notification/rebuild.
        var liveCard = column.Locate(item.Id)!; var builds = liveCard.BuildCount;
        foreach (var width in new[] { 430.0, 240.0, 180.0, 300.0, 430.0 })
        {
            column.Width = width; column.Root.UpdateLayout();
            var pill = Children<Border>(liveCard).Single(x => Equals(x.Tag, "TaskStageName") && x.DataContext is Stage s && s.Id == stage.Id);
            var row = (CardHeading)VisualTreeHelper.GetParent(pill);
            var name = Children<MarqueeText>(row).Single(x => Equals(x.Tag, "TaskConversationName"));
            var slot = Math.Max(0, name.TranslatePoint(new Point(), row).X - name.Margin.Left - pill.Margin.Right);
            var visible = Math.Min(pill.ActualWidth, pill.Clip?.Bounds.Width ?? double.PositiveInfinity);
            Check(liveCard.BuildCount == builds && Math.Abs(visible - slot) < 1, "同一卡片缩放至" + width + "不重建，长阶段同步占满余宽");
            Render(liveCard, Path.Combine(root, "resize-" + width + ".png"));
        }
        var constrainedPill = Children<Border>(liveCard).Single(x => Equals(x.Tag, "TaskStageName") && x.DataContext is Stage s && s.Id == stage.Id);
        var constrainedRow = (CardHeading)VisualTreeHelper.GetParent(constrainedPill);
        var stageText = (MarqueeText)constrainedPill.Child;
        var conversationText = Children<MarqueeText>(constrainedRow).Single(x => Equals(x.Tag, "TaskConversationName"));
        stageText.Restart(); conversationText.Restart(); await Task.Delay(250);
        Check(Math.Abs(stageText.Offset) < .1 && Math.Abs(conversationText.Offset) < .1, "阶段与会话滚动前均保留起点停顿");
        await Task.Delay(600);
        Check(stageText.Offset < -1 && conversationText.Offset < -1, "阶段与会话超长文本实际各自滚动");
        Render(liveCard, Path.Combine(root, "scrolling.png"));
        stage.Name = "短"; stage.Codex!.ThreadName = "短"; controller.Engine.Notify([item.Id]); column.Root.UpdateLayout();
        liveCard = column.Locate(item.Id)!;
        var shortStage = Children<Border>(liveCard).Single(x => Equals(x.Tag, "TaskStageName") && x.DataContext is Stage s && s.Id == stage.Id);
        var shortRow = (CardHeading)VisualTreeHelper.GetParent(shortStage);
        var shortName = Children<MarqueeText>(shortRow).Single(x => Equals(x.Tag, "TaskConversationName"));
        Check(shortStage.ActualWidth < 40 && shortStage.Clip is null && ((MarqueeText)shortStage.Child).Overflow < .1,
            "短阶段名无字符最小宽度，不拉伸、不滚动、无旧裁切残留");
        Check(shortName.Overflow < .1 && shortName.Label.TextAlignment == TextAlignment.Right, "不足三个中文字宽的会话占三个中文字宽且文字靠右，不滚动");
        Render(liveCard, Path.Combine(root, "short-names.png"));
        stage.Codex = null; controller.Engine.Notify([item.Id]); column.Root.UpdateLayout(); liveCard = column.Locate(item.Id)!;
        Check(Children<MarqueeText>(liveCard).Any(x => Equals(x.Tag, "TaskConversationName") && x.Label.Text == "未绑定"), "无绑定仍显示未绑定占位");
        foreach (var lane in new[] { Lane.Inspiration, Lane.Running, Lane.Refining, Lane.Paused, Lane.Done })
        {
            var sample = controller.Engine.NewSession(lane); sample.Working.Title = "单阶段例外"; sample.Working.Stages[0].Text = "正文";
            var single = controller.Engine.Submit(sample); var target = controller.Columns.Single(x => x.Lane == lane); target.Root.UpdateLayout();
            var card = target.Locate(single.Id)!;
            Check(!Children<Border>(card).Any(x => Equals(x.Tag, "TaskStageName")) && !Children<Button>(card).Any(x => Equals(x.Tag, "TaskStageRun")), lane + "单阶段仍不显示阶段气泡或状态图标");
            Check(Children<MarqueeText>(card).Any(x => Equals(x.Tag, "TaskConversationName")) == (lane != Lane.Inspiration), lane + "保留会话栏及灵感库例外");
        }
        File.WriteAllText(Path.Combine(root, "ux0920-measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(root, "ux0920-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, failures, checks, cases = measurements.Count,
            dpi = VisualTreeHelper.GetDpi(column.Root).PixelsPerDip, mouseMoved = false, keyboardInjected = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count > 0) throw new InvalidOperationException(failures.Count + "项布局检查失败：" + failures[0]);
    }
    private static double Width(TextBlock label, string text) => new FormattedText(text, CultureInfo.CurrentUICulture, label.FlowDirection,
        new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize, label.Foreground, VisualTreeHelper.GetDpi(label).PixelsPerDip).WidthIncludingTrailingWhitespace;
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject => StageFeatureChecks.Children<T>(root);
    private static void Render(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
