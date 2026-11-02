using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Taskboard.Desktop;

// Capture stays with the source button; no OLE data is dropped into Codex.
// Thus a failed/cancelled attempt cannot attach a file, insert text or navigate.
internal sealed class ConversationBindingDrag : IDisposable
{
    private readonly BoardController controller;
    private readonly ColumnSurface source;
    private readonly Guid task, stage;
    private readonly Guid? expectedBinding;
    private readonly object sourceState;
    private readonly bool previousSuppression;
    private readonly List<ColumnSurface> hidden = [];
    private readonly Window? ghost;
    private readonly TextBlock? feedback;
    private readonly DispatcherTimer? timer;
    private Task<ConversationDropTarget>? probe;
    private bool disposed, completing, leftSource;
    private long nextProbe;
    private IntPtr handle;
    internal int HiddenCount => hidden.Count;
    internal bool LeftSource => leftSource;
    internal ConversationBindingDrag(BoardController controller, ColumnSurface source, Guid task, Guid stage, Guid? expectedBinding, string caption)
    {
        this.controller = controller; this.source = source; this.task = task; this.stage = stage; this.expectedBinding = expectedBinding;
        sourceState = controller.Engine.State;
        controller.ConversationDrag?.Dispose(); controller.ConversationDrag = this;
        previousSuppression = controller.SuppressCollapse; controller.SuppressCollapse = true;
        if (controller.Smoke) return; // Verification never probes or moves the user's pointer.
        try
        {
            var pane = new StackPanel();
            pane.Children.Add(new TextBlock { Text = caption, Foreground = Ui.Text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280 });
            feedback = new TextBlock { Text = "拖到 Codex 会话 · Esc 取消", Foreground = Ui.Muted, FontSize = 11, MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
            pane.Children.Add(feedback);
            ghost = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                IsHitTestVisible = false, SizeToContent = SizeToContent.WidthAndHeight, Left = -12000, Top = -12000,
                Title = "拖动绑定会话 · 名称预览",
                Content = new Border { Background = Ui.Brush("#ED192333"), BorderBrush = Ui.Accent, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Child = pane } };
            ghost.SourceInitialized += (_, _) =>
            {
                handle = new WindowInteropHelper(ghost).Handle;
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000 | 0x80);
            };
            ghost.Show();
            timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += Tick; timer.Start(); Tick(null, EventArgs.Empty);
        }
        catch { Dispose(); throw; }
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (disposed || completing) return;
        if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { Dispose(); return; }
        var cursor = System.Windows.Forms.Cursor.Position; var point = new Point(cursor.X, cursor.Y);
        MoveTo(point);
        if (handle != IntPtr.Zero && ghost is not null)
            Native.SetWindowPos(handle, new IntPtr(-1), cursor.X + 18, cursor.Y + 22, 0, 0, 0x0011);
        if (!leftSource || probe is { IsCompleted: false } || Environment.TickCount64 < nextProbe) return;
        nextProbe = Environment.TickCount64 + 250;
        _ = Preview(point);
    }
    private async Task Preview(Point point)
    {
        try
        {
            var request = probe = Task.Run(() => CodexConversationDropTarget.Read(point));
            var target = await request;
            if (disposed || completing || feedback is null) return;
            feedback.Text = target.CanBind ? "松开绑定 · " + target.Name + "\n" + target.ThreadId : target.Reason;
            feedback.Foreground = target.CanBind || target.CanResolveOnDrop ? Ui.Accent : Ui.Muted;
        }
        catch { if (!disposed && feedback is not null) feedback.Text = "读取失败，松开不会绑定"; }
    }
    internal void MoveTo(Point point)
    {
        if (disposed || leftSource || Contains(source, point)) return;
        leftSource = true;
        foreach (var column in controller.Columns.Where(x => x != source && x.IsVisible))
        { hidden.Add(column); column.Hide(); }
    }
    private static bool Contains(ColumnSurface column, Point point)
    {
        try
        {
            if (!column.IsVisible) return false;
            return new Rect(column.PointToScreen(new Point()), column.PointToScreen(new Point(column.ActualWidth, column.ActualHeight))).Contains(point);
        }
        catch (InvalidOperationException) { return false; }
    }
    internal async Task Complete(Point point, Func<Point, Task<ConversationDropTarget>>? readForCheck = null)
    {
        if (disposed || completing) return;
        completing = true; timer?.Stop();
        ghost?.Hide();
        var target = ConversationDropTarget.Unavailable("没有拖出来源列表，原绑定保留");
        var cancelled = false;
        try
        {
            if (leftSource && !Contains(source, point) && (!controller.Smoke || readForCheck is not null))
            {
                // Always read again at release. Never commit the last hover's ID.
                if (probe is { IsCompleted: false }) await probe.WaitAsync(TimeSpan.FromSeconds(1));
                target = readForCheck is null ? await Task.Run(() => CodexConversationDropTarget.Read(point)).WaitAsync(TimeSpan.FromSeconds(3)) : await readForCheck(point);
                if (!disposed && !target.CanBind && target.CanResolveOnDrop && !controller.Smoke) target = await CodexConversationMenu.Resolve(point);
            }
        }
        catch (Exception) { target = ConversationDropTarget.Unavailable("读取准确编号超时或失败，原绑定保留"); }
        finally { cancelled = disposed || !ReferenceEquals(controller.ConversationDrag, this); Dispose(); }
        if (cancelled) return;
        var message = await controller.CompleteConversationBinding(task, stage, expectedBinding, target, sourceState);
        if (source.IsVisible) Ui.Hint(source.Root, message, !controller.Smoke, source.Root);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer?.Stop(); ghost?.Close();
        // Hook/raw presses are delivered asynchronously. A cancelling right
        // press must not fold the restored workspace after this gesture ends.
        controller.FinishConversationInput();
        if (ReferenceEquals(controller.ConversationDrag, this)) controller.ConversationDrag = null;
        controller.SuppressCollapse = previousSuppression;
        foreach (var column in hidden.Where(x => controller.Expanded && x.Definition.Visible && !x.Definition.Deleted && controller.Columns.Contains(x)))
        { column.Show(); column.RestartCardMarquees(); }
        hidden.Clear();
    }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
