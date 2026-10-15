$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repository
function Report([string]$Path) {
    if (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $Path) 'startup-error.txt')) { throw ('验证错误：' + $Path) }
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}
function Evidence([string]$Path) { [ordered]@{ Path=$Path; SHA256=(Get-FileHash -LiteralPath $Path).Hash } }
$build = Report 'docs/BUILD-INFO-0.9.5-standard.json'
if ($build.Version -ne '0.9.5' -or $build.DraftSwitchSpeed -ne 1.35 -or $build.OtherAnimationDurationMs -ne 100 -or -not $build.ZipAllFilesVerified -or $build.ContainsData) { throw '运行包参数错误' }
$folder = Split-Path -Parent $build.Entry
foreach ($file in $build.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $folder $file.Path)).Hash -ne $file.SHA256) { throw ('运行文件变化：'+$file.Path) }
}
foreach ($file in $build.Sources) {
    if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.SHA256) { throw ('来源变化：'+$file.Path) }
}
if ((Get-FileHash -LiteralPath $build.Archive).Hash -ne $build.ArchiveSHA256) { throw 'ZIP变化' }
$compactPath='test-output/ux-095-compact-standard-02/compact-report.json'
$scrollPath='test-output/ux-095-scroll-final-01/scroll-report.json'
$stackPath='test-output/ux-095-stack-standard-01/scroll-report.json'
$sharedPath='test-output/ux-095-shared-standard-01/shared-motion-report.json'
$floatingPath='test-output/floating-interaction-095-final-01/floating-report.json'
$compact=Report $compactPath; $scroll=Report $scrollPath; $stack=Report $stackPath
$shared=Report $sharedPath; $floating=Report $floatingPath
$badge=Report 'test-output/ux-095-compact-standard-02/badge-size.json'
if ($compact.passed -ne 82 -or $scroll.passed -ne 111 -or $stack.passed -ne 8 -or -not $shared.passed -or $floating.passed -ne 6 -or $badge.Width -ne $badge.Height -or $badge.Width -gt 38) { throw '隔离验证不完整' }
$motions=@(Get-ChildItem -LiteralPath 'test-output/ux-095-scroll-final-01/scroll-frames' -Filter '*-motion.json' | ForEach-Object { Report $_.FullName })
if ($motions.Count -ne 45 -or @($motions | Where-Object { -not $_.UsesIndependentAnimation -or $_.LastAnimationError }).Count) { throw '动画发生回退或错误' }
$controller=Get-Content -LiteralPath 'src/Taskboard.Desktop/BoardController.cs' -Raw
if ($controller -notmatch 'previous.Left \+ 32' -or $controller -notmatch 'previous.Top \+ 32') { throw '最终错位不是32/32' }
$coreHash=(Get-FileHash -LiteralPath (Join-Path $folder 'Taskboard.Core.dll')).Hash
$priorCoreHash=(Get-FileHash -LiteralPath 'release/即时任务看板-0.9.2-final/Taskboard.Core.dll').Hash
$rules=Report 'test-output/ux-092-rules-02/report.json'
if ($coreHash -ne $priorCoreHash -or $rules.passed -ne 122) { throw '复用核心规则依据不符' }
$result=[ordered]@{
    Version='0.9.5'; Date='2026-10-15'; LocalOnly=$true; Requirements='PRD U91-U95 / A109-A113 / T115-T120'
    Entry=$build.Entry; Archive=$build.Archive; ArchiveSHA256=$build.ArchiveSHA256; FileCount=$build.FileCount; ZipAllFilesVerified=$true
    RuntimeSHA256=(Get-FileHash -LiteralPath (Join-Path $folder 'Taskboard.dll')).Hash
    DraftSwitchSpeed=1.35; DraftSwitchDurationMs=360/1.35; OtherAnimationDurationMs=100
    FinalEditorOffset=[ordered]@{ X=32; Y=32; Units='DIP'; UserSelected=$true; VerifiedBy='源码参数及最终publish构建'; PhysicalFixtureEarlierX=$floating.deltaX; PhysicalFixtureEarlierY=$floating.deltaY }
    Compact=[ordered]@{ Passed=82; Report=(Evidence $compactPath); Badge=$badge }
    UnifiedScroll=[ordered]@{ Passed=111; Report=(Evidence $scrollPath); RecordedScenes=45; AnimationFallbacks=0; AnimationErrors=0 }
    MultiDraftSingleFlare=[ordered]@{ Passed=8; Report=(Evidence $stackPath) }
    FloatingGesture=[ordered]@{ Passed=6; Report=(Evidence $floatingPath); Rules='卡片打开/复用；空白或标题双击只关闭最上层' }
    SharedMotion=[ordered]@{ Passed=$shared.passed; Report=(Evidence $sharedPath); DurationMs=$shared.durationMs }
    EvidenceBoundary='桌面报告在最终32/32微调前完成；动画实现及命中策略之后未改，最后仅错位常量及对应检查变化，最终publish成功。未将旧52/44的实机几何报告称为32/32实测。'
    CoreRules=[ordered]@{ Passed=122; Reused=$true; Rerun=$false; CoreSHA256=$coreHash; PriorCoreSHA256=$priorCoreHash; Report=(Evidence 'test-output/ux-092-rules-02/report.json') }
    Boundaries=@('所有数据隔离','真实鼠标已协调并恢复','未改正式数据或启动登记','未退出用户进程','未上传GitHub','有限画面采样不承诺任意设备或负载下零闪烁')
    MemoryRecord='docs/INDEX-STATUS.md'
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath 'docs/VERIFICATION-0.9.5.json' -Encoding utf8
$acceptance=@"
# 0.9.5本地验收（2026-10-15）

编号恢复原始小圆尺寸，实测约31.24×31.24，亮度白边保留，X仍48×40。悬浮窗口UI和泛光共用一个100ms进度，白芯由2改为1界面单位，保留光晕；仅暂存切换维持1.35倍。定位并修复原生显示后层次覆盖使光条被遮挡的问题，取消的旧动画不会清除新场景。

用户确认：双击任务卡片只打开或复用任务；列表空白或标题双击才关闭最上层悬浮窗口。其他应用点击整组收起及同窗口同输入恢复保持。编辑窗口错位原26/0，试用52/44后，按最终指定改为横向32、纵向32；屏幕边缘限制及已有保存位置保留。

- 82项控件/手势、111项冷开/正常开关/整组恢复/快速反向、8项多暂存单白条检查通过。45份场景诊断无动画回退或错误。
- 真实鼠标6项卡片、空白、标题及两编辑窗口测试通过，抽牌/透明局部补口/球展开采样通过。上述桌面报告在最终32/32参数微调前完成，动画和命中实现未再改；最终参数通过源码核对及publish构建，未声称32/32重新实机测量。
- 122项核心规则复用旧报告，核心DLL哈希相同，本轮未重跑。失败诊断证据保留，有限采样不保证所有设备负载下零闪烁。

完整便携入口：[即时任务看板.exe](../$($build.Entry))；请从托盘退出旧版再启动，保留整个目录。ZIP为$($build.Archive)，共$($build.FileCount)文件逐一核验，附SHA256，不需另装.NET。普通本地修改未更新GitHub，正式数据及用户进程未操作。

证据：[验证摘要](VERIFICATION-0.9.5.json)、[构建清单](BUILD-INFO-0.9.5-standard.json)、[索引刷新](INDEX-STATUS.md)。
"@
[IO.File]::WriteAllText((Join-Path $repository 'docs/ACCEPTANCE-0.9.5.md'), $acceptance+[Environment]::NewLine, [Text.UTF8Encoding]::new($false))
[ordered]@{ Version=$result.Version; Compact=82; Scroll=111; SingleFlare=8; PhysicalGestures=6; Offset='32/32'; Files=$build.FileCount; ArchiveSHA256=$build.ArchiveSHA256 } | ConvertTo-Json
