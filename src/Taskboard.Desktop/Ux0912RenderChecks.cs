using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

internal static class Ux0912RenderChecks
{
    internal static async Task Run(BoardController controller)
    {
        controller.SuppressCollapse = true;
        var columns = controller.Columns.Where(x => x.Definition.Visible).ToArray();
        foreach (var column in columns)
        {
            var session = controller.Engine.NewSession(column.Lane); session.Working.Title = "展开交接诊断";
            session.Working.Stages[0].Text = "仅隔离数据，无鼠标键盘输入"; controller.Engine.Submit(session);
        }
        var area = controller.WorkArea(columns[0]);
        var paneWidth = Math.Min(220, (area.Width * .9 - 40 - 12 * (columns.Length - 1)) / columns.Length);
        var totalWidth = 40 + (paneWidth + 12) * columns.Length;
        var backing = new SurfaceWindow(totalWidth, 400) { GlassEnabled = false, FloatingAnimations = false, SuppressActivation = true, Left = area.Left + 20, Top = area.Top + 40, WindowLayer = -1 };
        backing.Root.Background = Brushes.DimGray; backing.Show(); await Task.Delay(100);
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i]; column.Left = backing.Left + 20 + i * (paneWidth + 12); column.Top = backing.Top + 20;
            column.Width = paneWidth; column.Height = 340; column.WarmAnimation();
        }
        var all = new List<object>();
        var passed = 0;
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var oldTicks = columns.Select(x => x.MotionDisplayStartTicks).ToArray();
            var scale = Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(columns[0]).Handle) / 96.0;
            var sampleX = columns.Select(x => (int)((x.Left + x.Width - 70) * scale)).ToArray();
            var headingX = columns.Select(x => (int)((x.Left + 16) * scale)).ToArray();
            var rows = new[] { 260, 85, 31 }.Select(y => (int)((columns[0].Top + y) * scale)).ToArray();
            var width = (int)(40 * scale);
            var frames = new List<(double Ms, double[] MotionMs, bool[] Opening, double[] Mean)>();
            var captures = new HashSet<string>();
            var clock = Stopwatch.StartNew();
            // Sampling runs independently so a dispatcher stall during handoff
            // cannot hide the very frame we need to measure.
            var recording = Task.Run(async () =>
            {
                while (clock.ElapsedMilliseconds < 2200)
                {
                    var now = Stopwatch.GetTimestamp();
                    var motionMs = columns.Select((x, i) => x.MotionDisplayStartTicks == oldTicks[i] ? -1 : (now - x.MotionDisplayStartTicks) * 1000.0 / Stopwatch.Frequency).ToArray();
                    var name = motionMs[0] > 700 ? "late" : !columns[0].LiveContentHidden && motionMs[0] > 0 ? "live" : motionMs[0] > 105 ? "snapshot" : "";
                    var capture = name.Length > 0 && captures.Add(name) ? Path.Combine(controller.Store.DirectoryPath, "cycle-" + cycle + "-" + name + ".png") : null;
                    frames.Add((clock.Elapsed.TotalMilliseconds, motionMs, columns.Select(x => x.IsRevealAnimating).ToArray(), SampleRegions(sampleX, headingX, rows, width, capture)));
                    await Task.Delay(3).ConfigureAwait(false);
                }
            });
            controller.SetExpanded(true); await recording;
            var baseline = Enumerable.Range(0, columns.Length * 3).Select(i => frames.TakeLast(12).Average(x => x.Mean[i])).ToArray();
            // Measure first physical appearance separately from a later flash.
            // Native completion is not proof that DWM has presented that frame.
            var completedMs = Enumerable.Range(0, columns.Length).Select(i => frames.Where(x => x.MotionMs[i] > 0 && !x.Opening[i]).Select(x => x.Ms).DefaultIfEmpty(double.PositiveInfinity).Min()).ToArray();
            var appearedMs = Enumerable.Range(0, columns.Length * 3).Select(i => frames.Where(x => x.MotionMs[i % columns.Length] > 0 && Math.Abs(x.Mean[i] - baseline[i]) <= 2).Select(x => x.Ms).DefaultIfEmpty(double.PositiveInfinity).Min()).ToArray();
            var rawTargetDifference = Enumerable.Range(0, columns.Length * 3).Select(i => frames.Where(x => x.MotionMs[i % columns.Length] > 105 && x.MotionMs[i % columns.Length] < 1200).Select(x => Math.Abs(x.Mean[i] - baseline[i])).DefaultIfEmpty(999).Max()).ToArray();
            var difference = Enumerable.Range(0, columns.Length * 3).Select(i => frames.Where(x => x.Ms >= appearedMs[i] && x.MotionMs[i % columns.Length] < 1200).Select(x => Math.Abs(x.Mean[i] - baseline[i])).DefaultIfEmpty(999).Max()).ToArray();
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "render-cycle-" + cycle + ".json"), JsonSerializer.Serialize(new { baseline, difference, rawTargetDifference, completedMs, appearedMs, frames = frames.Select(x => new { x.Ms, x.MotionMs, x.Opening, x.Mean }) }));
            for (var i = 0; i < columns.Length * 3; i++)
            {
                var column = columns[i % columns.Length];
                var clippedFrame = frames.Any(x => x.MotionMs[i % columns.Length] > 0 && x.MotionMs[i % columns.Length] < 70 && x.Mean[i % columns.Length] > 90);
                if ((i < columns.Length && (baseline[i] >= 90 || !clippedFrame)) || difference[i] > 2 || column.PersistentBackdropMotionCount == 0 || column.RestingHandoffCount != 0 || column.LastAnimationError is not null)
                    throw new InvalidOperationException("第" + cycle + "轮第" + i + "列展开后像素或永久背景检查失败：" + difference[i]);
                passed++;
            }
            all.Add(new { cycle, baseline, difference, rawTargetDifference, completedMs, appearedMs, frames = frames.Select(x => new { x.Ms, x.MotionMs, x.Opening, x.Mean }) });
            var bounds = Native.ScreenBounds(backing);
            using (var image = new Drawing.Bitmap((int)bounds.Width, (int)bounds.Height))
            { using var g = Drawing.Graphics.FromImage(image); g.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0, image.Size); image.Save(Path.Combine(controller.Store.DirectoryPath, "column-cycle-" + cycle + ".png")); }
            controller.SetExpanded(false); await Task.Delay(250);
        }
        backing.Destroy(false);
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "render-report.json"), JsonSerializer.Serialize(new { passed, cycles = all, mouseMoved = false, keyboardInjected = false,
            boundary = "3轮六列背景/卡片/标题屏幕像素，未移动鼠标。未声称覆盖所有显卡负载或用户真实数据。" }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static double[] SampleRegions(int[] backgroundX, int[] headingX, int[] rows, int width, string? path)
    {
        var left = Math.Min(backgroundX.Min(), headingX.Min()); var top = rows.Min();
        using var image = new Drawing.Bitmap(Math.Max(backgroundX.Max(), headingX.Max()) - left + width, rows.Max() - top + 1);
        using (var graphics = Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen(left, top, 0, 0, image.Size);
        if (path is not null) image.Save(path);
        return rows.SelectMany((y, region) => (region == 2 ? headingX : backgroundX).Select(x =>
        {
            var sum = 0.0; for (var i = 0; i < width; i++) { var color = image.GetPixel(x - left + i, y - top); sum += (color.R + color.G + color.B) / 3.0; }
            return sum / width;
        })).ToArray();
    }
}
