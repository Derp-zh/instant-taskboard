param([string]$Version = '0.8.0', [string]$PublishDirectory = '', [string]$ReleaseLabel = '')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号格式不正确。' }
if ($ReleaseLabel -notmatch '^[a-zA-Z0-9_-]*$') { throw '发布标记格式不正确。' }
$releaseVersion = $Version + $(if ($ReleaseLabel) { '-' + $ReleaseLabel } else { '' })
$repository = Split-Path -Parent $PSScriptRoot
$folder = if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { Join-Path $repository ('release/Taskboard-' + $Version) } else { [System.IO.Path]::GetFullPath((Join-Path $repository $PublishDirectory)) }
if (-not $folder.StartsWith((Join-Path $repository 'release') + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw '发布目录必须位于项目release内。' }
$archive = Join-Path $repository ('release/即时任务看板-' + $releaseVersion + '-win-x64.zip')
$entryName = '即时任务看板.exe'
if (-not (Test-Path -LiteralPath (Join-Path $folder $entryName))) { throw '必须先构建便携版。' }
if (Test-Path -LiteralPath (Join-Path $folder 'data')) { throw '发布目录含业务数据，停止打包。' }
Copy-Item -LiteralPath (Join-Path $repository 'docs/USER-GUIDE.md') -Destination (Join-Path $folder '使用说明.md')
$files = @(Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = [System.IO.Path]::GetRelativePath($folder, $_.FullName).Replace('\','/'); Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $archive -Force
# Verify every zipped byte against the output folder; no extraction or execution.
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entries = @($zip.Entries | Where-Object { $_.Name -ne '' })
    if ($entries.Count -ne $files.Count) { throw '压缩包文件数量不一致。' }
    foreach ($entry in $entries) {
        $name = $entry.FullName.Replace('\','/')
        $expected = @($files | Where-Object Path -eq $name)[0]
        if (-not $expected) { throw ('压缩包出现额外文件：' + $name) }
        $stream = $entry.Open()
        try { $hash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -ne $expected.SHA256 -or $entry.Length -ne $expected.Bytes) { throw ('压缩包校验失败：' + $name) }
    }
} finally { $zip.Dispose() }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repository 'src') -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = [System.IO.Path]::GetRelativePath($repository, $_.FullName).Replace('\','/'); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$info = [ordered]@{
    Version = $Version; BuildDate = (Get-Date -Format 'yyyy-MM-dd'); Runtime = 'self-contained win-x64'
    Entry = [System.IO.Path]::GetRelativePath($repository, (Join-Path $folder $entryName)).Replace('\','/'); Archive = ('release/即时任务看板-' + $releaseVersion + '-win-x64.zip')
    ArchiveSHA256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    FileCount = $files.Count; Files = $files; Sources = $sources; ZipAllFilesVerified = $true; ContainsData = $false
}
$info | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $repository ('docs/BUILD-INFO-' + $releaseVersion + '.json')) -Encoding utf8
[ordered]@{ Files = $files.Count; Archive = $archive; SHA256 = $info.ArchiveSHA256; FolderMB = [Math]::Round(($files.Bytes | Measure-Object -Sum).Sum / 1MB, 2); ArchiveMB = [Math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 2) } | ConvertTo-Json
