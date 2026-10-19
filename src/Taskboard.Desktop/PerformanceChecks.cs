using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Drawing = System.Drawing;
namespace Taskboard.Desktop;
internal static class PerformanceChecks
{
    internal static async Task Run(BoardController controller)
    {
        var before = FrameTiming.Read(); var clock=Stopwatch.StartNew(); await Task.Delay(1000); var after=FrameTiming.Read(); var timingInterval=clock.Elapsed.TotalMilliseconds;
        var searches = new List<double>();
        for(var i=0;i<100;i++) { var watch=Stopwatch.StartNew(); _=controller.Engine.Search("记录").ToArray(); searches.Add(watch.Elapsed.TotalMilliseconds); }
        var feedback = new List<double>();
        var details=new List<object>();
        for(var i=0;i<6;i++) { var watch=Stopwatch.StartNew(); controller.SetExpanded(!controller.Expanded); feedback.Add(watch.Elapsed.TotalMilliseconds); details.Add(new{controller.Expanded,controller.LastWorkspaceRevealMs,controller.LastBallRevealMs,controller.LastEditorRestoreMs,columns=controller.VisibleColumns.Select(x=>new{x.LastAnimationPreparationMs,x.LastSnapshotMs,x.LastShowMs}).ToArray(),editors=controller.Editors.Count}); await Task.Delay(180); }
        var independent = controller.Ball.UsesIndependentAnimation;
        var liveLists = controller.VisibleColumns.All(x=>x.LiveListRevealCount>0 && !x.LiveContentHidden && x.RestingHandoffCount==0);
        var liveBall = controller.Ball.LiveBallRevealCount > 0 && !controller.Ball.LiveContentHidden && controller.Ball.RestingHandoffCount == 0;
        var report = new { passed=!independent && liveBall && liveLists && searches.Max()<8.333 && feedback.Max()<200,
            displayRefreshHz=after.Hz,desktopComposedFrames=after.Frame-before.Frame,desktopRefreshes=after.Refresh-before.Refresh,intervalMs=timingInterval,
            compositorAnimations=independent,liveBallAnimation=liveBall,liveListClipping=liveLists,frameBudgetMs=1000.0/120,operationFeedbackBudgetMs=200,animationDurationMs=100,
            searchMaximumMs=searches.Max(),searchP95Ms=searches.Order().ElementAt(94),toggleSynchronousFeedbackMs=feedback,
            feedbackDetails=details,
            columnsPreparationMs=controller.VisibleColumns.Select(x=>new { x.Title,x.LastAnimationPreparationMs }),ballPreparationMs=controller.Ball.LastAnimationPreparationMs,
            note="DWM counters describe the desktop, not individual app frames. Pixel animation evidence is recorded separately. Actual displayed frames are limited by monitor refresh." };
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath,"performance-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        if(!report.passed) throw new InvalidOperationException("动画或即时响应预算未通过："+JsonSerializer.Serialize(report));
    }
    internal sealed record PixelFrame(double Ms,int Width);
    internal static async Task<object> SampleBallAnimation(BoardController controller)
    {
        var ball=controller.Ball; var area=controller.WorkArea(ball); var previous=ball.Position();
        controller.SetExpanded(false); await Task.Delay(180);
        var background=new SurfaceWindow(area.Width,area.Height) {GlassEnabled=false,WindowLayer=-100,Left=area.Left,Top=area.Top}; background.Root.Background=Ui.Brush("#487ADD"); background.Show();
        ball.Left=area.Left+area.Width*.75; ball.Top=area.Bottom-ball.Height-100; ball.SetExpanded(true); await Task.Delay(180); ball.SetExpanded(false); await Task.Delay(180);
        // Place the isolated sampling background above other topmost apps;
        // internal ordering alone ignores foreign windows between our handles.
        ball.Show(); Native.Raise(background); Native.Raise(ball); Native.RaiseBall(); await Task.Delay(100);
        for (var wait = 0; ball.IsRevealAnimating && wait < 200; wait++) await Task.Delay(3);
        if (ball.IsEdgeDocked) ball.DockAtRight();
        await Task.Delay(35);
        var bounds = Native.ScreenBounds(ball); var origin = new Point(bounds.Right, bounds.Top + bounds.Height / 2);
        var initialBounds = Native.ScreenBounds(ball).ToString(); var initialPosition = ball.Position();
        var right=origin.X; var mid=(int)origin.Y; var scale=Native.ScreenBounds(ball).Width/58;
        var width=(int)Math.Round(370*scale); var startX=(int)right-width;
        var frames=new List<PixelFrame>(); var started=new TaskCompletionSource(); var samplingClock=Stopwatch.StartNew();
        var sampling=Task.Run(() =>
        {
            var previousDpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
            using var line=new Drawing.Bitmap(width,1); using var graphics=Drawing.Graphics.FromImage(line);
            started.SetResult();
            while(samplingClock.Elapsed.TotalMilliseconds<240)
            {
                graphics.CopyFromScreen(startX,mid,0,0,line.Size);
                var first=width;
                for(var x=0;x<width;x++) { var p=line.GetPixel(x,0); if(Math.Abs(p.R-72)+Math.Abs(p.G-122)+Math.Abs(p.B-221)>35) {first=x;break;} }
                frames.Add(new PixelFrame(samplingClock.Elapsed.TotalMilliseconds,width-first)); Thread.Sleep(1);
            }
            line.Save(Path.Combine(controller.Store.DirectoryPath,"sampling-line.png")); SetThreadDpiAwarenessContext(previousDpi);
        });
        try
        {
            await started.Task; var begin=samplingClock.Elapsed.TotalMilliseconds; var previousStart = ball.MotionDisplayStartTicks; ball.SetExpanded(true);
            for (var wait = 0; ball.MotionDisplayStartTicks == previousStart && wait < 100; wait++) await Task.Delay(2);
            // The user chose real controls for the ball too. A busy UI can
            // pause frames; no screenshot/live handoff may occur afterward.
            await Task.Delay(15); var blockBegin=samplingClock.Elapsed.TotalMilliseconds; Thread.Sleep(55); var blockEnd=samplingClock.Elapsed.TotalMilliseconds;
            await sampling; await Task.Delay(180);
            var during=frames.Where(x=>x.Ms>=blockBegin && x.Ms<=blockEnd).Select(x=>x.Width).Distinct().ToArray();
            var intermediate=frames.Where(x=>x.Width>68*scale && x.Width<360*scale).ToArray();
            var monotonic = frames.Zip(frames.Skip(1)).All(x => x.Second.Width >= x.First.Width - 8);
            var expandedBounds = Native.ScreenBounds(ball);
            using (var shot = new Drawing.Bitmap((int)expandedBounds.Width, (int)expandedBounds.Height))
            {
                using (var paint = Drawing.Graphics.FromImage(shot)) paint.CopyFromScreen((int)expandedBounds.Left, (int)expandedBounds.Top, 0, 0, shot.Size);
                shot.Save(Path.Combine(controller.Store.DirectoryPath, "ball-expanded-screen.png"));
            }
            var closingFrames = new List<PixelFrame>(); var closeReady = new TaskCompletionSource(); var closeClock = Stopwatch.StartNew();
            var closeSampling = Task.Run(() =>
            {
                var dpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                try
                {
                    using var row = new Drawing.Bitmap(width, 1); using var paint = Drawing.Graphics.FromImage(row);
                    closeReady.SetResult();
                    while (closeClock.ElapsedMilliseconds < 260)
                    {
                        paint.CopyFromScreen(startX, mid, 0, 0, row.Size);
                        var first = width;
                        for (var x = 0; x < width; x++) { var c = row.GetPixel(x, 0); if (Math.Abs(c.R - 72) + Math.Abs(c.G - 122) + Math.Abs(c.B - 221) > 35) { first = x; break; } }
                        closingFrames.Add(new PixelFrame(closeClock.Elapsed.TotalMilliseconds, width - first)); Thread.Sleep(2);
                    }
                }
                finally { SetThreadDpiAwarenessContext(dpi); }
            });
            await closeReady.Task; ball.SetExpanded(false); await closeSampling; await Task.Delay(100);
            var closingMonotonic = closingFrames.Zip(closingFrames.Skip(1)).All(x => x.Second.Width <= x.First.Width + 8);
            var closingIntermediate = closingFrames.Any(x => x.Width > 68 * scale && x.Width < 360 * scale);
            // A deliberate 55ms UI stall can consume all useful middle frames.
            // Retain that entire probe, and verify ordinary opening separately.
            var openingFrames = new List<PixelFrame>(); var openReady = new TaskCompletionSource(); var openClock = Stopwatch.StartNew();
            var openSampling = Task.Run(() =>
            {
                var dpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                try
                {
                    using var row = new Drawing.Bitmap(width, 1); using var paint = Drawing.Graphics.FromImage(row);
                    openReady.SetResult();
                    while (openClock.ElapsedMilliseconds < 260)
                    {
                        paint.CopyFromScreen(startX, mid, 0, 0, row.Size);
                        var first = width;
                        for (var x = 0; x < width; x++) { var c = row.GetPixel(x, 0); if (Math.Abs(c.R - 72) + Math.Abs(c.G - 122) + Math.Abs(c.B - 221) > 35) { first = x; break; } }
                        openingFrames.Add(new PixelFrame(openClock.Elapsed.TotalMilliseconds, width - first)); Thread.Sleep(2);
                    }
                }
                finally { SetThreadDpiAwarenessContext(dpi); }
            });
            await openReady.Task; ball.SetExpanded(true); await openSampling; await Task.Delay(100);
            var openingMonotonic = openingFrames.Zip(openingFrames.Skip(1)).All(x => x.Second.Width >= x.First.Width - 8);
            var openingIntermediate = openingFrames.Any(x => x.Width > 68 * scale && x.Width < 360 * scale);
            var report=new { passed=openingIntermediate && openingMonotonic && monotonic && closingMonotonic && closingIntermediate && !ball.UsesIndependentAnimation && !ball.LiveContentHidden && ball.RestingHandoffCount == 0,
                monotonic,closingMonotonic,closingIntermediate,openingMonotonic,openingIntermediate,refreshHz=FrameTiming.Read().Hz,beginMs=begin,blockBeginMs=blockBegin,blockEndMs=blockEnd,
                sampleX=startX,sampleY=mid,sampleWidth=width,initialBounds,initialPosition,finalPosition=ball.Position(),expandedBounds=expandedBounds.ToString(),ballBounds=Native.ScreenBounds(ball).ToString(),contentBounds=ball.LiveContentBounds.ToString(),rootWidth=ball.Root.ActualWidth,ballVisible=ball.IsVisible,independent=ball.UsesIndependentAnimation,
                distinctIntermediatePixelWidths=intermediate.Select(x=>x.Width).Distinct().Count(),pixelWidthsWhileUiBusy=during,frames,closingFrames,openingFrames,measure="Actual screen pixels on another thread. Busy opening may skip middle frames; separate unblocked opening and closing require intermediate frames. All three must be monotonic without a snapshot/live handoff." };
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath,"animation-pixels.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
            if(!report.passed) throw new InvalidOperationException("球真实开合采样未通过：无中间帧、回退或内容交接"); return report;
        }
        finally { background.Destroy(); ball.SetExpanded(false); await Task.Delay(180); ball.Place(previous); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
