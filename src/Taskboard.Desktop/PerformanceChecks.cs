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
        var independent = controller.Columns.Where(x=>x.Definition.Visible).All(x=>x.UsesIndependentAnimation) && controller.Ball.UsesIndependentAnimation;
        // A17 specifies 200ms for operation feedback; 8.33ms is the animation
        // frame budget, and is supplied by independent system composition.
        var report = new { passed=independent && searches.Max()<8.333 && feedback.Max()<200,
            displayRefreshHz=after.Hz,desktopComposedFrames=after.Frame-before.Frame,desktopRefreshes=after.Refresh-before.Refresh,intervalMs=timingInterval,
            compositorAnimations=independent,frameBudgetMs=1000.0/120,operationFeedbackBudgetMs=200,animationDurationMs=100,
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
        ball.Show(); Native.RaiseBall(); await Task.Delay(100);
        var origin=ball.PointToScreen(new Point(ball.Width,ball.Height/2));
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
            await started.Task; var begin=samplingClock.Elapsed.TotalMilliseconds; ball.SetExpanded(true);
            // The UI dispatcher is deliberately busy; pixels must still advance.
            await Task.Delay(25); var blockBegin=samplingClock.Elapsed.TotalMilliseconds; Thread.Sleep(55); var blockEnd=samplingClock.Elapsed.TotalMilliseconds;
            await sampling; await Task.Delay(180);
            var during=frames.Where(x=>x.Ms>=blockBegin && x.Ms<=blockEnd).Select(x=>x.Width).Distinct().ToArray();
            var intermediate=frames.Where(x=>x.Width>68*scale && x.Width<360*scale).ToArray();
            var report=new { passed=intermediate.Length>0 && during.Length>=2,refreshHz=FrameTiming.Read().Hz,beginMs=begin,blockBeginMs=blockBegin,blockEndMs=blockEnd,
                sampleX=startX,sampleY=mid,sampleWidth=width,ballBounds=Native.ScreenBounds(ball).ToString(),ballVisible=ball.IsVisible,independent=ball.UsesIndependentAnimation,
                distinctIntermediatePixelWidths=intermediate.Select(x=>x.Width).Distinct().Count(),pixelWidthsWhileUiBusy=during,frames,measure="Actual screen pixels sampled on another thread; no timer ticks counted as rendered frames." };
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath,"animation-pixels.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
            if(!report.passed) throw new InvalidOperationException("合成动画没有持续绘制或有瞬间跳变"); return report;
        }
        finally { background.Destroy(); ball.SetExpanded(false); await Task.Delay(180); ball.Place(previous); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
