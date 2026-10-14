using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class CardDragSession : IDisposable
{
    private readonly BoardController controller;
    private readonly Window ghost;
    private readonly DispatcherTimer timer;
    private readonly Vector offset;
    private readonly int pixelWidth;
    private readonly int pixelHeight;
    private readonly bool previousSuppression;
    private IntPtr handle;
    private readonly IntPtr ballHandle;
    private ColumnSurface? target;
    public Guid Id { get; }
    public WorkItem Item { get; }
    public double Height { get; }
    public int PositionUpdates { get; private set; }
    public int PreviewUpdates { get; private set; }
    public double SnapshotMs { get; }
    public double MaxMoveMs { get; private set; }
    public double MaxPreviewMs { get; private set; }
    private bool disposed;
    private System.Drawing.Point? previousPointer;
    public CardDragSession(BoardController controller, TaskCard card, WorkItem item, Point grab)
    {
        this.controller = controller; Item = item; Id = item.Id; Height = card.ActualHeight;
        ballHandle = new WindowInteropHelper(controller.Ball).Handle;
        previousSuppression = controller.SuppressCollapse; controller.SuppressCollapse = true;
        var dpi = VisualTreeHelper.GetDpi(card);
        pixelWidth = Math.Max(1, (int)Math.Ceiling(card.ActualWidth * dpi.DpiScaleX));
        pixelHeight = Math.Max(1, (int)Math.Ceiling(card.ActualHeight * dpi.DpiScaleY));
        offset = new Vector(grab.X * dpi.DpiScaleX, grab.Y * dpi.DpiScaleY);
        var watch = Stopwatch.StartNew();
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(card.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(card.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(card); bitmap.Freeze(); watch.Stop(); SnapshotMs = watch.Elapsed.TotalMilliseconds;
        ghost = new Window { Width = card.ActualWidth, Height = Height, WindowStyle = WindowStyle.None,
            Title = "任务卡片拖动预览",
            ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true, IsHitTestVisible = false,
            Content = new Image { Source = bitmap, Stretch = Stretch.Fill, Opacity = .85 }, Left = -10000, Top = -10000 };
        ghost.SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(ghost).Handle;
            var style = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, style | 0x20 | 0x08000000 | 0x80); // click-through, no activation, tool window
        };
        ghost.Show(); Move();
        foreach (var column in controller.Columns) column.BeginCardDrag(Id);
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) => Move(); timer.Start();
    }
    public void Move()
    {
        if (disposed || handle == IntPtr.Zero) return;
        var watch = Stopwatch.StartNew(); var cursor = System.Windows.Forms.Cursor.Position;
        if (previousPointer == cursor) return; previousPointer = cursor;
        Native.SetWindowPos(handle, ballHandle, controller.Smoke ? -10000 : (int)Math.Round(cursor.X - offset.X), controller.Smoke ? -10000 : (int)Math.Round(cursor.Y - offset.Y),
            pixelWidth, pixelHeight, 0x0011 | 0x0200);
        PositionUpdates++; watch.Stop(); MaxMoveMs = Math.Max(MaxMoveMs, watch.Elapsed.TotalMilliseconds);
    }
    public void Preview(ColumnSurface column, Guid? before)
    {
        var watch = Stopwatch.StartNew();
        if (!ReferenceEquals(target, column)) { target?.ClearCardPreview(); target = column; }
        if (column.SetCardPreview(Item, before)) PreviewUpdates++;
        Move(); watch.Stop(); MaxPreviewMs = Math.Max(MaxPreviewMs, watch.Elapsed.TotalMilliseconds);
    }
    public void Leave(ColumnSurface column)
    { if (!ReferenceEquals(target, column)) return; column.ClearCardPreview(); target = null; }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer.Stop(); ghost.Close();
        foreach (var column in controller.Columns) column.EndCardDrag();
        controller.SuppressCollapse = previousSuppression; Native.RaiseBall();
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
