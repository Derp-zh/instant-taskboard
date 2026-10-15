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
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

// Coordinated desktop evidence only, always launched with an isolated --data-dir.
internal static class SharedMotionChecks
{
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    internal static async Task Run(BoardController controller)
    {
        controller.SuppressCollapse = true;
        await PerformanceChecks.SampleBallAnimation(controller);
        var area = controller.WorkArea(controller.Ball);
        var background = new SurfaceWindow(area.Width, area.Height) { GlassEnabled = false, WindowLayer = -100, Left = area.Left, Top = area.Top };
        background.Root.Background = Ui.Brush("#487ADD"); background.Show(); Native.Raise(background); Native.RaiseBall();
        try
        {
            var older = controller.NewTask(Lane.Action); await Task.Delay(180); FirstInput(older).Text = "待操作 · 共享草稿一";
            var front = controller.NewTask(Lane.Running); await Task.Delay(180); FirstInput(front).Text = "进行中 · 共享草稿二";
            var host = (DraftStackSurface)front.Host!;
            await Task.Delay(400);
            var original = Native.ScreenBounds(host); var hideCount = host.NativeHideCount;
            var scale = original.Height / host.Height;
            controller.SetFloatingTransparency(50); await Task.Delay(200);
            int transparencyDifference; bool upperGapFilled, lowerGapFilled;
            using (var still = new Drawing.Bitmap((int)original.Width, (int)original.Height))
            {
                using var paint = Drawing.Graphics.FromImage(still);
                var dpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                try { paint.CopyFromScreen((int)original.Left, (int)original.Top, 0, 0, still.Size); }
                finally { SetThreadDpiAwarenessContext(dpi); }
                var pane = (int)Math.Round(front.Width * scale); var y = (int)(200 * scale);
                transparencyDifference = Difference(still.GetPixel((int)(8 * scale), y).ToArgb(), still.GetPixel(pane - (int)(8 * scale), y).ToArgb());
                var blue = Drawing.Color.FromArgb(72, 122, 221).ToArgb();
                upperGapFilled = Difference(still.GetPixel(pane - 2, 4).ToArgb(), blue) > 35;
                lowerGapFilled = Difference(still.GetPixel(pane - 2, still.Height - 5).ToArgb(), blue) > 35;
                still.Save(Path.Combine(controller.Store.DirectoryPath, "native-stack-50pct.png"));
            }
            if (transparencyDifference >= 15 || !upperGapFilled || !lowerGapFilled) throw new InvalidOperationException($"透明堆叠真实画面补口验证失败：差值{transparencyDifference}，上{upperGapFilled}，下{lowerGapFilled}");
            controller.SetFloatingTransparency(10); await Task.Delay(200);
            var width = Math.Min((int)original.Width, (int)Math.Round(front.Width * 1.2 * scale)); var height = (int)original.Height;
            var points = new[] { new Drawing.Point(12, 100), new Drawing.Point((int)(front.Width * scale) + 4, 100), new Drawing.Point((int)(front.Width * scale) - 2, 4) };
            var samples = new List<(double Ms, int[] Colors)>(); var ready = new TaskCompletionSource(); var clockStart = Stopwatch.GetTimestamp(); var clock = Stopwatch.StartNew();
            var frames = Path.Combine(controller.Store.DirectoryPath, "shared-frames"); Directory.CreateDirectory(frames);
            var sampling = Task.Run(() =>
            {
                var dpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                try
                {
                    using var bitmap = new Drawing.Bitmap(width, height); using var paint = Drawing.Graphics.FromImage(bitmap);
                    var nextImage = 0.0; var image = 0;
                    ready.SetResult();
                    while (clock.Elapsed.TotalMilliseconds < 1000)
                    {
                        paint.CopyFromScreen((int)original.Left, (int)original.Top, 0, 0, bitmap.Size);
                        var ms = clock.Elapsed.TotalMilliseconds;
                        samples.Add((ms, points.Select(p => bitmap.GetPixel(p.X, p.Y).ToArgb()).ToArray()));
                        if (ms >= nextImage) { bitmap.Save(Path.Combine(frames, $"frame-{image++:00}.png")); nextImage += 75; }
                        Thread.Sleep(3);
                    }
                }
                finally { SetThreadDpiAwarenessContext(dpi); }
            });
            await ready.Task; var begin = clock.Elapsed.TotalMilliseconds; older.Activate();
            // Ensure the compositor carries the movement while the UI thread is busy.
            await Task.Delay(35); Thread.Sleep(45); await sampling; await Task.Delay(200);
            var restored = Native.ScreenBounds(host); var settled = samples[^1].Colors;
            var finishMs = (host.ContentSwapFinishTicks - clockStart) * 1000.0 / Stopwatch.Frequency;
            var late = samples.Where(x => x.Ms >= finishMs).ToArray();
            var maxDifference = late.SelectMany(x => x.Colors.Zip(settled).Select(pair => Difference(pair.First, pair.Second))).DefaultIfEmpty().Max();
            var baseline = samples[0].Colors;
            var preparationDifference = samples.Where(x => x.Ms < begin + host.LastContentSwapPreparationMs - 10).SelectMany(x => x.Colors.Zip(baseline).Select(pair => Difference(pair.First, pair.Second))).DefaultIfEmpty().Max();
            var passed = !host.IsContentSwapping && controller.ActiveDraft == older.Session.Id && original == restored && host.NativeHideCount == hideCount && late.Length > 5 && maxDifference < 35 && preparationDifference < 35;
            var report = new { passed, transparencyDifference, upperGapFilled, lowerGapFilled, original = original.ToString(), restored = restored.ToString(), durationMs = DraftSwitchMotion.DurationMs,
                host.LastContentSwapPreparationMs, preparationDifference, finishMs, sampledFrames = samples.Count, lateFrames = late.Length, finalHandoffMaxColorDifference = maxDifference,
                host.UsesIndependentAnimation, mouseMoved = false, desktopShown = true, isolated = true,
                samples = samples.Select(x => new { x.Ms, x.Colors }), screenshots = frames };
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "shared-motion-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new InvalidOperationException("共享抽牌实机采样未通过：" + JsonSerializer.Serialize(report));
            controller.CloseDraftWindow(); await Task.Delay(180); controller.OpenNewEntry(Lane.Done); await Task.Delay(180);
            if (controller.Editors.Count(x => x.Session.TaskId is null) != 2) throw new InvalidOperationException("跨列表重开未恢复两份共享草稿");
        }
        finally { background.Destroy(); }
    }
    private static System.Windows.Controls.TextBox FirstInput(DependencyObject element)
    {
        if (element is System.Windows.Controls.TextBox input) return input;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
        { try { return FirstInput(System.Windows.Media.VisualTreeHelper.GetChild(element, i)); } catch (InvalidOperationException) { } }
        throw new InvalidOperationException("没有输入框");
    }
    private static int Difference(int first, int second)
    {
        var a = Drawing.Color.FromArgb(first); var b = Drawing.Color.FromArgb(second);
        return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }
}
