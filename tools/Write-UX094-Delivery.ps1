$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repository
function Read-Report([string]$Path) {
    $folder = Split-Path -Parent $Path
    if (Test-Path -LiteralPath (Join-Path $folder 'startup-error.txt')) { throw ('验证存在错误：' + $folder) }
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}
function Evidence([string]$Path) { [ordered]@{ Path=$Path; SHA256=(Get-FileHash -LiteralPath $Path).Hash } }
$build = Get-Content -LiteralPath 'docs/BUILD-INFO-0.9.4-standard.json' -Raw | ConvertFrom-Json
if ($build.Version -ne '0.9.4' -or $build.DraftSwitchSpeed -ne 1.35 -or $build.OtherAnimationDurationMs -ne 100 -or -not $build.ZipAllFilesVerified -or $build.ContainsData) { throw '运行包参数错误。' }
$folder = Split-Path -Parent $build.Entry
foreach ($file in $build.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $folder $file.Path)).Hash -ne $file.SHA256) { throw ('运行文件变化：'+$file.Path) }
}
foreach ($file in $build.Sources) {
    if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.SHA256) { throw ('来源文件变化：'+$file.Path) }
}
if ((Get-FileHash -LiteralPath $build.Archive).Hash -ne $build.ArchiveSHA256) { throw '压缩包变化。' }
$compactPath = 'test-output/ux-094-compact-final-02/compact-report.json'
$scrollPath = 'test-output/ux-094-scroll-runtime-01/scroll-report.json'
$stackPath = 'test-output/ux-094-stack-runtime-01/scroll-report.json'
$observerPath = 'test-output/outside-observer-094-final-01/observer-report.json'
$outsidePath = 'test-output/outside-click-094-01/outside-click-report.json'
$sharedPath = 'test-output/ux-094-shared-runtime-01/shared-motion-report.json'
$compact = Read-Report $compactPath; $scroll = Read-Report $scrollPath; $stack = Read-Report $stackPath
$observer = Read-Report $observerPath; $outside = Read-Report $outsidePath; $shared = Read-Report $sharedPath
if ($compact.passed -ne 80 -or $compact.switchSpeed -ne 1.35 -or $scroll.passed -ne 66 -or $scroll.durationMs -ne 100 -or $stack.passed -ne 8 -or $observer.passed -ne 4 -or -not $shared.passed) { throw '最终回归未全部通过。' }
foreach ($key in @('firstVisible','slowVisible','differentVisible','doubleClosed','sharedHost','settingsSameHost','settingsSingleVisible','settingsDoubleClosed','draftCloseKeepsLists','settingsCloseKeepsLists','freshOutsideCollapses','draftExternalCollapsed','draftGroupRestored','draftRestoredValue','settingsExternalCollapsed','settingsGroupRestored')) { if (-not $outside.$key) { throw ('真实交互失败：'+$key) } }
$coreHash = (Get-FileHash -LiteralPath (Join-Path $folder 'Taskboard.Core.dll')).Hash
$priorCoreHash = (Get-FileHash -LiteralPath 'release/即时任务看板-0.9.2-final/Taskboard.Core.dll').Hash
$rules = Read-Report 'test-output/ux-092-rules-02/report.json'
if ($coreHash -ne $priorCoreHash -or $rules.passed -ne 122) { throw '复用规则依据不符。' }
$result = [ordered]@{
    Version='0.9.4'; Date='2026-10-15'; LocalOnly=$true; Requirements='PRD U86-U90 / A104-A108 / T109-T114'
    Entry=$build.Entry; Archive=$build.Archive; ArchiveSHA256=$build.ArchiveSHA256; FileCount=$build.FileCount; ZipAllFilesVerified=$true
    RuntimeSHA256=(Get-FileHash -LiteralPath (Join-Path $folder 'Taskboard.dll')).Hash
    DraftSwitchSpeed=1.35; DraftSwitchDurationMs=360/1.35; OtherAnimationDurationMs=100
    Compact=[ordered]@{ Passed=80; Report=(Evidence $compactPath); IncludesPhysicalTimeAndLateDelivery=$true }
    RepeatedScroll=[ordered]@{ Passed=66; Report=(Evidence $scrollPath); Types=3; CompleteCyclesPerType=4; QuickReopensPerType=3 }
    MultiDraftSingleFlare=[ordered]@{ Passed=8; Report=(Evidence $stackPath); ReproducedMultipleFlares=$false; UserAllowedSkip=$true; SeparateDuplicateFlareFix=$false }
    BusyOutsideClick=[ordered]@{ Passed=4; Report=(Evidence $observerPath); BlockedUiMs=1700; TestedFinalRuntime=$true }
    OutsidePolicy=[ordered]@{ Report=(Evidence $outsidePath); TestedBeforeFinalClickGateRefinement=$true; PhysicalRuleImplementationUnchanged=$true; MaxBackgroundDifference=$outside.firstOutsideMaxColorDifference }
    SharedMotion=[ordered]@{ Passed=$shared.passed; Report=(Evidence $sharedPath); DurationMs=$shared.durationMs }
    CoreRules=[ordered]@{ Passed=122; Reused=$true; Rerun=$false; CoreSHA256=$coreHash; PriorCoreSHA256=$priorCoreHash; Report=(Evidence 'test-output/ux-092-rules-02/report.json') }
    Boundaries=@('所有测试隔离--data-dir','真实鼠标已协调并恢复','不改正式数据或启动登记','未退出用户进程','未上传GitHub','有限桌面采样不承诺任意硬件负载下零闪烁')
    MemoryRecord='docs/INDEX-STATUS.md'
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath 'docs/VERIFICATION-0.9.4.json' -Encoding utf8
$acceptance = @"
# 0.9.4本地验收（2026-10-15）

已选定1.35倍作为常用暂存切换速度，约266.67ms；其他完整开合及快速反向均100ms。编号恢复40×40圆形，保留白边亮度；X48×40。切换准备期和前2/3点击忽略，最后1/3只登记一次后续切换，交接结束执行，原先过早事件延迟到达也无效。

重复重开缺白条的路径为旧进度复用及剩余距离缩短时间。现用独立本次泛光时间线，开合固定100ms，首尾白芯保留。外部点击不收起属于检测失效路径，不是最新产品规则；鼠标监听脱离界面和同步命中检测，界面繁忙后仍有效。其他应用点击收起整组，恢复同一悬浮窗口及输入；看板内部窗口外单击保持，双击关闭最上层悬浮窗口。

- 80项控件及手势检查通过，覆盖圆形编号、前2/3忽略、后1/3仅一次、原始按下时刻及迟到投递。
- 66项重复开合通过：新建/编辑/设置各4轮完整整组收起恢复、3轮快速重开，每次重新展开均有白条。
- 三张暂存各4轮开合、8项单白条采样通过；未重现多条白条，按用户补充不作专项修改。原先25ms漏采失败记录保留。
- 最终运行包的4项繁忙鼠标检查通过：故意阻塞隔离界面1700ms，期间和恢复后的点击均能收起，再展开恢复同一窗口及输入。
- 整套真实内部单双击、重复打开、其他应用整组收起/恢复已通过。该轮在最后切换时间过滤细化前执行；外点处理实现保持相同，最终包另外通过繁忙真实点击。单击/重复打开背景采样差值为0。
- 最终包的抽牌、交接、透明局部补口及球展开采样通过，详见验证摘要。122项核心规则复用0.9.2原报告：核心DLL SHA256完全相同，本轮未声称重跑。

完整便携运行入口：[即时任务看板.exe](../$($build.Entry))。从托盘退出旧版，再启动新版，保留整个目录；正式数据沿用原保存位置，旧包保留。完整ZIP为$($build.Archive)，$($build.FileCount)文件逐一核验，附SHA256；不需另装.NET。普通本地修改，未更新GitHub。

证据：[验证摘要](VERIFICATION-0.9.4.json)、[构建清单](BUILD-INFO-0.9.4-standard.json)、[索引刷新](INDEX-STATUS.md)。有限实机采样不保证所有硬件及负载下恒定帧率；本轮不操作正式数据、用户运行进程或真实通知/自启配置。
"@
[IO.File]::WriteAllText((Join-Path $repository 'docs/ACCEPTANCE-0.9.4.md'), $acceptance + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
[ordered]@{ Version=$result.Version; Compact=80; RepeatedScroll=66; SingleFlare=8; BusyClicks=4; FileCount=$result.FileCount; ArchiveSHA256=$result.ArchiveSHA256 } | ConvertTo-Json
