param()
$ErrorActionPreference='Stop'
$taskRepository=Split-Path -Parent $PSScriptRoot
$taskReports=@(
 @{Kind='core';Path='test-output/ux097-delivery-rules-final/report.json';Expected=151},
 @{Kind='stage-ui';Path='test-output/ux097-delivery-stage-final/stage-report.json';Expected=31},
 @{Kind='compact-ui';Path='test-output/ux097-delivery-compact-final/compact-report.json';Expected=82},
 @{Kind='migration-settings';Path='test-output/ux097-delivery-integration-final/integration-report.json';Expected=17}
)
$taskResults=@(foreach($taskReport in $taskReports){
 $taskReportPath=Join-Path $taskRepository $taskReport.Path
 $taskReceipt=Get-Content -LiteralPath $taskReportPath -Raw | ConvertFrom-Json
 if($taskReceipt.passed -ne $taskReport.Expected){throw ('检查数量不匹配：'+$taskReport.Path)}
 @{Kind=$taskReport.Kind;Report=$taskReport.Path;Passed=$taskReceipt.passed;SHA256=(Get-FileHash -LiteralPath $taskReportPath -Algorithm SHA256).Hash}
})
$taskDelivery=Join-Path $taskRepository 'release/即时任务看板-0.9.7-final'
$taskSources=@(Get-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux097-source-scope.json') -Raw | ConvertFrom-Json)
$taskSources=@($taskSources + 'tools/Write-UX097-Delivery.ps1' | Sort-Object -Unique)
$taskSources | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux097-source-scope.json') -Encoding utf8
$taskVerification=[ordered]@{
 Version='0.9.7';Date='2026-10-17';LocalOnly=$true;ProductionDataChanged=$false;UserProcessesStopped=$false;StartupConfigurationChanged=$false
 Entry='release/即时任务看板-0.9.7-final/即时任务看板.exe';PortableManifest='docs/BUILD-INFO-0.9.7-local.json'
 DraftSwitchSpeed=1.35;OtherAnimationDurationMs=100;MinimumRunningStages=1
 Passed=($taskResults.Passed | Measure-Object -Sum).Sum;Reports=$taskResults
 RuntimeHashes=@(foreach($taskName in @('Taskboard.dll','Taskboard.Core.dll','即时任务看板.exe')){@{Path=$taskName;SHA256=(Get-FileHash -LiteralPath (Join-Path $taskDelivery $taskName) -Algorithm SHA256).Hash}})
 CoreTestUsesPublishedCore=$true
 OffscreenUI=@{MouseMoved=$false;KeyboardInjected=$false;ForegroundActivationSuppressed=$true;LastRunningStageHintsRequested=2;RealHintPopupShown=$false}
 NativeHotkeyLifecycle=@{ConflictPreservesPrevious=$true;ReplacementReleasesPrevious=$true;DisableReleases=$true;DisposeReleases=$true;InputInjected=$false}
 DesktopAttempt=@{
  UserAuthorized=$true;Directory='test-output/ux097-desktop-input-07';Accepted=$false;DefaultCtrlTildeRegistered=$true
  ForegroundPreconditionPassed=$false;EnglishHotkeyTriggered=$false;ChineseIMETriggered=$false;ActualClickAssertionsReached=$false;ActualHintVisualVerified=$false
  KeyboardInputReached=$false;MouseFocusAttempted=$true;TestProcessesExited=$true;UserToldToResume=$true
  Reason='隔离输入窗口未取得实际前台，前提断言失败。并非功能断言失败；不能据此认定实际快捷键、中文输入法或真实点击通过。'
  ReceiptCaveat='开发尝试报表旧keyboardInjected/mouseMoved字段为预设能力标识；执行在PressHotkey之前中止，未注入快捷键。最终测试代码已改为实际调用追踪，未再占用用户桌面重跑。'
 }
 Boundaries=@('未操作第五人格或崩坏三；旧版全屏模拟、Alt+Tab结果未冒充本轮重测。','未新建Codex聊天、发送消息或批准；现有桌面聊天实时桥接仍待专项确认。','未修改正式任务、配置、自启入口或停止用户旧版程序。','普通本地修改未上传GitHub。')
 Index=@{Project='taskboard';Receipt='test-output/index-097-final-status.json';Coverage='test-output/index-097-final-coverage.json';FreshnessCaveat='metadata_changed；指定源码读取及便携构建来源SHA256补证，不宣称图确认源码新鲜。'}
}
$taskVerification | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $taskRepository 'docs/VERIFICATION-0.9.7.json') -Encoding utf8
$taskAcceptance=@'
# 0.9.7本地验收记录

