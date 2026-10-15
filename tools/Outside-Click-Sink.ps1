param([Parameter(Mandatory)][string]$DataDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$probeData = [IO.Path]::GetFullPath($DataDirectory)
if (-not $probeData.StartsWith((Join-Path $repository 'test-output') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '只支持test-output隔离目录。' }
Add-Type -AssemblyName System.Windows.Forms
$panel = [Windows.Forms.Form]::new()
$panel.Text = '隔离外部点击窗口'; $panel.ShowInTaskbar = $false; $panel.TopMost = $true
$panel.StartPosition = [Windows.Forms.FormStartPosition]::Manual
$panel.Location = [Drawing.Point]::new(50,50); $panel.Size = [Drawing.Size]::new(220,100)
$panel.Add_Shown({ [IO.File]::WriteAllText((Join-Path $probeData 'sink-ready.txt'), $panel.Handle.ToInt64().ToString()) })
try { [Windows.Forms.Application]::Run($panel) } finally { $panel.Dispose() }
