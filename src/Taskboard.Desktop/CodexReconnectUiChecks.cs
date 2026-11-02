using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class CodexReconnectUiChecks
{
    internal static async Task Run(BoardController controller)
    {
        if (!controller.Isolated || !controller.Smoke) throw new InvalidOperationException("Only isolated offscreen controls");
        var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
        var calls = 0;
        Check((await controller.LoadConversations(() => { calls++; return Task.FromResult(Array.Empty<CodexThread>()); })).Length == 0, "首次目录空可返回");
        Check((await controller.LoadConversations(() => { calls++; return Task.FromResult(new[] { new CodexThread("local", "本地会话", 1) }); })).Length == 1 && calls == 2, "空目录不缓存30秒，下一次点击即可恢复");
        Check((await controller.LoadConversations(() => throw new Exception("should use healthy cache"))).Length == 1, "成功目录继续复用缓存，不重复加载");
        typeof(BoardController).GetField("conversationLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, Task.FromException<CodexThread[]>(new IOException("stale reader")));
        Check((await controller.LoadConversations(() => Task.FromResult(new[] { new CodexThread("recovered", "已恢复", 1) }))).Single().Id == "recovered", "旧失败不阻止再次加载");
        var canceled = new System.Threading.CancellationToken(true);
        typeof(BoardController).GetField("conversationLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, Task.FromCanceled<CodexThread[]>(canceled));
        var completion = new TaskCompletionSource<CodexThread[]>(); calls = 0;
        Task<CodexThread[]> Load() { calls++; return completion.Task; }
        var a = controller.LoadConversations(Load); var b = controller.LoadConversations(Load);
        completion.SetResult([new("fresh", "新会话", 1)]); await Task.WhenAll(a, b);
        Check(calls == 1 && a.Result.Length == 1 && b.Result.Length == 1, "取消后可重试，并发选择器共享同次加载");
        var custom = new BoardColumn { Name = "进行中", Visible = false }; controller.Engine.State.Preferences.Columns.Add(custom);
        var extra = new ColumnSurface(controller, custom);
        var columns = controller.Columns.Concat([extra]).ToArray();
        TextBlock Hint(ColumnSurface column) => column.Root.Children.OfType<TextBlock>().Single(x => Equals(x.Tag, "CodexConnectionHint"));
        foreach (var column in columns) column.RefreshCodexConnectionHint();
        Check(columns.Where(x => x.Definition.BuiltinLane is Lane.Running or Lane.Action).All(x => Hint(x).Visibility == Visibility.Visible && Hint(x).Text.Contains("自动重试")), "连接失败只在两个内置列表显示常驻轻提示");
        Check(columns.Where(x => x.Definition.BuiltinLane is not (Lane.Running or Lane.Action)).All(x => Hint(x).Visibility == Visibility.Collapsed), "其余四列和同名自定义列不出现提示");
        var running = columns.Single(x => x.Definition.BuiltinLane == Lane.Running);
        var hint = Hint(running);
        Check(Grid.GetRow(hint) == 2 && !hint.IsHitTestVisible && hint.FontSize == 10 && hint.Opacity < .7, "提示固定底部、低调且不抢鼠标输入");
        running.Root.Measure(new Size(240, 680)); running.Root.Arrange(new Rect(0, 0, 240, 680)); running.Root.UpdateLayout();
        var image = new RenderTargetBitmap(240, 680, 96, 96, PixelFormats.Pbgra32); image.Render(running.Root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(Path.Combine(controller.Store.DirectoryPath, "connection-footer.png"))) png.Save(stream);
        Check(hint.ActualHeight > 0 && hint.TransformToAncestor(running.Root).Transform(new Point()).Y > 600, "实际离屏布局将提示固定在最下侧");
        await controller.Codex.DisposeAsync();
        await using var observer = new CodexObserver(localConnect: _ => Task.FromResult<ICodexLocalEvents>(new Local()));
        typeof(BoardController).GetProperty("Codex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, observer);
        observer.Start("", "");
        for (var i = 0; i < 100 && !observer.TransportConnected; i++) await Task.Delay(20);
        foreach (var column in columns) column.RefreshCodexConnectionHint();
        Check(observer.TransportConnected && columns.All(x => Hint(x).Visibility == Visibility.Collapsed), "模拟恢复连接后全部提示消失");
        extra.Destroy();
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "reconnect-ui-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks,
            offscreen = true, originalCodexTouched = false, mouseMoved = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class Local : ICodexLocalEvents
    {
        public long Frames => 0;
        public bool Connected { get; private set; } = true;
        public event Action<string>? Changed { add { } remove { } }
        public Task Subscribe(string[] ids) => Task.CompletedTask;
        public CodexDesktopSnapshot Snapshot(string id) => new(default, [], false, "fixture", "local");
        public ValueTask DisposeAsync() { Connected = false; return ValueTask.CompletedTask; }
    }
}
