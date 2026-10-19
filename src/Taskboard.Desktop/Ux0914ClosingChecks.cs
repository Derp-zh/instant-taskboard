using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

internal static class Ux0914ClosingChecks
{
    internal static async Task Run(BoardController controller)
    {
        controller.SuppressCollapse = true; controller.SetExpanded(true); await Task.Delay(350);
        var root = controller.Store.DirectoryPath; var checks = new List<string>(); var measurements = new List<object>();
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        var area = controller.WorkArea(controller.Ball);
        var backing = new SurfaceWindow(480, 700) { GlassEnabled = false, FloatingAnimations = false, SuppressActivation = true, WindowLayer = -1, Left = area.Left + 35, Top = area.Top + 20 };
        backing.Root.Background = Brushes.DimGray; backing.Show();
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "关闭前亮度采样";
        session.Working.Stages[0].Text = "独立线程采样保存和关闭的交界"; var item = controller.Engine.Submit(session);
        for (var variant = 0; variant < 2; variant++)
        {
            var editor = controller.EditTask(item.Id); var host = editor.Host!;
            host.Left = backing.Left + 20; host.Top = backing.Top + 15; await Ux0914Checks.Settled(host); await Task.Delay(180);
            if (variant == 1) { editor.StagePickers[session.Working.Stages[0].Id].Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.UpdateLayout(); }
            Ux0914Checks.Children<TextBox>(editor).First().Text = "修改后双击关闭" + variant;
            controller.ObserveOutsidePress(host, true); controller.ObserveOutsidePress(host, false, new Point(40, 40), 1000); await Task.Delay(100);
            var save = Ux0914Checks.Children<Button>(editor).Single(x => Equals(x.Tag, "SaveTask"));
            var point = save.PointToScreen(new Point(save.ActualWidth - 7, save.ActualHeight / 2));
            var baseline = Sample(point); var previousTicks = host.MotionDisplayStartTicks;
            var enabledChanges = 0; editor.Root.IsEnabledChanged += (_, _) => enabledChanges++;
            var frames = new List<(double Ms, double MotionMs, double Mean)>(); var clock = Stopwatch.StartNew();
            var recording = Task.Run(async () =>
            {
                while (clock.ElapsedMilliseconds < 650)
                {
                    var tick = host.MotionDisplayStartTicks;
                    var motionMs = tick == previousTicks ? -1 : (Stopwatch.GetTimestamp() - tick) * 1000.0 / Stopwatch.Frequency;
                    frames.Add((clock.Elapsed.TotalMilliseconds, motionMs, Sample(point))); await Task.Delay(3);
                }
            });
            var gate = (SemaphoreSlim)typeof(BoardController).GetField("saveGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            await gate.WaitAsync();
            try
            {
                await Task.Delay(60); controller.ObserveOutsidePress(host, false, new Point(40, 40), 1100); await Task.Delay(140);
                Check(editor.Root.IsEnabled && !editor.Root.IsHitTestVisible && enabledChanges == 0, "编辑" + variant + "真实可见保存等待期间不禁用、不变暗");
            }
            finally { gate.Release(); }
            await recording;
            var before = frames.Where(x => x.Ms > 70 && x.MotionMs < 0).ToArray();
            var max = before.Select(x => Math.Abs(x.Mean - baseline)).DefaultIfEmpty(999).Max();
            Check(before.Length > 10 && max < 2, "编辑" + variant + "第二次外点至关闭开始前实屏亮度无跳变");
            Check(!controller.Editors.Contains(editor) && enabledChanges == 0 && controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Title == "修改后双击关闭" + variant, "编辑" + variant + "修改保存后正常关闭，全程未触发禁用主题");
            measurements.Add(new { variant, associationInitiallyOpen = variant == 1, baseline, max, frames = frames.Select(f => new { f.Ms, f.MotionMs, f.Mean }) });
        }
        backing.Destroy(false);
        File.WriteAllText(Path.Combine(root, "ux0914-closing-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, measurements, independentScreenSampler = true, mouseMoved = false, keyboardInjected = false, productionDataChanged = false, boundary = "检查第二外点保存至关闭开始前的亮度；关闭中央白条及裁切仍是既定动画，未声称所有负载均无帧问题" }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static double Sample(Point point)
    {
        using var image = new Drawing.Bitmap(3, 3); using (var g = Drawing.Graphics.FromImage(image)) g.CopyFromScreen((int)point.X, (int)point.Y, 0, 0, image.Size);
        var sum = 0.0; for (var y = 0; y < 3; y++) for (var x = 0; x < 3; x++) { var c = image.GetPixel(x, y); sum += (c.R + c.G + c.B) / 3.0; }
        return sum / 9;
    }
}
