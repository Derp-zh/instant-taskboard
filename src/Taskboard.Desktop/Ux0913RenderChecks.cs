using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

internal static class Ux0913RenderChecks
{
    internal static async Task Run(BoardController controller)
    {
        controller.SuppressCollapse = true;
        controller.SetExpanded(true); await Task.Delay(400);
        var checks = new List<string>(); var root = controller.Store.DirectoryPath;
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); File.WriteAllText(Path.Combine(root, "ux0913-render-progress.json"), JsonSerializer.Serialize(checks)); }
        var column = controller.Columns.Single(x => x.Lane == Lane.Action);
        var area = controller.WorkArea(column);
        var backing = new SurfaceWindow(900, 760) { GlassEnabled = false, FloatingAnimations = false, SuppressActivation = true, WindowLayer = -1, Left = area.Left + 20, Top = area.Top + 10 };
        backing.Root.Background = Brushes.DimGray; backing.Show();
        column.Left = backing.Left + 20; column.Top = backing.Top + 20; column.Height = 580; column.Show(); await Task.Delay(200);
        var entry = Children<Button>(column.Root).Single(x => Equals(x.Tag, "NewTaskEntry"));
        foreach (var fraction in new[] { .1, .25, .5, .75, .9 })
        {
            var point = entry.PointToScreen(new Point(entry.ActualWidth * fraction, entry.ActualHeight / 2));
            var hit = WindowFromPoint(new NativePoint((int)point.X, (int)point.Y));
            Check(hit == column.ContentHandle, "新建按钮横向" + fraction + "原生命中内容窗口，而非穿透背景");
        }
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "外部第一击闪烁诊断"; session.Working.Stages[0].Text = "正文";
        var item = controller.Engine.Submit(session);
        var editors = new[] { controller.EditTask(item.Id), controller.NewTask(Lane.Action) };
        controller.ToggleSettings();
        var windows = editors.Select(x => x.Host!).Append(Application.Current.Windows.OfType<SettingsSurface>().Single()).ToArray();
        var measurements = new List<object>();
        foreach (var window in windows)
        {
            foreach (var other in windows.Where(x => x != window)) Native.ShowWindow(new WindowInteropHelper(other).Handle, 0);
            window.Left = backing.Left + 290; window.Top = backing.Top + 20; window.Show(); Native.ShowWindow(new WindowInteropHelper(window).Handle, 4); Native.RaiseBall();
            await Task.Delay(600);
            var handle = new WindowInteropHelper(window).Handle;
            var physical = Native.ScreenBounds(window); var scale = Native.GetDpiForWindow(handle) / 96.0;
            var x = (int)(physical.Left + 8 * scale); var y = (int)(physical.Top + 70 * scale);
            var width = (int)Math.Ceiling(Math.Min(window.Width - 16, 340) * scale); var height = (int)Math.Ceiling(12 * scale);
            var oldMotion = window.MotionGeneration; var oldHides = window.NativeHideCount; var oldHandoff = window.RestingHandoffCount; var oldActivation = window.ActivationPaintsSuppressed;
            var frames = new List<(double Ms, double Mean)>(); var clock = Stopwatch.StartNew();
            var recording = Task.Run(async () =>
            {
                while (clock.ElapsedMilliseconds < 650)
                {
                    using var image = new Drawing.Bitmap(width, height);
                    using (var g = Drawing.Graphics.FromImage(image)) g.CopyFromScreen(x, y, 0, 0, image.Size);
                    var sum = 0.0;
                    for (var row = 0; row < height; row++) for (var i = 0; i < width; i++) { var c = image.GetPixel(i, row); sum += (c.R + c.G + c.B) / 3.0; }
                    frames.Add((clock.Elapsed.TotalMilliseconds, sum / (width * height))); await Task.Delay(3);
                }
            });
            await Task.Delay(160);
            controller.ObserveOutsidePress(window, true);
            controller.ObserveOutsidePress(window, false, new Point(30, 30), 1000);
            SendMessage(handle, 0x0086, IntPtr.Zero, IntPtr.Zero);
            Native.Raise(column);
            await Task.Delay(130); SendMessage(handle, 0x0086, new IntPtr(1), IntPtr.Zero);
            await recording;
            var baseline = frames.Where(f => f.Ms < 140).Average(f => f.Mean);
            var maxDelta = frames.Where(f => f.Ms > 170).Max(f => Math.Abs(f.Mean - baseline));
            measurements.Add(new { type = window.GetType().Name, baseline, maxDelta, frames = frames.Select(f => new { f.Ms, f.Mean }) });
            Check(window.ActivationPaintsSuppressed >= oldActivation + 2, window.GetType().Name + "激活切换经过保护，不触发系统非客户区重绘");
            Check(window.MotionGeneration == oldMotion && window.NativeHideCount == oldHides && window.RestingHandoffCount == oldHandoff && !window.LiveContentHidden, window.GetType().Name + "第一击及激活切换没有隐藏/重播/交接");
            Check(maxDelta < 2, window.GetType().Name + "第一击及激活切换背景实屏亮度稳定");
            using var shot = new Drawing.Bitmap((int)physical.Width, (int)physical.Height);
            using (var g = Drawing.Graphics.FromImage(shot)) g.CopyFromScreen((int)physical.Left, (int)physical.Top, 0, 0, shot.Size);
            shot.Save(Path.Combine(root, window.GetType().Name + ".png"));
            Native.ShowWindow(handle, 0);
        }
        controller.SetExpanded(false); await Task.Delay(300);
        var ball = controller.Ball; ball.RestoreFromEdge(); ball.Left = area.Right - 58; ball.Top = area.Top + 100;
        await Task.Delay(100);
        var corner = ball.OrbPointToScreen(new Point(56, 2));
        Check(WindowFromPoint(new NativePoint((int)corner.X, (int)corner.Y)) == ball.ContentHandle, "吸附球右上矩形区域实际原生可点击");
        Capture(ball, Path.Combine(root, "ball-docked.png"));
        ball.HideAtEdge(area); await Task.Delay(140);
        Check(Math.Abs(ball.EdgeVisibleClip.Width - 14.5) < 1, "真实屏幕右侧隐藏仅保留1/4原生区域");
        Capture(ball, Path.Combine(root, "ball-hidden.png"));
        ball.RestoreFromEdge(animate: true); await Task.Delay(140); Capture(ball, Path.Combine(root, "ball-restored.png"));
        backing.Destroy(false);
        await Ux0914ClosingChecks.Run(controller);
        File.WriteAllText(Path.Combine(root, "ux0913-render-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, measurements, mouseMoved = false, keyboardInjected = false, productionDataChanged = false, boundary = "原生命中与第一外点/激活消息实屏采样，不代表真实鼠标双击或所有显卡负载验收" }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Capture(SurfaceWindow window, string path)
    {
        var rect = Native.ScreenBounds(window);
        using var image = new Drawing.Bitmap((int)rect.Width, (int)rect.Height);
        using (var g = Drawing.Graphics.FromImage(image)) g.CopyFromScreen((int)rect.Left, (int)rect.Top, 0, 0, image.Size);
        image.Save(path);
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject => StageFeatureChecks.Children<T>(root);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