2026-10-17。当前入口`release/即时任务看板-0.9.7-final/即时任务看板.exe`，完整ZIP和SHA256见BUILD-INFO-0.9.7-local.json。普通修改未上传GitHub；上一公开0.9.5，旧运行目录保留。

## 已完成的本地修改

- PRD31 U102至U116、TASKS19 T128至T139：编辑任务/设置入口重复点击开关，未提交副本保留；物理双击只算一次开关。
- 列表每次新建独立卡片并预选本列，恢复共享池，旧草稿状态及全部字段不覆盖；仍只以名称或详情文字触发暂存。内部新建无状态，保存前需选择。
- 保存/删除高度40，与X一致。按阶段独立会话关联，右侧关联按钮宽1.6倍；旧绑定无损迁移、逐字段合并和旧通知身份保护。
- 每阶段运行中/暂无进程，允许多个运行但至少一个；编辑器图标加文字、列表仅图标。多阶段任意列表只显示运行阶段。点击最后一个时保留状态，气泡约2秒消失；提交、合并、读取、撤销也保护。
- 只有运行阶段的可信会话驱动流转，等待回答/批准/检查优先待操作；切换看板运行标记不向Codex发送启动/暂停。
- 灵感详情圆角最少两行，圆按钮按保存情况转新建/编辑并保留末字、无重复任务；主设置可关闭快捷卡片。
- 主设置可配置Ctrl+~，物理按键识别，像球一样切换展开/收起并保留窗口；冲突保留旧组合，替换、禁用和退出释放注册。
- 球和托盘第一项为原有动态隐藏/恢复悬浮球开关，第二设置；没有新增恢复位置功能。
- 原100ms悬浮开合、1.35倍暂存切换、前2/3忽略点击、32/32错位及透明度保持。

## 已通过的检查

最终运行目录的Core DLL交给规则检查程序执行，151项通过；屏幕外阶段/录入/设置/原生快捷键生命周期31项、控件及旧交互回归82项、迁移/设置17项，共281项。结果及报告SHA256见VERIFICATION-0.9.7.json。

屏幕外检查不移动鼠标、不注入键盘、不激活真实前台；末个运行阶段检查确认状态保持和两次提示请求，未显示真实气泡。布局离屏图已查看，不能当作实际桌面截图。原生注册检查确认冲突、替换、禁用与退出，不等于键盘触发已验收。

## 本轮桌面测试限制

用户起初不方便，后于本轮明确允许暂停桌面操作。已启动非正式目录的真实测试；默认Ctrl+~系统注册成功，但隔离输入窗口未取得前台，严格前提检查中止，未进入快捷键注入和后续鼠标断言。英文触发、中文IME实际触发、真实重复点击及气泡画面均未验收，不能写成通过，也不能认定为产品功能失败。测试窗口、子进程与注册已退出，执行鼠标和前台恢复并通知用户可恢复操作。

开发尝试01至07回执保留。早期执行策略、无BOM编码与辅助窗口显示问题已修正；最终仍有前台限制。旧报表keyboardInjected字段为预设标识，实际未到PressHotkey；最终代码改为实际调用追踪，停止占用桌面后未再重跑。测试所在桌面与输入桌面均Default、同会话，不能把问题归因于已证实的不同桌面隔离。

未操作正式任务、配置、preserved-data、启动配置或用户旧版进程；Codex实时桥接、真实游戏及真实通知音替换仍为原有边界。后续真实输入验证须重新协调，不沿用已结束的暂停时段。

## 完整包及索引

便携包是完整自包含Windows x64目录，非单exe补丁。Package-Portable逐文件核对ZIP内容、长度和SHA256，输出完整ZIP及校验文件；来源仅显式相关路径，见构建清单。发布目录不含测试/正式数据。

Codebase Memory实际连接并执行状态/架构/图定位，ADR六章更新、full持久化。覆盖仍报metadata_changed，直接源码、最终运行DLL及来源哈希补证；路径、计数及解析缺口见INDEX-STATUS及回执，不声称索引证明新鲜完整。
'@
[IO.File]::WriteAllText((Join-Path $taskRepository 'docs/ACCEPTANCE-0.9.7.md'),$taskAcceptance,[Text.UTF8Encoding]::new($false))
@{Passed=$taskVerification.Passed;Reports=$taskResults.Count;SourceScope=$taskSources.Count;DesktopAccepted=$false} | ConvertTo-Json
