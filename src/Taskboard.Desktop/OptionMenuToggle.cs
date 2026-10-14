using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal sealed class OptionMenuToggle : IDisposable
{
    private readonly Button button;
    private ContextMenu? menu;
    private bool dismissedByTrigger;
    public OptionMenuToggle(Button button)
    { this.button = button; InputManager.Current.PostProcessInput += ProcessInput; }
    public bool CloseOrConsume()
    {
        if (dismissedByTrigger) { dismissedByTrigger = false; return true; }
        if (menu?.IsOpen != true) return false;
        menu.IsOpen = false; return true;
    }
    public void Open(ContextMenu next)
    {
        menu = next; button.ContextMenu = next; next.PlacementTarget = button;
        next.Placement = PlacementMode.Bottom; next.VerticalOffset = 4;
        var openProperty = DependencyPropertyDescriptor.FromProperty(ContextMenu.IsOpenProperty, typeof(ContextMenu));
        EventHandler changed = (_, _) =>
        {
            if (next.IsOpen) return;
            if (PresentationSource.FromVisual(button) is null) { dismissedByTrigger = false; return; }
            // A popup dismisses on mouse-down, before the button raises Click.
            // Preserve that dismissal through this release, rather than reopening.
            var cursor = System.Windows.Forms.Cursor.Position;
            var point = button.PointFromScreen(new Point(cursor.X, cursor.Y));
            dismissedByTrigger = (GetAsyncKeyState(1) & 0x8000) != 0 && new Rect(button.RenderSize).Contains(point);
        };
        // Closed arrives after the popup's fade-out, often after mouse release.
        // Observe IsOpen immediately, while the dismissing press is still down.
        openProperty.AddValueChanged(next, changed);
        next.Closed += (_, _) => openProperty.RemoveValueChanged(next, changed);
        next.IsOpen = true;
    }
    private void ProcessInput(object sender, ProcessInputEventArgs args)
    {
        if (args.StagingItem.Input is MouseButtonEventArgs mouse && mouse.ChangedButton == MouseButton.Left && mouse.ButtonState == MouseButtonState.Released)
            button.Dispatcher.BeginInvoke(new Action(() => dismissedByTrigger = false), DispatcherPriority.ContextIdle);
    }
    public void Close() { if (menu is not null) menu.IsOpen = false; dismissedByTrigger = false; }
    public void Dispose() { Close(); InputManager.Current.PostProcessInput -= ProcessInput; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
