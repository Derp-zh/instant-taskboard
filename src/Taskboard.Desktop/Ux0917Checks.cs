using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

// Visible isolated windows and screen sampling; no real Codex chat is touched.
internal static class Ux0917Checks
{
    [StructLayout(LayoutKind.Sequential)] private struct Box { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr rgn, out Box box);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr rgn);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr rgn);
    [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint point);
    private static double RegionHeight(SurfaceWindow window)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try { GetWindowRgn(new System.Windows.Interop.WindowInteropHelper(window).Handle, region); GetRgnBox(region, out var box); return box.Bottom - box.Top; }
        finally { DeleteObject(region); }
    }
    internal static async Task Run(BoardController controller, bool glassOnly = false)
    {
        if (!controller.Isolated || controller.Smoke) throw new InvalidOperationException("仅允许可见隔离窗口");
        controller.VerifyCardAnimations = true; controller.SuppressCollapse = true;
        await controller.Start();
        var root = controller.Store.DirectoryPath; var checks = new List<string>(); var traces = new List<object>();
        void Check(bool value, string text)
        { if (!value) throw new InvalidOperationException(text); checks.Add(text); File.WriteAllText(Path.Combine(root, "ux0917-progress.json"), JsonSerializer.Serialize(checks)); }
        var area = controller.WorkArea(controller.Ball);
        var backing = new SurfaceWindow(area.Width - 20, area.Height - 20) { GlassEnabled = false, FloatingAnimations = false,
            WindowLayer = -1, Left = area.Left + 10, Top = area.Top + 10 };
        backing.Root.Background = Brushes.LightGray; backing.Show();
        var session = controller.Engine.NewSession(Lane.Running); session.StatusSelected = true;
        session.Working.Title = "自动移列隔离检查";
        session.Working.Stages[0].Codex = new CodexBinding { ThreadId = "visual-fixture", TurnId = "visual-turn", SourceEpoch = "visual-source", Phase = CodexPhase.Running };
        var item = controller.Engine.Submit(session);
        for (var i = 0; i < controller.Columns.Count; i++)
        { var column = controller.Columns[i]; column.Left = area.Left + 20 + i * 160; column.Top = area.Top + 20; column.Width = 152; column.Height = Math.Min(500, area.Height - 50); }
        try
        {
            var listFrames = new List<object>();
            EventHandler frame = (_, _) => listFrames.Add(new { ticks = Stopwatch.GetTimestamp(), heights = controller.Columns.Select(RegionHeight).ToArray(),
                ball = Native.ScreenBounds(controller.Ball), content = controller.Ball.LiveContentBounds,
                search = StageFeatureChecks.Children<TextBox>(controller.Ball.Root).First().PointToScreen(new Point(0, 0)) });
            CompositionTarget.Rendering += frame;
            controller.SetExpanded(true);
            var coldDeadline = Stopwatch.StartNew();
            while (controller.Columns.Any(x => x.IsRevealAnimating) && coldDeadline.ElapsedMilliseconds < 4000) await Task.Delay(50);
            CompositionTarget.Rendering -= frame;
            traces.Add(new { label = "cold-list-reveal", frames = listFrames });
            Check(controller.Columns.All(x => x.LiveListRevealCount > 0), "六列均实际进入真实控件下拉路径");
            Check(controller.Columns.All(x => !x.IsRevealAnimating && !x.LiveContentHidden), "六列完成后真实控件保持可见");
            var draft = controller.NewTask(Lane.Running); var editor = controller.EditTask(item.Id);
            Check(Math.Abs(editor.Host!.Height - 550) < .01 && Math.Abs(draft.Height - 550) < .01, "新建编辑高度650缩为550、宽度保持430");
            Check(editor.Host.Width == 430 && draft.Width == 430, "新建编辑宽度与字体未缩放");
            // Sample a plain floating background while the lists open beneath it.
            foreach (var window in Enumerable.Range(0, glassOnly ? 0 : 6).Select(i => i % 2 == 0 ? editor.Host! : draft.Host!))
            {
                controller.SetExpanded(false); await Task.Delay(300);
                var pixels = new List<object>(); var clock = new Stopwatch();
                var floatingSamples = new List<double>(); var properties = new List<object>(); var occluded = 0; var anomalyCaptured = false;
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var ready = new TaskCompletionSource();
                var recording = Task.Run(() =>
                {
                    using var bitmap = new Drawing.Bitmap(3, 8); using var graphics = Drawing.Graphics.FromImage(bitmap);
                    ready.SetResult();
                    while (!clock.IsRunning) Thread.Sleep(1);
                    while (clock.ElapsedMilliseconds < 6200)
                    {
                        var scale = Native.GetDpiForWindow(handle) / 96.0;
                        var bounds = Native.ScreenBounds(handle);
                        var point = new PixelPoint { X = (int)(bounds.Left + 8 * scale), Y = (int)(bounds.Top + 85 * scale) };
                        var ownPixel = Native.GetAncestor(WindowFromPoint(point), 2) == handle;
                        graphics.CopyFromScreen(point.X, point.Y, 0, 0, bitmap.Size);
                        double sum = 0, brightest = 0;
                        for (var y = 0; y < 8; y++) for (var x = 0; x < 3; x++) { var c = bitmap.GetPixel(x, y); var light = (c.R + c.G + c.B) / 3.0; sum += light; brightest = Math.Max(brightest, light); }
                        var mean = sum / 24;
                        if (mean > 100 && clock.ElapsedMilliseconds >= 1000 && !anomalyCaptured)
                        {
                            anomalyCaptured = true;
                            using var shot = new Drawing.Bitmap((int)bounds.Width, (int)bounds.Height);
                            using var paint = Drawing.Graphics.FromImage(shot);
                            paint.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0, shot.Size);
                            shot.Save(Path.Combine(root, $"unexpected-background-{traces.Count}.png"));
                        }
                        lock (pixels) pixels.Add(new { ms = clock.Elapsed.TotalMilliseconds, mean, ownPixel });
                        // The user explicitly allows backdrop changes during
                        // reveal. Examine the settled surface, including the
                        // several-seconds-later interval of the reported bug.
                        if (clock.ElapsedMilliseconds >= 1000) { if (ownPixel) floatingSamples.Add(mean); else occluded++; }
                        Thread.Sleep(20);
                    }
                });
                await ready.Task;
                clock.Start(); controller.SetExpanded(true);
                window.Left = area.Left + 225; window.Top = area.Top + 60;
                Native.Raise(window);
                while (clock.ElapsedMilliseconds < 6250)
                {
                    properties.Add(new { ms = clock.Elapsed.TotalMilliseconds, window.Opacity, rootOpacity = window.Root.Opacity,
                        tint = (window.Root.Background as SolidColorBrush)?.Color.ToString(), window.Left, window.Top,
                        window.IsRevealAnimating, window.LiveContentHidden,
                        columns = controller.Columns.Select(x => new { x.Left, x.Top, x.Width, x.Height, x.IsRevealAnimating }).ToArray() });
                    await Task.Delay(50);
                }
                await recording;
                traces.Add(new { label = window == editor.Host ? "editor-over-lists" : "draft-over-lists", pixels, properties, occluded });
                Check(floatingSamples.Count > 120 && floatingSamples.Max() - floatingSamples.Min() <= 3,
                    (window == editor.Host ? "编辑" : "新建") + "同步展开完成1至6秒无突然变暗（静态隔离背景）");
            }
            var sequence = 0L;
            void Move(bool waiting)
            {
                var old = item.Lane;
                var message = JsonSerializer.SerializeToElement(new { method = "probe/snapshot", @params = new { threadId = "visual-fixture",
                    status = new { type = "active", activeFlags = waiting ? new[] { "waitingOnUserInput" } : Array.Empty<string>() } } });
                if (CodexState.Apply(item, item.Stages[0].Id, message, ++sequence, 1, "visual-source", true)) controller.NotifyCodexChange(item, old);
            }
            var running = controller.Columns.Single(x => x.Definition.BuiltinLane == Lane.Running);
            var action = controller.Columns.Single(x => x.Definition.BuiltinLane == Lane.Action);
            running.Locate(item.Id);
            Move(true);
            Check(item.Lane == Lane.Action, "非用户移列先立即提交正确后台状态");
            await controller.Flush();
            Check(controller.Store.Load().Tasks.Single(x => x.Id == item.Id).Lane == Lane.Action, "动画中实际存储已经保存目标列");
            await controller.PendingAutomaticMoves;
            Check(action.RenderedCard(item.Id) is not null && running.RenderedCard(item.Id) is null, "移出上拉后卡片实际进入待操作");
            Check(controller.AutomaticExitCount > 0 && controller.AutomaticEntryCount > 0, "移出和反向下拉移入均实际运行");
            var entered = action.RenderedCard(item.Id)!;
            Check(double.IsNaN(entered.Height) && entered.IsHitTestVisible && entered.TaskExpandCount > 0, "移入完成恢复自然高度及原操作范围");
            item.TimerUnread = true; Move(false); await controller.PendingAutomaticMoves;
            Check(item.Lane == Lane.Running && !item.TimerUnread && running.RenderedCard(item.Id) is not null, "恢复执行反向移列动画及清红点保留");
            Move(true); await Task.Delay(35); Move(false); await controller.PendingAutomaticMoves; await Task.Delay(300);
            Check(controller.Columns.Count(x => x.RenderedCard(item.Id) is not null) == 1 && running.RenderedCard(item.Id)?.IsHitTestVisible == true,
                "快速反向不残留重复卡片或被旧动画压成零高度");
            controller.SetExpanded(false); await Task.Delay(250); var oldCount = controller.AutomaticExitCount;
            Move(true); await controller.PendingAutomaticMoves;
            Check(controller.AutomaticExitCount == oldCount && item.Lane == Lane.Action, "隐藏时只更新后台、不强制展开或播放移列");
            controller.SetExpanded(true); await Task.Delay(350);
            Check(action.Locate(item.Id) is not null && controller.Columns.Count(x => x.RenderedCard(item.Id) is not null) == 1, "恢复整组后显示最新唯一卡片");
            Move(false); await controller.PendingAutomaticMoves;
            item.Reminder.Enabled = true; item.Reminder.NextLocal = DateTime.Now.AddSeconds(-1);
            var reminderExitCount = controller.AutomaticExitCount;
            var reminderOrder = controller.Engine.State.Tasks.Select(x => x.Id).ToArray();
            var due = controller.TickReminders(DateTime.Now); await controller.PendingAutomaticMoves;
            Check(due.Contains(item.Id) && item.Lane == Lane.Action && item.TimerUnread && controller.AutomaticExitCount > reminderExitCount,
                "定时提醒移列同样播放上拉下拉且保留提醒红点");
            Check(reminderOrder.SequenceEqual(controller.Engine.State.Tasks.Select(x => x.Id)), "提醒动画不改变原任务顺序");
            // A plain backing cannot demonstrate blur visually. Use a patterned
            // isolated backing and measure the actual configured transparency.
            var originalTransparency = SurfaceWindow.FloatingTransparency;
            foreach (var glassWindow in new[] { editor.Host!, draft.Host! })
            {
            foreach (var other in new[] { editor.Host!, draft.Host! }.Where(x => x != glassWindow)) other.Hide();
            await Task.Delay(250); glassWindow.Show();
            glassWindow.Left = area.Right - glassWindow.Width - 15; glassWindow.Top = area.Top + 40;
            Native.Raise(glassWindow); await Task.Delay(250);
            foreach (var testTransparency in new[] { originalTransparency, 60.0 })
            {
            SurfaceWindow.SetTransparency(controller.Engine.State.Preferences.GlassTransparency, testTransparency);
            double[] ScanGlass()
            {
                var bounds = Native.ScreenBounds(glassWindow); var scale = bounds.Width / glassWindow.Width;
                using var bitmap = new Drawing.Bitmap(120, 1); using var paint = Drawing.Graphics.FromImage(bitmap);
                paint.CopyFromScreen((int)(bounds.Left + 30 * scale), (int)(bounds.Top + 65 * scale), 0, 0, bitmap.Size);
                return Enumerable.Range(0, 120).Select(x => { var pixel = bitmap.GetPixel(x, 0); return (pixel.R + pixel.G + pixel.B) / 3.0; }).ToArray();
            }
            backing.Root.Background = Brushes.White; await Task.Delay(350); var overWhite = ScanGlass();
            backing.Root.Background = Brushes.Black; await Task.Delay(350); var overBlack = ScanGlass();
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
            drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 6, 12))));
            backing.Root.Background = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 12, 12), Stretch = Stretch.Fill };
            await Task.Delay(500); var overStripes = ScanGlass();
            var transparencyResponse = Math.Abs(overWhite.Average() - overBlack.Average());
            var stripeContrast = overStripes.Max() - overStripes.Min();
            traces.Add(new { label = "configured-glass", type = glassWindow.GetType().Name, glassWindow.BackdropMode, transparency = glassWindow.GlassTransparency,
                overWhite, overBlack, overStripes, transparencyResponse, stripeContrast });
            Check(glassWindow.BackdropMode == "实时系统毛玻璃" && transparencyResponse > 2,
                glassWindow.GetType().Name + "当前透明度下实际透出隔离背景、未降级成不透明背景");
            Check(stripeContrast < transparencyResponse * .7, glassWindow.GetType().Name + "细条纹实际被模糊、不是仅透明而没有毛玻璃");
            var shotBounds = Native.ScreenBounds(glassWindow);
            using (var screenshot = new Drawing.Bitmap((int)shotBounds.Width, (int)shotBounds.Height))
            {
                using var paint = Drawing.Graphics.FromImage(screenshot);
                paint.CopyFromScreen((int)shotBounds.Left, (int)shotBounds.Top, 0, 0, screenshot.Size);
                screenshot.Save(Path.Combine(root, glassWindow.GetType().Name + "-glass-" + testTransparency + ".png"));
            }
            }
            SurfaceWindow.SetTransparency(controller.Engine.State.Preferences.GlassTransparency, originalTransparency);
            }
            await controller.Flush();
            File.WriteAllText(Path.Combine(root, "ux0917-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, traces,
                syntheticCodexSignals = true, productionDataChanged = false, mouseMoved = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { File.WriteAllText(Path.Combine(root, "ux0917-traces.json"), JsonSerializer.Serialize(traces)); backing.Destroy(false); }
    }
}
