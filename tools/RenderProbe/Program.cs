using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;
using Taskboard.Desktop;

internal static class Program
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
    [STAThread] static void Main(string[] args)
    {
        var directory = Path.GetFullPath(args[0]); Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                foreach (var noRedirect in new[] { false, true })
                foreach (var extend in new[] { false, true })
                {
                    var root = new SurfaceContentRoot { Background = Brushes.Transparent };
                    root.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(153,25,35,51)), Stroke = Brushes.Aquamarine, StrokeThickness = 1 });
                    root.Children.Add(new TextBlock { Text = "◈", Foreground = Brushes.Aquamarine, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    var window = new Window { Width = 120, Height = 120, Left = 580, Top = 240, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                        Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, Content = new GlassContentHost(root) };
                    var hwnd = new WindowInteropHelper(window).EnsureHandle();
                    if (noRedirect) SetWindowLong(hwnd,-20,GetWindowLong(hwnd,-20) | 0x00200000);
                    var source = HwndSource.FromHwnd(hwnd); source.CompositionTarget.BackgroundColor = Colors.Transparent;
                    int on=1; DwmSetWindowAttribute(hwnd,17,ref on,4); DwmSetWindowAttribute(hwnd,33,ref on,4);
                    if (extend) { var margins = new Margins { Left=-1,Right=-1,Top=-1,Bottom=-1 }; DwmExtendFrameIntoClientArea(hwnd,ref margins); }
                    SetWindowRgn(hwnd,IntPtr.Zero,false);
                    using var backdrop = new LiveBackdrop(hwnd);
                    backdrop.Update(new[] { new GlassRegion(new Rect(0,0,120,120),Radius:60) },1.5);
                    window.Show(); await Task.Delay(350);
                    if (extend && !noRedirect)
                    {
                        root.UpdateLayout(); var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(180,180,144,144,PixelFormats.Pbgra32);
                        bitmap.Render(root); var bytes = new byte[180*180*4]; bitmap.CopyPixels(bytes,720,0);
                        var done = new TaskCompletionSource();
                        backdrop.Animate(hwnd,bytes,180,180,false,0,180,() => done.TrySetResult());
                        await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
                        backdrop.CancelAnimation(); File.WriteAllText(Path.Combine(directory,"gpu-success.txt"),"Composition animation completed");
                    }
                    var origin = window.PointToScreen(new Point());
                    using var image = new System.Drawing.Bitmap(180,180);
                    using (var graphics = System.Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen((int)origin.X,(int)origin.Y,0,0,image.Size);
                    image.Save(Path.Combine(directory,$"circle-{noRedirect}-{extend}.png"));
                    window.Close(); await Task.Delay(80);
                }
                // Verify that cached UI can be handed to the compositor without
                // involving WPF on every animation frame.
                app.Shutdown();
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(directory,"error.txt"),error.ToString()); app.Shutdown(1); }
        };
        app.Run();
    }
}
