using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

// Real desktop pixels on an owned solid background; not timer-count evidence.
internal static class DraftDealChecks
{
    internal static async Task Run(BoardController controller, SurfaceWindow background)
    {
        controller.SetExpanded(true); await Task.Delay(250);
        var first = controller.Editors.First(x => x.Session.TaskId is null);
        var second = controller.NewTask(Lane.Running); await Task.Delay(250);
        var stack = (DraftStackSurface)second.Host!;
        foreach (var window in Application.Current.Windows.OfType<SurfaceWindow>().Where(x => x != stack && x != background).ToArray()) window.Hide();
        Native.Raise(background); Native.Raise(stack); await Task.Delay(180);
        var original = Native.ScreenBounds(stack); var handle = new WindowInteropHelper(stack).Handle;
        var scale = Native.GetDpiForWindow(handle) / 96.0; var pane = first.Width * scale;
        var padding = DraftSwitchMotion.Padding(first.Width) * scale;
        var left = (int)Math.Round(original.Left - padding); var top = (int)Math.Round(original.Top - DraftSwitchMotion.Lift * scale);
        var width = (int)Math.Ceiling(original.Width + padding * 2); var height = (int)Math.Ceiling(original.Height + DraftSwitchMotion.Lift * scale);
        var output = Path.Combine(controller.Store.DirectoryPath, "deal-artifacts"); Directory.CreateDirectory(output);
        var frames = new List<object>(); var separated = false; var lifted = false;
        var nativeHides = stack.NativeHideCount;
        using var ready = new System.Threading.ManualResetEventSlim();
        var monitor = Task.Run(() =>
        {
            using var row = new Drawing.Bitmap(width, 3); using var graphics = Drawing.Graphics.FromImage(row);
            var watch = Stopwatch.StartNew(); ready.Set(); var lastCapture = -100d;
            while (watch.Elapsed.TotalMilliseconds < 800)
            {
                graphics.CopyFromScreen(left, (int)(original.Top + 240 * scale), 0, 0, row.Size);
                var spans = new List<int>(); var run = 0; var white = 0;
                for (var x = 0; x < width; x += 3)
                {
                    var pixel = row.GetPixel(x, 1);
                    var dark = Math.Abs(pixel.R - 72) + Math.Abs(pixel.G - 122) + Math.Abs(pixel.B - 221) > 30;
                    if (pixel.R > 245 && pixel.G > 245 && pixel.B > 245) white++;
                    if (dark) run += 3; else if (run > 0) { spans.Add(run); run = 0; }
                }
                if (run > 0) spans.Add(run);
                separated |= spans.Count(x => x >= pane * .85) >= 2;
                using var probe = new Drawing.Bitmap(1, 1);
                using (var painter = Drawing.Graphics.FromImage(probe)) painter.CopyFromScreen((int)(original.Left + pane + 6 * scale), (int)(original.Top - 6 * scale), 0, 0, probe.Size);
                var liftPixel = probe.GetPixel(0, 0);
                lifted |= Math.Abs(liftPixel.R - 72) + Math.Abs(liftPixel.G - 122) + Math.Abs(liftPixel.B - 221) > 30;
                var elapsed = watch.Elapsed.TotalMilliseconds;
                frames.Add(new { elapsedMs = elapsed, darkSpans = spans, whitePixels = white });
                if (elapsed - lastCapture >= 35 && elapsed < 600)
                {
                    using var image = new Drawing.Bitmap(width, height);
                    using (var painter = Drawing.Graphics.FromImage(image)) painter.CopyFromScreen(left, top, 0, 0, image.Size);
                    image.Save(Path.Combine(output, $"deal-{frames.Count:D2}.png"), Drawing.Imaging.ImageFormat.Png); lastCapture = elapsed;
                }
                System.Threading.Thread.Sleep(5);
            }
        });
        ready.Wait(); first.Activate(); await monitor; await Task.Delay(100);
        var end = Native.ScreenBounds(stack);
        var sameBounds = Math.Abs(end.Left - original.Left) < 2 && Math.Abs(end.Top - original.Top) < 2 && Math.Abs(end.Width - original.Width) < 2 && Math.Abs(end.Height - original.Height) < 2;
        var passed = separated && lifted && sameBounds && !stack.IsContentSwapping && stack.NativeHideCount == nativeHides && new WindowInteropHelper(stack).Handle == handle;
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "deal-report.json"), JsonSerializer.Serialize(new { passed, durationTargetMs = DraftSwitchMotion.DurationMs, realDesktopSamples = frames.Count, separated, lifted, sameBounds,
            stack.LastContentSwapPreparationMs, sameNativeWindow = new WindowInteropHelper(stack).Handle == handle, unexpectedNativeHides = stack.NativeHideCount - nativeHides, frames }, new JsonSerializerOptions { WriteIndented = true }));
        controller.DeleteDraft(second);
        if (!passed) throw new InvalidOperationException("扑克牌实机像素验收未通过，请查看deal-report.json和deal-artifacts。");
    }
}
