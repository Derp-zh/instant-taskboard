using System;
using System.Runtime.InteropServices;
namespace Taskboard.Desktop;
internal static class FrameTiming
{
    [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Ratio { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Timing
    {
        public uint Size; public Ratio RefreshRate; public ulong RefreshPeriod; public Ratio ComposeRate;
        public ulong VBlank,Refresh; public uint DXRefresh; public ulong Compose,Frame; public uint DXPresent;
        public ulong RefreshFrame,Submitted; public uint DXSubmitted; public ulong Confirmed; public uint DXConfirmed;
        public ulong RefreshConfirmed; public uint DXRefreshConfirmed; public ulong Late; public uint Outstanding;
        public ulong Displayed,DisplayedQpc,RefreshDisplayed,Complete,CompleteQpc,Pending,PendingQpc,
            FramesDisplayed,FramesComplete,FramesPending,Available,Dropped,Missed,NextDisplayed,NextPresented,
            RefreshesDisplayed,RefreshesPresented,RefreshStarted,PixelsReceived,PixelsDrawn,BuffersEmpty;
        internal double Hz => RefreshRate.Denominator==0 ? 0 : (double)RefreshRate.Numerator/RefreshRate.Denominator;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd,ref Timing info);
    internal static Timing Read()
    { var info = new Timing { Size=(uint)Marshal.SizeOf<Timing>() }; Marshal.ThrowExceptionForHR(DwmGetCompositionTimingInfo(IntPtr.Zero,ref info)); return info; }
}
