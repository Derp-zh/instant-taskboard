using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

internal static class ScrollMotionChecks
{
    internal static async Task Run(BoardController controller, bool restorationOnly = false, bool stackOnly = false)
    {
        var directory = Path.Combine(controller.Store.DirectoryPath, "scroll-frames"); Directory.CreateDirectory(directory);
        var checks = new List<string>(); var evidence = new List<object>();
        void Assert(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        var area = controller.WorkArea(controller.Ball);
        var background = new SurfaceWindow(area.Width, area.Height) { GlassEnabled = false, WindowLayer = -100, Left = area.Left, Top = area.Top };
        background.Root.Background = Ui.Brush("#33465F"); background.Show();
        controller.SuppressCollapse = true; controller.SetExpanded(true); await Task.Delay(400);
        foreach (var column in controller.Columns) column.Hide(); controller.Ball.Hide();
        try
        {
            if (stackOnly)
            {
                EditorSurface? top = null;
                for (var i = 1; i <= 3; i++) { top = controller.NewTask(Lane.Running); top.Session.Working.Title = "白条验证暂存卡片" + i; }
                var host = top!.Host!; host.WindowLayer = 100; await Task.Delay(600);
                for (var cycle = 1; cycle <= 4; cycle++)
                {
                    var closing = await Capture("三张暂存-收起-" + cycle, host, () => controller.SetExpanded(false), 600);
                    var opening = await Capture("三张暂存-展开-" + cycle, host, () => controller.SetExpanded(true), 600);
                    evidence.Add(new { name = "三张暂存", cycle, opening, closing }); Write();
                    Assert(opening.Any(x => x.WhiteBars == 1) && opening.All(x => x.WhiteBars <= 1), "三张暂存第" + cycle + "次展开只有一条泛光白条");
                    Assert(closing.Any(x => x.WhiteBars == 1) && closing.All(x => x.WhiteBars <= 1), "三张暂存第" + cycle + "次收起只有一条泛光白条");
                }
                Write(); return;
            }
            var draft = controller.NewTask(Lane.Running); draft.Session.Working.Title = "画卷动效实验草稿";
            var draftWindow = draft.Host!; await Task.Delay(400); draftWindow.Hide(); await Task.Delay(400);
            var created = controller.Engine.NewSession(Lane.Running); created.Working.Title = "画卷动效实验 · 编辑任务"; controller.Engine.Submit(created);
            var editor = controller.EditTask(created.Working.Id); var editWindow = editor.Host!; await Task.Delay(400); editWindow.Hide(); await Task.Delay(400);
            controller.Settings(); var settings = Application.Current.Windows.OfType<SettingsSurface>().Single(); await Task.Delay(400); settings.Hide(); await Task.Delay(400);
            // First display without warming a snapshot catches the missing
            // initial flare that repeated/preheated open-close cycles miss.
            if (!restorationOnly) foreach (var (name, window) in new[] {
                ("编辑首次", (SurfaceWindow)new TaskEditorWindow(new EditorSurface(controller, Copies.Of(editor.Session), snapshotMode: true))),
                ("设置首次", (SurfaceWindow)new SettingsSurface(controller)),
                ("其他悬浮首次", new SurfaceWindow(360, 260)) })
            {
                window.Title = name; window.Left = area.Left + (area.Width - window.Width) / 2; window.Top = area.Top + (area.Height - window.Height) / 2;
                new WindowInteropHelper(window).EnsureHandle();
                try
                {
                    var opening = await Capture(name + "-展开", window, window.Show, sampleDurationMs: 1200);
                    var closing = await Capture(name + "-收起", window, window.Hide);
                    evidence.Add(new { name, opening, closing }); Write();
                    Assert(opening.Any(x => x.WhiteLinePixels > window.Height * .1) && closing.Any(x => x.WhiteLinePixels > window.Height * .1), name + "未经内容/动画预热，第一次打开及关闭均有中央泛光白条");
                    Assert(opening.Concat(closing).Any(x => x.WhiteTopMargin <= 3 && x.WhiteBottomMargin <= 3), name + "首次白条贯穿窗口上下边缘");
                }
                finally { window.Destroy(false); }
            }
            if (!restorationOnly) foreach (var (name, window) in new[] { ("新建", draftWindow), ("编辑", editWindow), ("设置", (SurfaceWindow)settings) })
            {
                window.Left = area.Left + (area.Width - window.Width) / 2; window.Top = area.Top + (area.Height - window.Height) / 2;
                window.WarmAnimation(); await Task.Delay(100);
                var opening = await Capture(name + "-展开", window, window.Show);
                Assert(window.IsVisible && !window.IsScrollAnimating && !window.UsesIndependentAnimation && window.LiveScrollRevealCount > 0 && window.RestingHandoffCount == 0, name + "真实控件展开且无截图交接");
                var closing = await Capture(name + "-收起", window, window.Hide);
                Assert(!window.IsVisible && !window.IsScrollAnimating, name + "收起结束没有残留窗口");
                evidence.Add(new { name, opening, closing }); Write();
                // A 25ms flash can fall between full-desktop capture frames.
                // Keep the original evidence and retry at most two cycles.
                for (var attempt = 1; attempt <= 2 && (!opening.Any(x => x.WhiteLinePixels > window.Height * .1) || !closing.Any(x => x.WhiteLinePixels > window.Height * .1)); attempt++)
                {
                    opening = await Capture(name + "-展开-重采" + attempt, window, window.Show);
                    closing = await Capture(name + "-收起-重采" + attempt, window, window.Hide);
                    evidence.Add(new { name, attempt, opening, closing }); Write();
                }
                Assert(opening.Any(x => x.WhiteLinePixels > window.Height * .1) && closing.Any(x => x.WhiteLinePixels > window.Height * .1), name + "打开与关闭均采到中央白线");
                Assert(opening.Any(x => x.PaintedWidth > 60 && x.PaintedWidth < x.TotalWidth - 80) && closing.Any(x => x.PaintedWidth > 60 && x.PaintedWidth < x.TotalWidth - 80), name + "展开与收起均有可见中间帧");
                // The draft window also contains a transparent add-entry strip.
                // Measure symmetry while both moving edges cross the solid editor.
                var intermediate = opening.Concat(closing).Where(x => x.PaintedWidth > 60 && x.PaintedWidth < x.TotalWidth * .6).ToArray();
                Assert(intermediate.Length > 0 && intermediate.All(x => Math.Abs(x.LeftMargin - x.RightMargin) <= 8), name + "从中央向左右对称展开收起");
                Assert(opening.Concat(closing).Any(x => x.WhiteTopMargin <= 3 && x.WhiteBottomMargin <= 3), name + "竖白线与窗口上下边缘对齐");
                Write();
                window.Show(); await Task.Delay(45); var before = window.ScrollProgress; window.Hide(); var after = window.ScrollProgress;
                Assert(Math.Abs(before - after) < .09, name + "展开途中反向从当前位置继续"); await Task.Delay(400);
                Assert(!window.IsVisible, name + "快速反向最终隐藏");
                window.Show(); await Task.Delay(400); window.Hide(); await Task.Delay(35); before = window.ScrollProgress; window.Show(); after = window.ScrollProgress;
                Assert(Math.Abs(before - after) < .09, name + "收起途中反向从当前位置继续"); await Task.Delay(400);
                Assert(window.IsVisible && !window.IsScrollAnimating, name + "反向重开最终完整可见");
                window.Hide(); await Task.Delay(400); Write();
            }
            // Exercise restoration through the workspace, rather than only
            // calling Show/Hide on a standalone floating host.
            foreach (var (name, window) in new[] { ("新建", draftWindow), ("编辑", editWindow), ("设置", (SurfaceWindow)settings) })
            {
                var layer = window.WindowLayer; window.WindowLayer = 100;
                window.Show(); await Task.Delay(400);
                var openingWhiteCycles = 0; var closingWhiteCycles = 0; var quickWhiteCycles = 0;
                for (var cycle = 1; cycle <= 4; cycle++)
                {
                    var closing = await Capture(name + "-整组收起-" + cycle, window, () => controller.SetExpanded(false), 600);
                    Assert(!window.IsVisible, name + "整组收起第" + cycle + "次隐藏原悬浮窗口");
                    var opening = await Capture(name + "-整组展开-" + cycle, window, () => controller.SetExpanded(true), 600);
                    Assert(window.IsVisible && !window.IsScrollAnimating, name + "整组展开第" + cycle + "次恢复原悬浮窗口");
                    evidence.Add(new { name, cycle, workspace = true, opening, closing }); Write();
                    if (opening.Any(x => x.WhiteLinePixels > window.Height * .1)) openingWhiteCycles++;
                    if (closing.Any(x => x.WhiteLinePixels > window.Height * .1)) closingWhiteCycles++;
                }
                for (var cycle = 1; cycle <= 3; cycle++)
                {
                    controller.SetExpanded(false); await Task.Delay(35);
                    var opening = await Capture(name + "-快速整组重开-" + cycle, window, () => controller.SetExpanded(true), 600);
                    evidence.Add(new { name, cycle, quickReopen = true, opening }); Write();
                    if (opening.Any(x => x.WhiteLinePixels > window.Height * .1)) quickWhiteCycles++;
                    Assert(window.IsVisible && !window.IsScrollAnimating, name + "快速重开第" + cycle + "次完整交接");
                }
                // Live UI clipping can skip a short flare frame when busy, as
                // explicitly accepted. Retain every sample/missed cycle; require
                // real white-bar evidence for all three restoration routes.
                evidence.Add(new { name, openingWhiteCycles, closingWhiteCycles, quickWhiteCycles, totalOpen = 4, totalClose = 4, totalQuick = 3 });
                Assert(openingWhiteCycles > 0 && closingWhiteCycles > 0 && quickWhiteCycles > 0, name + "整组展开/收起/快速重开均采到中央白条，逐轮漏采保留证据");
                window.Hide(); await Task.Delay(400); window.WindowLayer = layer;
            }
            if (restorationOnly) { Write(); return; }
            editor.Show(); await Task.Delay(400);
            var title = Descendants<TextBox>(editor).First(); var original = created.Working.Title; title.Text = "实验修改不应保存";
            var editClose = Descendants<Button>(editor).Single(x => Equals(x.Tag, "EditorClose")); Invoke(editClose);
            await WaitForClosing(editWindow); Assert(editWindow.IsVisible && editWindow.IsScrollAnimating, "编辑X触发收起而非直接销毁");
            await Task.Delay(400); Assert(!editWindow.IsVisible && controller.Engine.State.Tasks.Single(x => x.Id == created.Working.Id).Title == original, "编辑X收起后仍放弃未提交修改");
            draft.Show(); await Task.Delay(400);
            var draftTitle = Descendants<TextBox>(draft).First(); draftTitle.Text = "实验草稿仍会暂存";
            Invoke(Descendants<Button>(draft).Single(x => Equals(x.Tag, "EditorClose")));
            await WaitForClosing(draftWindow); Assert(draftWindow.IsVisible && draftWindow.IsScrollAnimating, "最后一个新建X保留画面收起");
            await Task.Delay(400); Assert(!draftWindow.IsVisible && controller.Engine.State.Sessions.Any(x => x.Working.Title == "实验草稿仍会暂存" && !x.Visible), "新建X收起后非空草稿仍暂存");
            editor = controller.EditTask(created.Working.Id); editWindow = editor.Host!; await Task.Delay(400);
            Descendants<TextBox>(editor).First().Text = "画卷实验保存结果";
            Invoke(Descendants<Button>(editor).First(x => Equals(x.Content, "保存任务")));
            await WaitForClosing(editWindow); Assert(editWindow.IsVisible && editWindow.IsScrollAnimating, "保存任务提交后播放画卷收起");
            await Task.Delay(400); Assert(!editWindow.IsVisible && controller.Engine.State.Tasks.Single(x => x.Id == created.Working.Id).Title == "画卷实验保存结果", "保存收起完成后修改只提交一次");
            settings.Show(); await Task.Delay(400);
            Invoke(Descendants<Button>(settings.Root).First(x => Equals(x.Content, "×")));
            await Task.Delay(400); controller.SetExpanded(false); await Task.Delay(400); controller.SetExpanded(true); await Task.Delay(400);
            Assert(!settings.IsVisible, "主动关闭的设置不会随整组重开复活");
            var fallback = new ScrollFallbackWindow { Left = area.Left + 40, Top = area.Top + 40 };
            fallback.Show(); Assert(fallback.IsVisible && !fallback.UsesIndependentAnimation && fallback.IsHitTestVisible, "无合成能力时仍可显示并交互");
            fallback.Hide(); Assert(!fallback.IsVisible, "无合成能力时仍能关闭窗口"); fallback.Destroy(false);
            Write();
        }
        finally { background.Destroy(false); }
        void Write() => File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "scroll-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, mouseMoved = false, durationMs = ScrollMotion.DurationMs, checks, evidence }, new JsonSerializerOptions { WriteIndented = true }));

        async Task<List<Frame>> Capture(string name, SurfaceWindow window, Action transition, int sampleDurationMs = 300)
        {
            var bounds = Native.ScreenBounds(window); var frames = new List<Frame>();
            File.WriteAllText(Path.Combine(directory, name + "-bounds.json"), JsonSerializer.Serialize(new {
                bounds, window.Left, window.Top, window.Width, window.Height, window.IsVisible,
                virtualLeft = SystemParameters.VirtualScreenLeft, virtualTop = SystemParameters.VirtualScreenTop,
                virtualWidth = SystemParameters.VirtualScreenWidth, virtualHeight = SystemParameters.VirtualScreenHeight }));
            // Sample the actual desktop on a worker, independent of UI preparation.
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var begin = new ManualResetEventSlim();
            var worker = Task.Run(() =>
            {
                var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                try
                {
                var width = (int)bounds.Width; var height = (int)bounds.Height; var rowY = (int)(height * .53);
                using var baseline = new Drawing.Bitmap(width, 1);
                using (var graphics = Drawing.Graphics.FromImage(baseline)) graphics.CopyFromScreen((int)bounds.X, (int)bounds.Y + rowY, 0, 0, baseline.Size);
                ready.TrySetResult(); begin.Wait(); var clock = Stopwatch.StartNew();
                var samples = new List<(double Ms, Drawing.Bitmap Row, Drawing.Bitmap Column, int WhiteBars, Drawing.Bitmap? WhiteImage)>();
                using var wholeImage = new Drawing.Bitmap(width, height); using var sampleGraphics = Drawing.Graphics.FromImage(wholeImage);
                while (clock.ElapsedMilliseconds < sampleDurationMs)
                {
                    // One desktop read keeps row and column in the same frame
                    // and avoids missing a brief flare between two GDI waits.
                    sampleGraphics.CopyFromScreen((int)bounds.X, (int)bounds.Y, 0, 0, wholeImage.Size);
                    var hits = new int[width]; var rowBytes = new byte[width * 4];
                    var bits = wholeImage.LockBits(new Drawing.Rectangle(0, 0, width, height), Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        foreach (var fraction in new[] { .2, .3, .4, .53, .65, .8 })
                        {
                            System.Runtime.InteropServices.Marshal.Copy(bits.Scan0 + (int)(height * fraction) * bits.Stride, rowBytes, 0, rowBytes.Length);
                            for (var x = 0; x < width; x++) if (rowBytes[x * 4] > 170 && rowBytes[x * 4 + 1] > 170 && rowBytes[x * 4 + 2] > 170) hits[x]++;
                        }
                    }
                    finally { wholeImage.UnlockBits(bits); }
                    var bars = 0; var previousWhite = false;
                    for (var x = 0; x < width; x++)
                    {
                        var whiteHere = hits[x] >= 5;
                        if (whiteHere && !previousWhite) bars++;
                        previousWhite = whiteHere;
                    }
                    samples.Add((clock.Elapsed.TotalMilliseconds,
                        wholeImage.Clone(new Drawing.Rectangle(0, rowY, width, 1), Drawing.Imaging.PixelFormat.Format32bppArgb),
                        wholeImage.Clone(new Drawing.Rectangle(width / 2 - 3, 0, 7, height), Drawing.Imaging.PixelFormat.Format32bppArgb), bars,
                        bars > 0 ? (Drawing.Bitmap)wholeImage.Clone() : null)); Thread.Sleep(1);
                }
                for (var i = 0; i < samples.Count; i++)
                {
                    using var row = samples[i].Row; using var column = samples[i].Column;
                    using var whiteImage = samples[i].WhiteImage;
                    whiteImage?.Save(Path.Combine(directory, name + "-white-" + i.ToString("D3") + ".png"), Drawing.Imaging.ImageFormat.Png);
                    row.Save(Path.Combine(directory, name + "-" + i.ToString("D3") + "-row.png"), Drawing.Imaging.ImageFormat.Png);
                    column.Save(Path.Combine(directory, name + "-" + i.ToString("D3") + "-column.png"), Drawing.Imaging.ImageFormat.Png);
                    var left = width; var right = -1;
                    for (var x = 0; x < width; x++)
                    {
                        // Ignore the glow when measuring the panel aperture.
                        if (Math.Abs(x - width / 2) < 28) continue;
                        var a = row.GetPixel(x, 0); var b = baseline.GetPixel(x, 0);
                        // Closing starts from a visible panel; use the controlled background.
                        if (name.Contains("-收起")) b = Drawing.Color.FromArgb(51, 70, 95);
                        if (Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B) > 35) { left = Math.Min(left, x); right = Math.Max(right, x); }
                    }
                    var white = 0; var whiteTop = height; var whiteBottom = -1;
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < 7; x++)
                        {
                            var c = column.GetPixel(x, y);
                            if (c.R > 170 && c.G > 170 && c.B > 170) { white++; whiteTop = Math.Min(whiteTop, y); whiteBottom = y; break; }
                        }
                    }
                    frames.Add(new Frame(samples[i].Ms, width, right >= left ? right-left+1 : 0, left, right < 0 ? width : width-1-right, white, whiteTop, whiteBottom < 0 ? height : height-1-whiteBottom, samples[i].WhiteBars));
                }
                return frames;
                }
                catch (Exception error) { ready.TrySetException(error); throw; }
                finally { SetThreadDpiAwarenessContext(previousDpi); }
            });
            await ready.Task; begin.Set(); transition();
            var collected = await worker;
            File.WriteAllText(Path.Combine(directory, name + "-motion.json"), JsonSerializer.Serialize(new {
                window.UsesIndependentAnimation, window.LastAnimationError, window.MotionDisplayStartTicks,
                window.LastAnimationPreparationMs, window.LastSnapshotMs, window.LastShowMs, window.MotionGeneration,
                window.IsVisible, window.ScrollProgress, window.ScrollLight }));
            return collected;
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    private sealed record Frame(double Ms, int TotalWidth, int PaintedWidth, int LeftMargin, int RightMargin, int WhiteLinePixels, int WhiteTopMargin, int WhiteBottomMargin, int WhiteBars);
    private sealed class ScrollFallbackWindow : SurfaceWindow
    {
        protected override bool ScrollEnabled => true;
        internal ScrollFallbackWindow() : base(240, 180) { GlassEnabled = false; }
    }
    private static async Task WaitForClosing(SurfaceWindow window)
    {
        var clock = Stopwatch.StartNew();
        while (window.IsVisible && !window.IsScrollAnimating && clock.ElapsedMilliseconds < 3000) await Task.Delay(2);
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result) yield return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
}
