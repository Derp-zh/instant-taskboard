using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class WorkspaceChecks
{
    internal static async Task Run(BoardController controller, bool geometryOnly = false)
    {
        var checks = new List<string>();
        void Check(bool value, string description) { if (!value) throw new InvalidOperationException(description); checks.Add(description); }
        double Difference(WindowPosition a, WindowPosition b) => new[] { Math.Abs(a.X-b.X), Math.Abs(a.Y-b.Y), Math.Abs(a.Width-b.Width), Math.Abs(a.Height-b.Height) }.Max();
        var gate = new MousePressGate();
        Check(gate.Accept(0, 100, 1, 0) && !gate.Accept(0, 100, 2, 0), "同一物理点击的钩子和原始输入只处理一次，包括代次0");
        Check(gate.Accept(0, 108, 1, 0) && !gate.Accept(0, 108, 2, 0), "两个快速点击分别接受，各自跨来源去重");
        Check(gate.Accept(0, 110, 2, 1) && !gate.Accept(0, 111, 1, 1), "原始输入先到也去重，展开代次变化不丢点击");
        Check(gate.Accept(1, 113, 2, 1) && gate.Accept(1, 114, 2, 1), "不同按键和同一来源的连续点击不合并");
        Check(gate.Accept(0, 200, 1, 1) && !gate.Accept(0, 198, 2, 1), "同一点击的两个消息时间轻微倒序时仍去重，不误关窗口");
        var input = JsonSerializer.SerializeToElement(controller.OutsideInputStatus);
        Check(input.GetProperty("RawInput").GetBoolean() && input.GetProperty("RawError").GetInt32() == 0, "后台原始鼠标输入成功注册，无需游戏切换焦点");
        controller.SuppressCollapse = true;
        var columns = controller.VisibleColumns;
        var original = columns.Select(x => x.Position()).ToArray();
        Check(columns.All(x => x.Root.ContextMenu!.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "本窗口恢复到默认"))), "各列右键具有独立恢复菜单");
        var first = columns[0]; first.Left += 19; first.Top += 13; first.Width += 22; controller.SavePosition(first.Definition.PositionKey, first);
        controller.RestoreColumnDefault(first);
        if (Difference(first.Position(), original[0]) >= .01) File.WriteAllText(Path.Combine(controller.Store.DirectoryPath,"geometry-diagnostic.json"),JsonSerializer.Serialize(new { original=original[0],restored=first.Position(),baseline=controller.Engine.State.Preferences.Positions["layout-default:"+first.Definition.PositionKey],area=controller.WorkArea(controller.Ball) }));
        Check(Difference(first.Position(), original[0]) < .01, "单列恢复默认同时恢复大小和位置");
        Check(columns.Skip(1).Select((x,i) => Difference(x.Position(),original[i+1])).All(x => x < .01), "单列恢复不改变其他列表");
        controller.BeginWorkspaceResize(); var range = controller.MaximumRange;
        controller.ResizeWorkspace(.07 * (range.Width * range.Width + range.Height * range.Height) / (2 * range.Width), 0); controller.EndWorkspaceResize();
        var scaled = columns.Select(x => x.Position()).ToArray();
        Check(scaled[0].Width < original[0].Width && scaled[0].Height < original[0].Height, "最大范围缩放同时缩放窗口大小");
        first.Left += 20; first.Top += 12; first.Width += 18; controller.SavePosition(first.Definition.PositionKey, first);
        controller.RestoreColumnDefault(first);
        Check(Difference(first.Position(), scaled[0]) < 1, "单列恢复使用整组缩放后的默认尺寸及槽位");
        await controller.Flush(); var reloaded = controller.Store.Load();
        Check(reloaded.Preferences.Positions.Keys.Count(x => x.StartsWith("layout-default:", StringComparison.Ordinal)) >= columns.Length, "缩放后的默认槽位写入隔离配置并可重读");
        controller.RestoreDefaults();
        Check(columns.Select((x,i) => Difference(x.Position(),original[i])).All(x => x < .01), "整组恢复同时还原出厂默认大小和位置");
        if (geometryOnly)
        {
            var seed = controller.Engine.NewSession(Lane.Action); seed.Working.Title="不可见居中检查"; var task=controller.Engine.Submit(seed);
            var session=controller.Engine.Edit(task.Id);
            controller.Engine.State.Preferences.Positions["edit-"+session.Id]=new WindowPosition{X=12,Y=18,Width=400,Height=560};
            var editor=controller.EnsureEditor(session);
            Check(Centered(controller,editor.Host!), "编辑窗口的初始布局忽略旧坐标并居中，不显示窗口");
            var handles=Application.Current.Windows.OfType<SurfaceWindow>().Select(x=>new WindowInteropHelper(x).EnsureHandle()).ToArray();
            Check(handles.All(handle=>{var style=Native.GetWindowLongPtr(handle,-20).ToInt64();return (style&0x80)!=0&&(style&0x40000)==0;}), "已创建的球、列表、新建及编辑原生句柄均为非Alt+Tab工具窗口");
            Check(!Application.Current.Windows.OfType<SurfaceWindow>().Any(x => x.IsVisible), "布局及配置验证不显示窗口、不移动鼠标");
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath,"workspace-geometry-report.json"),JsonSerializer.Serialize(new { passed=checks.Count,checks,input,isolated=true,mouseMoved=false,shownWindows=0 },new JsonSerializerOptions{WriteIndented=true}));
            return;
        }
        var draft = controller.NewTask(Lane.Action); draft.Session.Working.Title = "居中验证草稿";
        await Settled(draft.Host!); var stack = draft.Host!;
        Check(Centered(controller, stack), "新建任务整个宿主默认居中");
        stack.Left -= 28; controller.EditorMoved(draft); var moved = stack.Position();
        controller.SetExpanded(false); await Hidden(stack); controller.SetExpanded(true); await Settled(stack);
        Check(draft.Host == stack && Difference(stack.Position(), moved) < 1, "整组收起恢复保留同一悬浮窗口、位置和输入");
        controller.CloseDraftWindow(); await Hidden(stack);
        draft = controller.OpenNewEntry(Lane.Action); await Settled(draft.Host!);
        Check(Centered(controller, draft.Host!), "手动关闭后重新打开草稿忽略旧位置并居中");
        WorkItem Task(string title) { var session=controller.Engine.NewSession(Lane.Action); session.Working.Title=title; return controller.Engine.Submit(session); }
        var a = Task("居中任务A"); var b = Task("居中任务B");
        var savedSession = controller.Engine.Edit(a.Id);
        controller.Engine.State.Preferences.Positions["edit-"+savedSession.Id] = new WindowPosition { X=12,Y=18,Width=400,Height=560 };
        var editA = controller.EditTask(a.Id); await Settled(editA.Host!);
        Check(Centered(controller, editA.Host!), "首个编辑窗口忽略已保存坐标并居中，保留尺寸");
        var editB = controller.EditTask(b.Id); await Settled(editB.Host!); var area=controller.WorkArea(controller.Ball);
        Check(Math.Abs(editB.Host!.Left - Math.Min(editA.Host!.Left+32,area.Right-editB.Host.Width)) < 1 && Math.Abs(editB.Host.Top-Math.Min(editA.Host.Top+32,area.Bottom-editB.Host.Height)) < 1, "后续编辑窗口按32/32错位并限制在屏幕内");
        controller.Settings(); var settings = Application.Current.Windows.OfType<SettingsSurface>().Single(); await Settled(settings);
        Check(Centered(controller, settings), "设置窗口居中打开");
        settings.Left -= 23; var existing = settings.Position(); controller.Settings();
        Check(Difference(settings.Position(),existing)<.01, "重复点击已打开设置只激活，不跳回中心或重播动画");
        settings.Hide(); await Hidden(settings); controller.Settings(); await Settled(settings);
        Check(Centered(controller, settings), "关闭后重开设置不记忆旧位置");
        var windows = Application.Current.Windows.OfType<SurfaceWindow>().Where(x=>x.IsVisible).ToArray();
        Check(windows.Length>=10 && windows.All(x=> {var style=Native.GetWindowLongPtr(new WindowInteropHelper(x).Handle,-20).ToInt64();return (style&0x80)!=0 && (style&0x40000)==0 && !x.ShowInTaskbar;}), "悬浮球、列表、新建、编辑、设置原生样式均排除Alt+Tab和任务栏");
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath,"workspace-report.json"),JsonSerializer.Serialize(new { passed=checks.Count,checks,input,visibleWindows=windows.Length,isolated=true,mouseMoved=false },new JsonSerializerOptions{WriteIndented=true}));
    }
    private static bool Centered(BoardController controller, SurfaceWindow window)
    { var area=controller.WorkArea(controller.Ball);return Math.Abs(window.Left-(area.Left+(area.Width-window.Width)/2))<1 && Math.Abs(window.Top-(area.Top+(area.Height-window.Height)/2))<1; }
    private static async Task Settled(SurfaceWindow window) { for(var i=0;i<600;i++){if(window.IsVisible&&!window.IsScrollAnimating&&!window.IsContentSwapHandoff)return;await Task.Delay(10);}throw new TimeoutException("等待悬浮窗口交接超时"); }
    private static async Task Hidden(SurfaceWindow window) { for(var i=0;i<600;i++){if(!window.IsVisible&&!window.IsScrollAnimating)return;await Task.Delay(10);}throw new TimeoutException("等待悬浮窗口关闭超时"); }
}
