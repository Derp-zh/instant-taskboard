using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class FloatingInteractionChecks
{
    internal static async Task Run(BoardController controller)
    {
        var checks = new List<string>(); var directory = controller.Store.DirectoryPath;
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        controller.SuppressCollapse = true; controller.SetExpanded(true);
        var first = controller.Engine.NewSession(Lane.Action); first.Working.Title = "双击验证任务A"; controller.Engine.Submit(first);
        var second = controller.Engine.NewSession(Lane.Action); second.Working.Title = "双击验证任务B"; controller.Engine.Submit(second);
        var draft = controller.NewTask(Lane.Running); draft.Session.Working.Title = "双击卡片仍保留此悬浮窗口";
        var area = controller.WorkArea(controller.Ball); var column = controller.Columns.First(x => x.Lane == Lane.Action);
        foreach (var other in controller.Columns.Where(x => x != column)) other.Hide();
        column.Left = area.Left + 12; column.Top = area.Top + 32; column.Width = 260; column.Height = Math.Min(650, area.Height - 64);
        await Task.Delay(600);
        var cardA = column.Locate(first.Working.Id)!; var cardB = column.Locate(second.Working.Id)!;
        var a = cardA.PointToScreen(new Point(24, cardA.ActualHeight / 2)); var b = cardB.PointToScreen(new Point(24, cardB.ActualHeight / 2));
        var blank = column.Root.PointToScreen(new Point(24, column.Height - 60));
        var header = column.Root.PointToScreen(new Point(24, 18));
        Assert(column.IsTaskCardAt(a) && column.IsTaskCardAt(b) && !column.IsOutsideDismissArea(a) && !column.IsOutsideDismissArea(b) && column.IsOutsideDismissArea(blank) && column.IsOutsideDismissArea(header), "命中检测区分真实任务卡片和列表空白/标题");
        var initialHides = draft.Host!.NativeHideCount;
        WriteReady("floating-ready.json"); await Wait("card-a.signal"); await Task.Delay(500);
        var editA = controller.Editors.Single(x => x.Session.TaskId == first.Working.Id);
        Assert(draft.Host.IsVisible && draft.Host.NativeHideCount == initialHides && editA.Host!.IsVisible, "双击任务A只打开一个编辑窗口，不关闭原新建悬浮窗口");
        WriteReady("floating-b-ready.json"); await Wait("card-b.signal"); await Task.Delay(500);
        var editB = controller.Editors.Single(x => x.Session.TaskId == second.Working.Id);
        Assert(editA.Host!.IsVisible && editB.Host!.IsVisible && draft.Host.IsVisible, "双击另一任务保留旧编辑与新建窗口");
        var deltaX = editB.Host!.Left - editA.Host.Left; var deltaY = editB.Host.Top - editA.Host.Top;
        Assert(Math.Abs(deltaX - 32) < .1 && Math.Abs(deltaY - 32) < .1, "新旧编辑窗口按用户选择横向32、纵向32错位");
        WriteReady("floating-blank-ready.json"); await Wait("blank.signal"); await Task.Delay(600);
        Assert(!editB.Session.Visible && editA.Session.Visible && draft.Host.IsVisible, "双击列表空白只关闭最上层编辑窗口，不衔接打开任务");
        WriteReady("floating-header-ready.json"); await Wait("header.signal"); await Task.Delay(600);
        Assert(!editA.Session.Visible && draft.Host.IsVisible, "双击列表标题继续只关闭当前最上层悬浮窗口");
        File.WriteAllText(Path.Combine(directory, "floating-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, deltaX, deltaY, isolated = true }, new JsonSerializerOptions { WriteIndented = true }));
        void WriteReady(string file) => File.WriteAllText(Path.Combine(directory, file), JsonSerializer.Serialize(new {
            columnHandle = new WindowInteropHelper(column).Handle.ToInt64(),
            cardA = new { x = a.X, y = a.Y }, cardB = new { x = b.X, y = b.Y },
            blank = new { x = blank.X, y = blank.Y }, header = new { x = header.X, y = header.Y } }));
        async Task Wait(string file)
        {
            for (var i = 0; i < 400; i++) { if (File.Exists(Path.Combine(directory, file))) return; await Task.Delay(50); }
            throw new TimeoutException("隔离鼠标验证信号超时：" + file);
        }
    }
}
