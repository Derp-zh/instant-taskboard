using System;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.Graphics.DirectX;
using Windows.Foundation;
using Vortice.Direct3D11;
using Vortice.Direct2D1;
using Vortice.DCommon;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinRT;

namespace Taskboard.Desktop;

// Upload once per UI change; the composition thread owns every animation frame.
internal sealed class CompositorPixels : IDisposable
{
    [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface CompositorInterop
    {
        void CreateCompositionSurfaceForHandle(IntPtr handle, out IntPtr result);
        void CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr result);
        void CreateGraphicsDevice(IntPtr device, out IntPtr result);
    }
    [ComImport, Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface SurfaceInterop
    {
        void BeginDraw(IntPtr rectangle, ref Guid iid, out IntPtr context, out PixelPoint offset);
        void EndDraw();
    }
    [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X, Y; }
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags,
        IntPtr levels, uint levelCount, uint version, out IntPtr device, out int level, out IntPtr context);
    private readonly ID3D11Device device;
    private readonly ID2D1Device drawing;
    private readonly CompositionGraphicsDevice graphics;
    internal CompositorPixels(Compositor compositor)
    {
        var result = D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out var raw,out _,out var context);
        if (result < 0) result = D3D11CreateDevice(IntPtr.Zero,5,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out raw,out _,out context);
        Marshal.ThrowExceptionForHR(result); device = new ID3D11Device(raw); Marshal.Release(context);
        using var dxgi = device.QueryInterface<IDXGIDevice>(); drawing = D2D1.D2D1CreateDevice(dxgi);
        compositor.As<CompositorInterop>().CreateGraphicsDevice(drawing.NativePointer,out var factory);
        try { graphics = MarshalInterface<CompositionGraphicsDevice>.FromAbi(factory); } finally { Marshal.Release(factory); }
    }
    internal CompositionDrawingSurface Upload(byte[] pixels, int width, int height)
    {
        var surface = graphics.CreateDrawingSurface(new Windows.Foundation.Size(width,height),DirectXPixelFormat.B8G8R8A8UIntNormalized,DirectXAlphaMode.Premultiplied);
        var interop = surface.As<SurfaceInterop>(); var iid = typeof(ID2D1DeviceContext).GUID;
        interop.BeginDraw(IntPtr.Zero,ref iid,out var raw,out var offset);
        try
        {
            using var context = new ID2D1DeviceContext(raw); context.SetDpi(96,96);
            // Composition can place several surfaces on one GPU atlas. Clear
            // only this surface's allocation, or later uploads erase earlier cards.
            context.PushAxisAlignedClip(new Vortice.RawRectF(offset.X, offset.Y, offset.X + width, offset.Y + height), AntialiasMode.Aliased);
            context.Clear(new Color4(0,0,0,0));
            var pin = GCHandle.Alloc(pixels,GCHandleType.Pinned);
            try
            {
                using var bitmap = context.CreateBitmap(new SizeI(width,height),pin.AddrOfPinnedObject(),(uint)(width*4),
                    new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm,Vortice.DCommon.AlphaMode.Premultiplied),96,96,BitmapOptions.None));
                context.DrawBitmap(bitmap,new Vortice.RawRectF(offset.X,offset.Y,offset.X+width,offset.Y+height),1,InterpolationMode.Linear,null,null);
            }
            finally { pin.Free(); context.PopAxisAlignedClip(); }
        }
        finally { interop.EndDraw(); }
        return surface;
    }
    public void Dispose() { graphics.Dispose(); drawing.Dispose(); device.Dispose(); }
}
