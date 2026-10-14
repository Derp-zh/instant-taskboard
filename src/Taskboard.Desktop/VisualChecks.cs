using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using Drawing = System.Drawing;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Visible, isolated verification: requires coordination before launching.
internal static class VisualChecks
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint point);
    [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X, Y; }
    public static async Task Run(BoardController controller)
    {
        var output = Path.Combine(controller.Store.DirectoryPath, "visual-artifacts"); Directory.CreateDirectory(output);
        var area = controller.WorkArea(controller.Ball);
        var backdrop = new SurfaceWindow(440, 290) { GlassEnabled = false, WindowLayer = 0 };
        var pattern = new Canvas { Background = Brushes.White };
        for (var y = 0; y < 290; y += 8)
            for (var x = 0; x < 440; x += 8)
            {
                var bright = (x / 8 + y / 8) % 2 == 0;
                var color = x < 220 ? (bright ? "#FF5040" : "#742018") : (bright ? "#40FF70" : "#185C28");
                var square = new Rectangle { Width = 8, Height = 8, Fill = Ui.Brush(color) }; Canvas.SetLeft(square, x); Canvas.SetTop(square, y); pattern.Children.Add(square);
            }
        backdrop.Root.Children.Add(pattern); backdrop.Left = area.Left + (area.Width - 440) / 2; backdrop.Top = area.Top + (area.Height - 290) / 2;
        var glass = new SurfaceWindow(360, 210) { Left = backdrop.Left + 40, Top = backdrop.Top + 40 };
        controller.SuppressCollapse = true;
        try
        {
            backdrop.Show(); Native.Raise(backdrop); await Task.Delay(300);
            using var before = Capture(glass.Left, glass.Top, glass.Width, glass.Height, backdrop, Path.Combine(output, "pattern.png"));
            glass.Show(); glass.Activate(); SetForegroundWindow(new WindowInteropHelper(glass).Handle); await Task.Delay(700);
            using var after = Capture(glass.Left, glass.Top, glass.Width, glass.Height, glass, Path.Combine(output, "glass.png"));
            var beforeEdges = Edges(before); var afterEdges = Edges(after);
            var transmission = ColorSeparation(after);
            var ratio = afterEdges / Math.Max(1, beforeEdges);
            var passed = ratio < .55 && transmission > 12;
            controller.Ball.Activate(); await Task.Delay(300);
            using var inactive = Capture(glass.Left, glass.Top, glass.Width, glass.Height, glass, Path.Combine(output, "inactive-glass.png"));
            var inactiveRatio = Edges(inactive) / Math.Max(1, beforeEdges); var inactiveTransmission = ColorSeparation(inactive);
            passed &= inactiveRatio < .55 && inactiveTransmission > 12;
            var result = new { passed, mode = glass.BackdropMode, beforeEdges, afterEdges, edgeRatio = ratio, backgroundColorSeparation = transmission, inactiveEdgeRatio = inactiveRatio, inactiveColorSeparation = inactiveTransmission, mouseMoved = false };
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "glass-report.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new InvalidOperationException("实际背景毛玻璃检查失败：" + JsonSerializer.Serialize(result));
            var samples = new List<object>(); var separations = new List<double>();
            foreach (var value in new[] { 0, 20, 60, 100 })
            {
                controller.SetGlassTransparency(value); await Task.Delay(220);
                using var frame = Capture(glass.Left, glass.Top, glass.Width, glass.Height, glass, Path.Combine(output, "transparency-" + value + ".png"));
                var color = ColorSeparation(frame); var edge = Edges(frame) / Math.Max(1, beforeEdges);
                samples.Add(new { transparency = value, colorSeparation = color, edgeRatio = edge }); separations.Add(color);
                if (value > 0 && edge >= .55) throw new InvalidOperationException("透明度增加后背景模糊消失");
            }
            var percentagesPassed = separations[0] < 12 && separations[1] > 12 && separations[2] > separations[1] + 20 && separations[3] > separations[2] + 20;
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "transparency-report.json"), JsonSerializer.Serialize(new { passed = percentagesPassed, samples }));
            if (!percentagesPassed) throw new InvalidOperationException("透明度百分比视觉变化不符合预期");
            controller.SetGlassTransparency(20); glass.Hide();
            var draft = controller.NewTask(Lane.Paused); await Task.Delay(250); var host = draft.Host!;
            var paneWidth = controller.LastDraftBounds[0].Width;
            using var stack = Capture(host.Left, host.Top, host.Width, host.Height, host, Path.Combine(output, "transparent-outline-stack.png"));
            var scale = Native.GetDpiForWindow(new WindowInteropHelper(host).Handle) / 96.0;
            // Sample the clear outline interior, away from its stroke, plus or label.
            var ghostX = (int)((paneWidth + paneWidth / 10) * scale); var ghostY = (int)(425 * scale);
            var over = stack.GetPixel(ghostX, ghostY);
            Native.ShowWindow(new WindowInteropHelper(host).Handle, 0); await Task.Delay(180);
            using var underlying = Capture(host.Left, host.Top, host.Width, host.Height, host, Path.Combine(output, "outline-underlying.png"));
            var under = underlying.GetPixel(ghostX, ghostY); Native.ShowWindow(new WindowInteropHelper(host).Handle, 4); Native.RaiseBall();
            var difference = Math.Abs(over.R - under.R) + Math.Abs(over.G - under.G) + Math.Abs(over.B - under.B);
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "draft-host-report.json"), JsonSerializer.Serialize(new { singleNativeWindow = Application.Current.Windows.OfType<DraftStackSurface>().Count() == 1, clearOutlineInterior = difference < 15, colorDifference = difference }));
            controller.Engine.Discard(draft.Session); controller.RemoveEditor(draft); draft.Destroy(); controller.SetExpanded(false);
            if (difference >= 15) throw new InvalidOperationException("虚线入口仍有不透明底板");
        }
        finally { glass.Destroy(); backdrop.Destroy(); controller.SuppressCollapse = false; }
        // Check the rendered HWND, including the working-area boundary and actual hit target.
        var ball = controller.Ball; var work = controller.WorkArea(ball);
        controller.Clamp(ball);
        var inside = ball.Left >= work.Left && ball.Top >= work.Top && ball.Left + ball.Width <= work.Right + .5 && ball.Top + ball.Height <= work.Bottom + .5;
        controller.Settings(); await Task.Delay(250);
        var pixel = ball.PointToScreen(new Point(ball.Width - 29, 29));
        var accessible = Native.GetAncestor(WindowFromPoint(new PixelPoint { X = (int)pixel.X, Y = (int)pixel.Y }), 2) == new WindowInteropHelper(ball).Handle;
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "ball-report.json"), JsonSerializer.Serialize(new { insideWorkArea = inside, accessibleAboveSettings = accessible }));
        if (!inside || !accessible) throw new InvalidOperationException("悬浮球工作区或置顶命中检查失败");
    }
    private static Drawing.Bitmap Capture(double left, double top, double width, double height, Window window, string file)
    {
        var scale = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformToDevice.M11;
        var bitmap = new Drawing.Bitmap((int)(width * scale), (int)(height * scale));
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)(left * scale), (int)(top * scale), 0, 0, bitmap.Size);
        bitmap.Save(file, ImageFormat.Png); return bitmap;
    }
    private static double Edges(Drawing.Bitmap bitmap)
    {
        double sum = 0; int count = 0;
        for (var y = 20; y < bitmap.Height - 20; y += 3)
            for (var x = 20; x < bitmap.Width - 20; x++)
            { if (Math.Abs(x - bitmap.Width / 2) < 30) continue; var a = bitmap.GetPixel(x, y); var b = bitmap.GetPixel(x + 1, y); sum += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); count++; }
        return sum / Math.Max(1, count);
    }
    private static double ColorSeparation(Drawing.Bitmap bitmap)
    {
        double left = 0, right = 0; int count = 0;
        for (var y = 30; y < bitmap.Height - 30; y += 5)
            for (var x = 30; x < bitmap.Width / 2 - 30; x += 5)
            { var a = bitmap.GetPixel(x, y); var b = bitmap.GetPixel(x + bitmap.Width / 2, y); left += a.R - a.G; right += b.R - b.G; count++; }
        return (left - right) / Math.Max(1, count);
    }
}
