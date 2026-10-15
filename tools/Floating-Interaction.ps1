param([string]$Executable = 'release/即时任务看板-0.9.5-standard/即时任务看板.exe', [string]$DataDirectory = 'test-output/floating-interaction-095-01')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$probeData = [IO.Path]::GetFullPath((Join-Path $repository $DataDirectory))
if (-not $probeData.StartsWith((Join-Path $repository 'test-output') + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $probeData)) { throw '必须使用test-output下的新隔离目录。' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class FloatingInput95 {
  [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr handle,uint flags);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
  public static IntPtr RootAt(int x,int y) { return GetAncestor(WindowFromPoint(new Point {X=x,Y=y}),2); }
  public static void Click(int x,int y) { SetCursorPos(x,y); mouse_event(2,0,0,0,UIntPtr.Zero); Thread.Sleep(25); mouse_event(4,0,0,0,UIntPtr.Zero); }
}
'@
function Wait-Ready([string]$Name) {
    for ($i=0; $i -lt 400; $i++) {
        if (Test-Path -LiteralPath (Join-Path $probeData 'startup-error.txt')) { throw (Get-Content -LiteralPath (Join-Path $probeData 'startup-error.txt') -Raw) }
        if (Test-Path -LiteralPath (Join-Path $probeData $Name)) { return (Get-Content -LiteralPath (Join-Path $probeData $Name) -Raw | ConvertFrom-Json) }
        Start-Sleep -Milliseconds 50
    }; throw ('验证信号超时：'+$Name)
}
function Double-Press($Ready,[string]$Name,[string]$Signal) {
    $point = $Ready.$Name; $x = [int]$point.x; $y = [int]$point.y
    if ([FloatingInput95]::RootAt($x,$y).ToInt64() -ne $Ready.columnHandle) { throw '鼠标落点被其他窗口覆盖，需重新协调。' }
    [FloatingInput95]::Click($x,$y); Start-Sleep -Milliseconds 65
    if ([FloatingInput95]::RootAt($x,$y).ToInt64() -ne $Ready.columnHandle) { throw '第二次点击落点被其他窗口覆盖，停止验证。' }
    [FloatingInput95]::Click($x,$y)
    [IO.File]::WriteAllText((Join-Path $probeData $Signal),'{}')
}
[FloatingInput95]::SetThreadDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
$pointer = [FloatingInput95+Point]::new(); [FloatingInput95]::GetCursorPos([ref]$pointer) | Out-Null
$foreground = [FloatingInput95]::GetForegroundWindow(); $probe = $null
try {
    $probe = Start-Process -FilePath (Join-Path $repository $Executable) -ArgumentList @('--data-dir',$probeData,'--no-integrations','--floating-verify') -WindowStyle Hidden -PassThru
    Double-Press (Wait-Ready 'floating-ready.json') 'cardA' 'card-a.signal'
    Double-Press (Wait-Ready 'floating-b-ready.json') 'cardB' 'card-b.signal'
    Double-Press (Wait-Ready 'floating-blank-ready.json') 'blank' 'blank.signal'
    Double-Press (Wait-Ready 'floating-header-ready.json') 'header' 'header.signal'
    Wait-Ready 'floating-report.json' | ConvertTo-Json -Depth 5
    if (-not $probe.WaitForExit(10000) -or $probe.ExitCode -ne 0) { throw '隔离验证未正常结束。' }
} finally {
    if ($probe -and -not $probe.HasExited) { Stop-Process -Id $probe.Id }
    [FloatingInput95]::SetCursorPos($pointer.X,$pointer.Y) | Out-Null
    [FloatingInput95]::SetForegroundWindow($foreground) | Out-Null
}
