using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

internal static class Ux0915RenderChecks
{
    internal static async Task Run(BoardController controller)
    {
        controller.SuppressCollapse = true; controller.SetExpanded(true); await Task.Delay(300);
        var area = controller.WorkArea(controller.Ball); var root = controller.Store.DirectoryPath;
        var checks = new List<string>(); var measurements = new List<object>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.WriteAllText(Path.Combine(root, "ux0915-render-progress.json"), JsonSerializer.Serialize(checks)); }
        var backing = new SurfaceWindow(area.Width - 20, area.Height - 20) { GlassEnabled = false, FloatingAnimations = false, WindowLayer = -1, Left = area.Left + 10, Top = area.Top + 10 };
        backing.Root.Background = Brushes.DimGray; backing.Show();
        try
        {
            var draft = controller.NewTask(Lane.Action);
            var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "实屏展开测试"; var item = controller.Engine.Submit(session);
            var editor = controller.EditTask(item.Id);
            controller.ToggleSettings();
            var windows = new[] { draft.Host!, editor.Host!, Application.Current.Windows.OfType<SettingsSurface>().Single() };
            foreach (var window in windows) { window.Hide(); await Task.Delay(180); }
            foreach (var window in windows)
            {
                window.Left = area.Left + 45; window.Top = area.Top + 35;
                for (var cycle = 0; cycle < (window == draft.Host ? 8 : 2); cycle++)
                {
                    var clock = Stopwatch.StartNew(); window.Show();
                    // Isolated startup-check placement intentionally moves
                    // floating hosts offscreen. Bring only this test host back.
                    window.Left = area.Left + 45; window.Top = area.Top + 35;
                    // BeforeOpen may recenter an editor/settings window. Read
                    // coordinates after Show, never sample its stale hidden HWND.
                    var rect = Native.ScreenBounds(window); var scale = rect.Height / window.Height;
                    var x = (int)(rect.Left + 8 * scale); var y = (int)(rect.Top + 80 * scale);
                    var samples = new List<(double Ms, double Mean)>(); var ready = new TaskCompletionSource();
                    var recording = Task.Run(() =>
                    {
                        using var bitmap = new Drawing.Bitmap(8, 12); using var paint = Drawing.Graphics.FromImage(bitmap);
                        ready.SetResult();
                        while (clock.ElapsedMilliseconds < 450)
                        {
                            paint.CopyFromScreen(x, y, 0, 0, bitmap.Size);
                            var sum = 0.0;
                            for (var row = 0; row < bitmap.Height; row++) for (var col = 0; col < bitmap.Width; col++) { var pixel = bitmap.GetPixel(col, row); sum += (pixel.R + pixel.G + pixel.B) / 3.0; }
                            samples.Add((clock.Elapsed.TotalMilliseconds, sum / 96)); Thread.Sleep(3);
                        }
                    });
                    await ready.Task;
                    if (cycle == 3) { await Task.Delay(30); Thread.Sleep(45); }
                    await recording; await Ux0914Checks.Settled(window);
                    var baseline = samples.TakeLast(8).Average(s => s.Mean);
                    var late = samples.Where(s => s.Ms >= 170).ToArray();
                    var delta = late.Max(s => Math.Abs(s.Mean - baseline));
                    measurements.Add(new { type = window.GetType().Name, cycle, baseline, delta, samples = samples.Select(s => new { s.Ms, s.Mean }) });
                    Check(late.Length > 10 && delta < 2 && baseline > 5 && baseline < 100, window.GetType().Name + "第" + (cycle + 1) + "次展开结束实际窗口背景稳定");
                    Check(!window.LiveContentHidden && !window.IsContentSwapHandoff && window.RestingHandoffCount == 0, window.GetType().Name + "第" + (cycle + 1) + "次无截图交接及隐藏真实内容");
                    if (cycle == 0)
                    {
                        var bounds = Native.ScreenBounds(window); using var shot = new Drawing.Bitmap((int)bounds.Width, (int)bounds.Height);
                        using (var paint = Drawing.Graphics.FromImage(shot)) paint.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0, shot.Size);
                        shot.Save(Path.Combine(root, window.GetType().Name + "-opened.png"));
                    }
                    window.Hide(); await Task.Delay(180);
                }
            }
            File.WriteAllText(Path.Combine(root, "ux0915-render-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, measurements, mouseMoved = false, productionDataChanged = false, boundary = "当前屏幕12轮打开结束背景采样，含45ms阻塞；不保证所有显卡、负载或混合DPI" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { backing.Destroy(false); }
    }
}
