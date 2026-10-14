# Visible compositor tests; run only during coordinated desktop acceptance.
param([string]$Executable, [string]$DataDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$testDirectory = [System.IO.Path]::GetFullPath((Join-Path $repository $DataDirectory))
$testRoot = Join-Path $repository 'test-output'
if (-not $testDirectory.StartsWith($testRoot + [System.IO.Path]::DirectorySeparatorChar,[System.StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $testDirectory)) { throw '必须使用test-output内新的隔离目录。' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AcceptanceForeground {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
}
'@
$before = [AcceptanceForeground]::GetForegroundWindow()
$process = $null
try {
    $process = Start-Process -FilePath (Join-Path $repository $Executable) -ArgumentList @('--data-dir',$testDirectory,'--no-integrations','--desktop-verify') -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $testDirectory 'ui-report.json'))) { throw '窗口验收未通过，请检查隔离目录的startup-error.txt、errors.log和motion-report.json。' }
    Get-Content -LiteralPath (Join-Path $testDirectory 'motion-report.json') -Raw
    Get-Content -LiteralPath (Join-Path $testDirectory 'ui-report.json') -Raw | ConvertFrom-Json | Select-Object passed,initialLayoutMs,cpuPercent
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    [AcceptanceForeground]::SetForegroundWindow($before) | Out-Null
}
