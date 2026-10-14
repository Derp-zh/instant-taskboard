#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$DataDirectory,
    [ValidateSet('listening', 'scroll', 'appearance', 'integration', 'compact', 'startup-entry')][string]$Mode = 'listening'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$taskData = [System.IO.Path]::GetFullPath((Join-Path $repository $DataDirectory))
$taskRoot = Join-Path $repository 'test-output'
if (-not $taskData.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskData)) {
    throw '必须使用test-output内新的隔离目录。'
}
$process = $null
try {
    $process = Start-Process -FilePath (Join-Path $repository $Executable) -ArgumentList @('--data-dir', $taskData, '--no-integrations', "--$Mode-verify") -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) { throw '模块验证超时。' }
    if ($process.ExitCode -ne 0) { throw "模块验证失败，请查看隔离目录：$taskData" }
    $reportFile = Join-Path $taskData "$Mode-report.json"
    if (-not (Test-Path -LiteralPath $reportFile)) { throw '验证报告缺失。' }
    $report = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
    $report | Select-Object passed, boundaries
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
