param([string]$Executable='release/即时任务看板-0.9.6-verified/即时任务看板.exe',[string]$DataDirectory='test-output/fullscreen-input-096-01')
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class FullscreenProbe96 {
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 public static object Process(IntPtr h){GetWindowThreadProcessId(h,out var p);return (long)p;}
}
'@
$scriptText=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Outside-Observer-Stress.ps1') -Raw
$scriptText=$scriptText.Replace('$repository = Split-Path -Parent $PSScriptRoot','$repository = (Get-Location).Path')
$scriptText=$scriptText.Replace("(Join-Path `$PSScriptRoot 'Outside-Click-Sink.ps1')","(Join-Path `$repository 'tools/Fullscreen-Input-Sink.ps1')")
$scriptText=$scriptText.Replace("'--outside-observer-verify'","'--game-input-verify'")
$start=$scriptText.IndexOf('function Click-Sink {');$end=$scriptText.IndexOf('[ObserverStressInput]::SetThreadDpiAwarenessContext', $start)
if($start -lt 0 -or $end -lt 0){throw '未找到隔离交互插入点'}
$click=@'
function Click-Sink {
    [ObserverStressInput]::SetForegroundWindow($sinkHandle) | Out-Null
    Start-Sleep -Milliseconds 180
    $game=Get-Content -LiteralPath (Join-Path $probeData 'game-sink-ready.json') -Raw | ConvertFrom-Json
    $rootAtPointer=[ObserverStressInput]::RootAt($game.X,$game.Y)
    if([FullscreenProbe96]::Process($rootAtPointer) -ne $probe.Id){throw '光标未覆盖隔离看板，不能证明全屏捕获场景'}
    $live=Get-Content -LiteralPath (Join-Path $probeData 'game-sink-live.json') -Raw | ConvertFrom-Json
    [ordered]@{Live=$live;Foreground=[FullscreenProbe96]::GetForegroundWindow().ToInt64();Sink=$sinkHandle.ToInt64();HitProcess=[FullscreenProbe96]::Process($rootAtPointer);ExpectedProbe=$probe.Id} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $probeData 'game-before-click.json') -Encoding utf8
    [IO.File]::WriteAllText((Join-Path $probeData 'overlay-hit-verified.txt'),'cursor-over-overlay-not-input-owner')
    [ObserverStressInput]::Click($game.X,$game.Y)
}
'@
$scriptText=$scriptText.Substring(0,$start)+$click+[Environment]::NewLine+$scriptText.Substring($end)
& ([scriptblock]::Create($scriptText)) -Executable $Executable -DataDirectory $DataDirectory
