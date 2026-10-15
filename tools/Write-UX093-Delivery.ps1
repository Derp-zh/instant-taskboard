$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
function Read-Report([string]$path) { Get-Content -LiteralPath (Join-Path $repository $path) -Raw | ConvertFrom-Json }
function Evidence([string]$path) { [ordered]@{ Path=$path; SHA256=(Get-FileHash -LiteralPath (Join-Path $repository $path)).Hash } }
$normalCompact = 'test-output/ux-093-final-normal-compact-02/compact-report.json'
$fastCompact = 'test-output/ux-093-final-fast-compact-03/compact-report.json'
$windows = 'test-output/ux-093-final-windows-02/ui-report.json'
$scroll = 'test-output/ux-093-final-scroll-02/scroll-report.json'
$outside = 'test-output/outside-click-093-09/outside-click-report.json'
$normalMotion = 'test-output/ux-093-final-normal-motion-02/shared-motion-report.json'
$fastMotion = 'test-output/ux-093-final-fast-motion-04/shared-motion-report.json'
$normalBall = 'test-output/ux-093-final-normal-motion-02/animation-pixels.json'
$fastBall = 'test-output/ux-093-final-fast-motion-04/animation-pixels.json'
if ((Read-Report $normalCompact).passed -ne 76 -or (Read-Report $fastCompact).passed -ne 76 -or (Read-Report $windows).passed -ne 77 -or (Read-Report $scroll).passed -ne 45) { throw '控件、窗口或画卷报告未通过。' }
if ((Read-Report 'test-output/ux-092-rules-02/report.json').passed -ne 122) { throw '复用的原规则报告不完整。' }
if ((Read-Report $normalCompact).switchSpeed -ne 1 -or (Read-Report $fastCompact).switchSpeed -ne 1.35) { throw '两个实际程序未使用对应速度。' }
foreach ($path in @($normalMotion,$fastMotion,$normalBall,$fastBall)) { if (-not (Read-Report $path).passed) { throw ('实际像素报告失败：'+$path) } }
$press = Read-Report $outside
foreach ($property in @('firstVisible','slowVisible','differentVisible','doubleClosed','sharedHost','settingsSameHost','settingsSingleVisible','settingsDoubleClosed','draftCloseKeepsLists','settingsCloseKeepsLists','freshOutsideCollapses','draftExternalCollapsed','draftGroupRestored','draftRestoredValue','settingsExternalCollapsed','settingsGroupRestored')) { if (-not $press.$property) { throw ('真实点击报告失败：'+$property) } }
$coreOld = (Get-FileHash -LiteralPath (Join-Path $repository 'release/即时任务看板-0.9.2-final/Taskboard.Core.dll')).Hash
$variants = [ordered]@{}
foreach ($label in @('normal','fast135')) {
    $manifest = Read-Report ('docs/BUILD-INFO-0.9.3-'+$label+'.json')
    if (-not $manifest.ZipAllFilesVerified -or $manifest.FileCount -ne 476 -or $manifest.ContainsData) { throw '便携包完整性未通过。' }
    if ((Get-FileHash -LiteralPath (Join-Path $repository $manifest.Archive)).Hash -ne $manifest.ArchiveSHA256) { throw 'ZIP在验证后发生变化。' }
    foreach ($source in $manifest.Sources) { if ((Get-FileHash -LiteralPath (Join-Path $repository $source.Path)).Hash -ne $source.SHA256) { throw ('源码在打包后发生变化：'+$source.Path) } }
    $folder = Split-Path -Parent (Join-Path $repository $manifest.Entry)
    if ((Get-FileHash -LiteralPath (Join-Path $folder 'Taskboard.Core.dll')).Hash -ne $coreOld) { throw '核心DLL改变，不能复用原规则报告。' }
    $variants[$label] = [ordered]@{
        Entry=$manifest.Entry; BuildInfo=('docs/BUILD-INFO-0.9.3-'+$label+'.json'); Archive=$manifest.Archive
        ArchiveSHA256=$manifest.ArchiveSHA256; EntrySHA256=(Get-FileHash -LiteralPath (Join-Path $repository $manifest.Entry)).Hash
        RuntimeSHA256=(Get-FileHash -LiteralPath (Join-Path $folder 'Taskboard.dll')).Hash
        FileCount=$manifest.FileCount; ZipAllFilesVerified=$true; Speed=$manifest.DraftSwitchSpeed; DurationMs=$manifest.DraftSwitchDurationMs
    }
}
$report = [ordered]@{
    Version='0.9.3'; Date='2026-10-15'; LocalOnly=$true; Requirements='PRD U79-U85 / A97-A103 / T101-T108'
    PreferredSpeed='pending-user-comparison'; Variants=$variants
    Counts=[ordered]@{ NormalCompact=76; FastCompact=76; Windows=77; ScrollIncludingColdFirstDisplay=45; RulesReused=122 }
    CoreRulesReuse=[ordered]@{ Mode='exact-identical-Core-DLL'; SHA256=$coreOld; Report=(Evidence 'test-output/ux-092-rules-02/report.json'); ReRun=$false }
    Evidence=[ordered]@{
        NormalCompact=(Evidence $normalCompact); FastCompact=(Evidence $fastCompact); Windows=(Evidence $windows); Scroll=(Evidence $scroll)
        Outside=(Evidence $outside); NormalCard=(Evidence $normalMotion); FastCard=(Evidence $fastMotion); NormalBall=(Evidence $normalBall); FastBall=(Evidence $fastBall)
    }
    Measurements=[ordered]@{ NormalCard=(Read-Report $normalMotion | Select-Object durationMs,LastContentSwapPreparationMs,preparationDifference,sampledFrames,lateFrames,finalHandoffMaxColorDifference,transparencyDifference,upperGapFilled,lowerGapFilled); FastCard=(Read-Report $fastMotion | Select-Object durationMs,LastContentSwapPreparationMs,preparationDifference,sampledFrames,lateFrames,finalHandoffMaxColorDifference,transparencyDifference,upperGapFilled,lowerGapFilled); Outside=$press; Idle=(Read-Report $windows | Select-Object idleSeconds,cpuPercent,cpuCorePercent) }
    RuntimeNotes=@('最终中文入口两版控件检查、原速77窗口与45画卷对应当前产物。','原速/快版像素采样在只改ScrollMotionChecks同帧采样器之前取得；正常产品代码一致，不声称全部报告的DLL字节相同。','真实鼠标09报告验证最新已确认规则；随后修正共用动画交接及测试等待，最终两版控件检查再次回归相同规则，没有再次移动鼠标。','准备时间和动作时间分别记录；有限采样不代表任意硬件/负载的恒定120帧或绝对无闪烁。')
    DataSafety=[ordered]@{ IsolatedTestData=$true; FormalDataChanged=$false; UserProcessesStopped=$false; RealNotificationsChanged=$false; StartupRegistryChanged=$false; GitHubPublished=$false }
    FailedEvidence='test-output本轮失败及无有效落点报告保留；不计为通过。'
    CodebaseMemory=[ordered]@{ ConnectedAtStart=$true; InitialRoot=$repository; InitialNodes=4357; InitialEdges=12007; FinalADRUpdateVerified=$false; FinalReindexVerified=$false; Error='Transport closed'; Pending='恢复连接后按INDEX-STATUS.md更新ADR、full重建并检查覆盖，不以旧图证明当前源码。' }
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $repository 'docs/VERIFICATION-0.9.3.json') -Encoding utf8
[ordered]@{ Verification='docs/VERIFICATION-0.9.3.json'; Counts=$report.Counts; CoreRulesReuse=$report.CoreRulesReuse.Mode; Variants=$variants.Keys } | ConvertTo-Json -Depth 3
