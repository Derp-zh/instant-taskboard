param([string]$Executable = 'release/即时任务看板-0.9.4/即时任务看板.exe', [string]$DataDirectory = 'test-output/outside-observer-094-01')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$probeData = [IO.Path]::GetFullPath((Join-Path $repository $DataDirectory))
if (-not $probeData.StartsWith((Join-Path $repository 'test-output') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $probeData)) { throw '必须使用test-output下的新隔离目录。' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class ObserverStressInput {
  [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
  public static IntPtr RootAt(int x,int y) { return GetAncestor(WindowFromPoint(new Point {X=x,Y=y}),2); }
  public static void Click(int x,int y) { SetCursorPos(x,y); mouse_event(2,0,0,0,UIntPtr.Zero); Thread.Sleep(25); mouse_event(4,0,0,0,UIntPtr.Zero); }
}
'@
function Wait-Signal([string]$Name) {
    for ($i=0; $i -lt 300; $i++) {
        if (Test-Path -LiteralPath (Join-Path $probeData 'startup-error.txt')) { throw (Get-Content -LiteralPath (Join-Path $probeData 'startup-error.txt') -Raw) }
        if (Test-Path -LiteralPath (Join-Path $probeData $Name)) { return }
        Start-Sleep -Milliseconds 50
    }
    throw ('验证信号超时：' + $Name)
}
function Click-Sink {
    [ObserverStressInput]::SetWindowPos($sinkHandle,[IntPtr]::new(-1),50,50,220,100,0x4010) | Out-Null
    Start-Sleep -Milliseconds 100
    if ([ObserverStressInput]::RootAt(80,80) -ne $sinkHandle) { throw '鼠标落点不是隔离外部窗口，请重新协调桌面操作。' }
    [ObserverStressInput]::Click(80,80)
}
[ObserverStressInput]::SetThreadDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
$pointer = [ObserverStressInput+Point]::new(); [ObserverStressInput]::GetCursorPos([ref]$pointer) | Out-Null
$foreground = [ObserverStressInput]::GetForegroundWindow()
$sinkProcess = $null; $sinkHandle = [IntPtr]::Zero
$probe = $null
try {
    $probe = Start-Process -FilePath (Join-Path $repository $Executable) -ArgumentList @('--data-dir', $probeData, '--no-integrations', '--outside-observer-verify') -WindowStyle Hidden -PassThru
    Wait-Signal 'observer-ready.json'
    $sinkProcess = Start-Process -FilePath ([Environment]::ProcessPath) -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Outside-Click-Sink.ps1'), '-DataDirectory', $probeData) -WindowStyle Hidden -PassThru
    Wait-Signal 'sink-ready.txt'; $sinkHandle = [IntPtr]::new([long](Get-Content -LiteralPath (Join-Path $probeData 'sink-ready.txt') -Raw))
    [IO.File]::WriteAllText((Join-Path $probeData 'block-ui.signal'), '{}')
    Wait-Signal 'observer-blocking.json'; Start-Sleep -Milliseconds 100; Click-Sink
    Wait-Signal 'observer-second-ready.json'; Click-Sink
    [IO.File]::WriteAllText((Join-Path $probeData 'second-click.signal'), '{}')
    Wait-Signal 'observer-report.json'
    if (-not $probe.WaitForExit(10000) -or $probe.ExitCode -ne 0) { throw '隔离验证进程未正常结束。' }
    Get-Content -LiteralPath (Join-Path $probeData 'observer-report.json') -Raw
} finally {
    if ($probe -and -not $probe.HasExited) { Stop-Process -Id $probe.Id }
    if ($sinkProcess -and -not $sinkProcess.HasExited) { Stop-Process -Id $sinkProcess.Id }
    [ObserverStressInput]::SetCursorPos($pointer.X,$pointer.Y) | Out-Null
    [ObserverStressInput]::SetForegroundWindow($foreground) | Out-Null
}
