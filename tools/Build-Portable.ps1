param([string]$Output = 'release/Taskboard')
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$destination = [System.IO.Path]::GetFullPath((Join-Path $repository $Output))
if (-not $destination.StartsWith($repository + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '发布目录必须位于当前项目内。'
}
if (Test-Path -LiteralPath $destination) { throw '请选择新的发布目录，不覆盖现有运行包或数据。' }
$defaultSound = Join-Path $repository 'src/Taskboard.Desktop/Assets/Alarm04.wav'
if (-not (Test-Path -LiteralPath $defaultSound)) {
    $systemSound = Join-Path ([Environment]::GetFolderPath('Windows')) 'Media/Alarm04.wav'
    if (-not (Test-Path -LiteralPath $systemSound)) { throw '请在Windows上构建，或将可合法使用的Alarm04.wav放入src/Taskboard.Desktop/Assets；该系统音频不随公开源码提供。' }
    Copy-Item -LiteralPath $systemSound -Destination $defaultSound
}
$packageCache = Join-Path $repository 'test-output/nuget-packages'
dotnet publish (Join-Path $repository 'src/Taskboard.Desktop/Taskboard.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:RestorePackagesPath=$packageCache" -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
Copy-Item -LiteralPath (Join-Path $repository 'docs/USER-GUIDE-0.9.22.md') -Destination (Join-Path $destination '使用说明.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs/RELEASE-NOTES-0.9.22.md') -Destination (Join-Path $destination '版本说明.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs/CODEX-PENDING-BRIDGE-PUBLIC.md') -Destination (Join-Path $destination 'CODEX-PENDING-BRIDGE-PUBLIC.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs/ACCEPTANCE-0.9.22-PUBLIC.md') -Destination (Join-Path $destination 'ACCEPTANCE-0.9.22-PUBLIC.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs/THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $destination 'THIRD-PARTY-NOTICES.txt')
Move-Item -LiteralPath (Join-Path $destination 'Taskboard.exe') -Destination (Join-Path $destination '即时任务看板.exe') -Force
Get-FileHash -LiteralPath (Join-Path $destination '即时任务看板.exe') -Algorithm SHA256 | Format-List
