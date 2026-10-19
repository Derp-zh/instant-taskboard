param([Parameter(Mandatory)][string]$DataDirectory)
$ErrorActionPreference='Stop'
$repository=Split-Path -Parent $PSScriptRoot
$probeData=[IO.Path]::GetFullPath($DataDirectory)
if (-not $probeData.StartsWith((Join-Path $repository 'test-output')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw '只支持隔离测试目录'}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class FullscreenSink96 {
 [StructLayout(LayoutKind.Sequential)] public struct Device {public ushort Page,Usage;public uint Flags;public IntPtr Target;}
 [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Device[] devices,uint count,uint size);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 public static bool Register(IntPtr target,bool remove) {return RegisterRawInputDevices(new[]{new Device{Page=1,Usage=2,Flags=remove?1u:0x30u,Target=target}},1,(uint)Marshal.SizeOf<Device>());}
}
'@
[FullscreenSink96]::SetThreadDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
$panel=[Windows.Forms.Form]::new(); $timer=[Windows.Forms.Timer]::new()
$panel.Text='隔离全屏输入模拟';$panel.ShowInTaskbar=$false;$panel.TopMost=$false
$panel.FormBorderStyle=[Windows.Forms.FormBorderStyle]::None
$panel.StartPosition=[Windows.Forms.FormStartPosition]::Manual;$panel.Bounds=[Windows.Forms.Screen]::PrimaryScreen.Bounds
$panel.BackColor=[Drawing.Color]::FromArgb(20,28,38)
$timer.Interval=50
$timer.Add_Tick({
 $foregroundOwn=[FullscreenSink96]::GetForegroundWindow() -eq $panel.Handle
 if($foregroundOwn){$panel.Capture=$true}
 [ordered]@{Handle=$panel.Handle.ToInt64();ForegroundOwn=$foregroundOwn;Capture=$panel.Capture;Bounds=$panel.Bounds.ToString()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $probeData 'game-sink-live.json') -Encoding utf8
})
$panel.Add_Shown({
 if(-not [FullscreenSink96]::Register($panel.Handle,$false)){throw '游戏式原始输入登记失败'}
 $panel.Activate();$panel.Capture=$true;[Windows.Forms.Cursor]::Hide();$timer.Start()
 [IO.File]::WriteAllText((Join-Path $probeData 'sink-ready.txt'),$panel.Handle.ToInt64().ToString())
 [ordered]@{Fullscreen=$true;NoLegacyMouseMessages=$true;CursorHidden=$true;InputCaptured=$true;X=($panel.Left+[int]($panel.Width/2));Y=($panel.Top+[int]($panel.Height/2))} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $probeData 'game-sink-ready.json') -Encoding utf8
})
try{[Windows.Forms.Application]::Run($panel)}finally{$timer.Stop();$timer.Dispose();$panel.Capture=$false;[Windows.Forms.Cursor]::Show();[FullscreenSink96]::Register([IntPtr]::Zero,$true)|Out-Null;$panel.Dispose()}
