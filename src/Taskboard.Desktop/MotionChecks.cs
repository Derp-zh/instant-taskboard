using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

// Isolated desktop evidence, not a mock of the compositor or a window-region check.
internal static class MotionChecks
{
    public static async Task Run(BoardController controller)
    {
        var output = Path.Combine(controller.Store.DirectoryPath, "motion-artifacts"); Directory.CreateDirectory(output);
        var reports = new List<object>(); var area = controller.WorkArea(controller.Ball);
        controller.SuppressCollapse = true; controller.SetExpanded(false); await Task.Delay(180);
        var background = new SurfaceWindow(area.Width, area.Height) { GlassEnabled = false, WindowLayer = -100, Left = area.Left, Top = area.Top };
        var red = Ui.Brush("#DA555A"); var blue = Ui.Brush("#487ADD"); background.Root.Background = red;
        background.Show(); controller.SetGlassTransparency(60); controller.SetBallTransparency(60);
        var surfaces = new List<(string Name, SurfaceWindow Window)>();
        surfaces.AddRange(controller.Columns.Select(x => ("列-" + Names.Lanes[(int)x.Lane], (SurfaceWindow)x)));
        var draft = controller.NewTask(Lane.Running); surfaces.Add(("新建堆叠", draft.Host!));
        var created = controller.Engine.NewSession(Lane.Action); created.Working.Title = "动态背景隔离检查";
        controller.Engine.Submit(created); var taskId = created.Working.Id;
        var editor = controller.EditTask(taskId); surfaces.Add(("已建编辑", editor.Host!));
        controller.Settings(); surfaces.Add(("设置", Application.Current.Windows.OfType<SettingsSurface>().Single()));
        var input = Descendants<TextBox>(controller.Ball.Root).Single(); input.Text = "动态背景隔离检查"; await Task.Delay(180);
        surfaces.Add(("搜索结果", Application.Current.Windows.OfType<SurfaceWindow>().Single(x => x.GetType() == typeof(SurfaceWindow) && Descendants<ListBox>(x.Root).Any())));
        surfaces.Add(("待操作预览", controller.Ball.PreviewForVerification()));
        Ui.Error("隔离画面检查"); surfaces.Add(("辅助提示", Application.Current.Windows.OfType<SurfaceWindow>().Last(x => x.Title == "即时任务看板提示")));
        controller.SetExpanded(false); await Task.Delay(180); surfaces.Add(("圆形悬浮球", controller.Ball));
        try
        {
            foreach (var (name, window) in surfaces)
            {
                foreach (var other in Application.Current.Windows.OfType<SurfaceWindow>().Where(x => x != background).ToArray()) other.Hide();
                window.Left = area.Left + (area.Width - window.Width) / 2; window.Top = area.Top + (area.Height - window.Height) / 2;
                background.Root.Background = red; await Task.Delay(100); using(var beforeShow=Capture(window,Path.Combine(output,name+"-显示前.png"))) { }
                window.Show(); Native.Raise(window); await Task.Delay(200);
                var redSample = await SettledSample(window,Drawing.Color.FromArgb(218,85,90));
                var outsideRed=ScreenPixel(background.PointToScreen(new Point(50,50)));
                background.Root.Background = blue; var backgroundUpdate=Stopwatch.StartNew(); var blueSample = await SettledSample(window,Drawing.Color.FromArgb(72,122,221)); backgroundUpdate.Stop();
                var outsideBlue=ScreenPixel(background.PointToScreen(new Point(50,50)));
                var dynamicDifference = Difference(redSample, blueSample);
                var nativeHides = window.NativeHideCount;
                var hides = 0; void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!(bool)args.NewValue) hides++; }
                window.IsVisibleChanged += VisibilityChanged;
                var start = new Point(window.Left, window.Top); var differences = new List<int>();
                try
                {
                    for (var frame = 0; frame < 18; frame++)
                    {
                        window.Left = start.X + 30 * Math.Sin(frame * Math.PI / 17); window.Top = start.Y + frame;
                        await Task.Delay(20); differences.Add(Difference(Sample(window), blueSample));
                        if (frame % 6 == 0 || frame == 17) { using var captured = Capture(window, Path.Combine(output, name + "-移动-" + frame + ".png")); }
                        if (!window.IsVisible) throw new InvalidOperationException(name + "移动中隐藏");
                    }
                    if (name == "新建堆叠") controller.EditorMoved(draft);
                    await Task.Delay(140); differences.Add(Difference(Sample(window), blueSample)); using var afterMove = Capture(window, Path.Combine(output, name + "-松开后.png"));
                }
                finally { window.IsVisibleChanged -= VisibilityChanged; }
                var nativeHideDifference = window.NativeHideCount - nativeHides;
                var passed = dynamicDifference > 25 && differences.Max() < 45 && hides == 0 && nativeHideDifference == 0 && window.BackdropMode == "实时系统毛玻璃";
                reports.Add(new { name, passed, mode = window.BackdropMode, stationaryBackgroundDifference = dynamicDifference, movementFrames = differences.Count,
                    backgroundOutsideDifference=Difference(outsideRed,outsideBlue),redSample=redSample.ToArgb(),blueSample=blueSample.ToArgb(),
                    backgroundUpdateObservedMs=backgroundUpdate.Elapsed.TotalMilliseconds,
                    maximumMovementColorChange = differences.Max(), unexpectedHides = hides, nativeHideEvents = nativeHideDifference });
                Write(); if (!passed) throw new InvalidOperationException(name + "动态背景或闪烁检查失败：" + JsonSerializer.Serialize(reports[^1]));
            }
            var ball = controller.Ball; ball.Hide(); await Task.Delay(140); using var under = Capture(ball, Path.Combine(output, "球隐藏时底色.png"));
            ball.Show(); Native.Raise(ball); await Task.Delay(180); using var rendered = Capture(ball, Path.Combine(output, "真正圆形悬浮球.png"));
            var corners = new List<Drawing.Point>(); var radius = rendered.Width / 2.0;
            for (var y = 2; y < rendered.Height - 2; y += 3)
                for (var x = 2; x < rendered.Width - 2; x += 3)
                    if (Math.Pow(x + .5 - radius, 2) + Math.Pow(y + .5 - radius, 2) > Math.Pow(radius + 2, 2)) corners.Add(new Drawing.Point(x, y));
            var cornerDifference = corners.Max(p => Difference(rendered.GetPixel(p.X, p.Y), under.GetPixel(p.X, p.Y)));
            reports.Add(new { name = "圆外像素无底板", passed = cornerDifference < 12, outsidePoints = corners.Count, maximumCornerDifference = cornerDifference }); Write();
            if (cornerDifference >= 12) throw new InvalidOperationException("圆形球外仍有方形底板");
            controller.RestoreDefaults(); ball.Left = area.Left + area.Width * .75; ball.Top = area.Bottom - ball.Height - 100;
            var opening = await ObserveAnimation(true); var closing = await ObserveAnimation(false);
            var ballScaleForFrames = Native.GetDpiForWindow(new WindowInteropHelper(ball).Handle) / 96.0;
            var intermediate = opening.Frames.Concat(closing.Frames).Any(x => x.VisibleBallWidthPixels > 68 * ballScaleForFrames && x.VisibleBallWidthPixels < 360 * ballScaleForFrames);
            reports.Add(new { name = "系统合成动画像素", passed = intermediate && ball.UsesIndependentAnimation,
                durationTargetMs = 100, opening, closing }); Write();
            if (!intermediate || !ball.UsesIndependentAnimation) throw new InvalidOperationException("展开没有中间画面或未使用系统合成");
            await DraftDealChecks.Run(controller, background);
            async Task<AnimationEvidence> ObserveAnimation(bool expand)
            {
                var frames = new List<AnimationFrame>();
                var clock = Stopwatch.StartNew(); controller.SetExpanded(expand);
                foreach (var auxiliary in Application.Current.Windows.OfType<SurfaceWindow>().Where(x => x != background && x != ball && x is not ColumnSurface).ToArray()) auxiliary.Hide();
                for (var frame = 0; frame < 16; frame++)
                {
                    var column = controller.Columns[0]; var bounds = Region(column);
                    var ballBounds = Native.ScreenBounds(ball);
                    using var capture = Capture(ball, Path.Combine(output, (expand ? "向左展开-" : "向右收起-") + frame + ".png"));
                    var firstPainted = capture.Width;
                    for (var x = 0; x < capture.Width; x++)
                        if (Difference(capture.GetPixel(x, capture.Height / 2), Drawing.Color.FromArgb(72, 122, 221)) > 30) { firstPainted = x; break; }
                    frames.Add(new AnimationFrame(clock.Elapsed.TotalMilliseconds, column.IsVisible ? bounds.Bottom : 0, (int)ballBounds.Width, capture.Width - firstPainted));
                    await Task.Delay(10);
                }
                return new AnimationEvidence(frames, null);
            }
        }
        finally
        {
            background.Destroy(); foreach (var window in Application.Current.Windows.OfType<SurfaceWindow>().Where(x => x.WindowLayer == 2).ToArray()) window.Hide();
            controller.Engine.Discard(draft.Session); controller.RemoveEditor(draft); draft.Destroy();
            controller.Engine.Discard(editor.Session); controller.RemoveEditor(editor); editor.Destroy(); controller.Engine.Delete(taskId);
            controller.SetGlassTransparency(20); controller.SetBallTransparency(40); controller.Engine.State.Preferences.Positions.Clear(); controller.RestoreDefaults();
            controller.Ball.Show(); controller.SetExpanded(false); controller.SuppressCollapse = true;
        }
        void Write() => File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "motion-report.json"), JsonSerializer.Serialize(new { sampledDesktopFrames = true, mouseMoved = false, reports }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static Drawing.Color Sample(SurfaceWindow window)
    {
        // Keep the probe away from moving rounded edges and text/input surfaces.
        // Window coordinates can lead the DWM-presented position by one frame.
        var local = window is BallSurface ? new Point(7, window.Height / 2)
            : window is DraftStackSurface or TaskEditorWindow ? new Point(window.GlassRegions().First().Bounds.Width / 2, window.Height - 25)
            : new Point(30, window.Height - 60);
        var point = window.PointToScreen(local); using var bitmap = new Drawing.Bitmap(3, 3);
        using (var graphics = Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen((int)point.X, (int)point.Y, 0, 0, bitmap.Size);
        return bitmap.GetPixel(1, 1);
    }
    private static async Task<Drawing.Color> SettledSample(SurfaceWindow window,Drawing.Color background)
    {
        var fraction=window.GlassTransparency/100;
        var expected=Drawing.Color.FromArgb((int)Math.Round(25*(1-fraction)+background.R*fraction),(int)Math.Round(35*(1-fraction)+background.G*fraction),(int)Math.Round(51*(1-fraction)+background.B*fraction));
        var clock=Stopwatch.StartNew();var sample=Sample(window);
        while(Difference(sample,expected)>15 && clock.Elapsed.TotalMilliseconds<2000) {await Task.Delay(25);sample=Sample(window);}
        if(Difference(sample,expected)>15) throw new InvalidOperationException(window.Title+"背景没有到达预期真实颜色，窗口可能被其他画面遮挡："+sample.ToArgb()+" / "+expected.ToArgb());
        return sample;
    }
    private static int Difference(Drawing.Color a, Drawing.Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    private sealed record AnimationFrame(double Ms, int ColumnNativeRegionHeightPixels, int BallWidthPixels, int VisibleBallWidthPixels);
    private sealed record AnimationEvidence(List<AnimationFrame> Frames, int? OutsideDifferenceNotSampled);
    [StructLayout(LayoutKind.Sequential)] private struct PixelRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int a, int b, int c, int d);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region, out PixelRect bounds);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    private static PixelRect Region(SurfaceWindow window)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try { GetWindowRgn(new WindowInteropHelper(window).Handle, region); GetRgnBox(region, out var bounds); return bounds; }
        finally { DeleteObject(region); }
    }
    private static Drawing.Color ScreenPixel(Point point)
    {
        using var bitmap = new Drawing.Bitmap(1, 1); using (var graphics = Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen((int)point.X, (int)point.Y, 0, 0, bitmap.Size);
        return bitmap.GetPixel(0, 0);
    }
    private static Drawing.Bitmap Capture(SurfaceWindow window, string path)
    {
        // Hidden WPF visuals can detach their PresentationSource. The native HWND
        // still supplies the rectangle needed for the before-show desktop sample.
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var scale = Native.GetDpiForWindow(handle) / 96.0; var bounds = Native.ScreenBounds(window);
        var bitmap = new Drawing.Bitmap((int)Math.Round(window.Width * scale), (int)Math.Round(window.Height * scale));
        using (var graphics = Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen((int)bounds.X, (int)bounds.Y, 0, 0, bitmap.Size);
        bitmap.Save(path, Drawing.Imaging.ImageFormat.Png); return bitmap;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result) yield return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
}
