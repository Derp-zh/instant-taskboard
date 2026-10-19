param()
$ErrorActionPreference='Stop'
$taskRepository=Split-Path -Parent $PSScriptRoot
function Set-TaskPrelude([string]$File,[string]$Marker,[string]$Prelude){
 $taskPath=Join-Path $taskRepository $File; $taskText=[IO.File]::ReadAllText($taskPath); $taskIndex=$taskText.IndexOf($Marker,[StringComparison]::Ordinal)
 if($taskIndex -lt 0){throw ('文档边界缺失：'+$File)}
 [IO.File]::WriteAllText($taskPath,$Prelude.TrimEnd()+[Environment]::NewLine+[Environment]::NewLine+$taskText.Substring($taskIndex),[Text.UTF8Encoding]::new($false))
}
$taskReports=@(
 @{Kind='core';Path='test-output/ux098-final-core/report.json';Expected=171},
 @{Kind='features';Path='test-output/ux098-verified-ux098/ux098-report.json';Expected=32},
 @{Kind='stage-regression';Path='test-output/ux098-verified-stage/stage-report.json';Expected=31},
 @{Kind='compact-regression';Path='test-output/ux098-verified-compact/compact-report.json';Expected=82},
 @{Kind='integration';Path='test-output/ux098-verified-integration/integration-report.json';Expected=17}
)
$taskResults=@(foreach($taskReport in $taskReports){
 $taskPath=Join-Path $taskRepository $taskReport.Path; $taskResult=Get-Content -LiteralPath $taskPath -Raw | ConvertFrom-Json
 if($taskResult.passed -ne $taskReport.Expected){throw ('检查不完整：'+$taskReport.Path)}
 @{Kind=$taskReport.Kind;Report=$taskReport.Path;Passed=$taskResult.passed;SHA256=(Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash}
})
$taskRuntime=Join-Path $taskRepository 'release/即时任务看板-0.9.8-final'
if((Get-FileHash -LiteralPath (Join-Path $taskRuntime 'Taskboard.Core.dll')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskRepository 'test-output/ux098-exact-core-runner/Taskboard.Core.dll')).Hash){throw '最终Core与规则检查使用DLL不一致'}
$taskFeatures=Get-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux098-verified-ux098/ux098-report.json') -Raw | ConvertFrom-Json
$taskVerification=[ordered]@{
 Version='0.9.8';Date='2026-10-18';LocalOnly=$true;Entry='release/即时任务看板-0.9.8-final/即时任务看板.exe'
 Passed=($taskResults.Passed | Measure-Object -Sum).Sum;Reports=$taskResults
 RuntimeHashes=@(foreach($taskName in @('Taskboard.dll','Taskboard.Core.dll','即时任务看板.exe')){@{Path=$taskName;SHA256=(Get-FileHash -LiteralPath (Join-Path $taskRuntime $taskName) -Algorithm SHA256).Hash}})
 CoreTestUsesPublishedCore=$true;MouseMoved=$false;KeyboardInjected=$false;DesktopVisualVerified=$false;RealCodexCatalogVerified=$false
 ProductionDataChanged=$false;StartupConfigurationChanged=$false;UserProcessesStopped=$false
 DraftSwitchSpeed=1.35;OtherAnimationDurationMs=100;CardCollapseDurationMs=180;ObservedOffscreenStageCollapseMs=$taskFeatures.stageCollapseMs
 WorkspaceBarrierLogicVerified=$true;WorkspaceBatchMembershipVerified=$true;ActualDesktopStartFrameVerified=$false
 Defaults=@{AutoArchiveCompleted=$true;AutoArchiveAfterDays=5;AutoDeleteArchives=$false;ArchiveRetentionWhenEnabled=40;InitialArchivePreviews=20}
 CodebaseMemory=@{Connected=$false;Attempted=@('index_status','get_architecture','search_graph');Error='Transport closed';GraphRefreshed=$false;Fallback='指定源码/rg与显式相关来源哈希';IndexNotes='docs/INDEX-STATUS.md'}
 Boundaries=@('所有检查使用新隔离目录；真实鼠标观感和实际Codex目录连接本轮未验收。','会话协议名称和游标字段查对官方源码，不等于本机连线已验收。','没有使用此前已经结束的桌面暂停许可。','普通本地修改未上传GitHub，旧包与正式数据保留。')
}
$taskVerification | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $taskRepository 'docs/VERIFICATION-0.9.8.json') -Encoding utf8
$taskScope=@(Get-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux097-source-scope.json') -Raw | ConvertFrom-Json)
$taskScope=@($taskScope | Where-Object {$_ -notin @('tools/Write-UX095-Delivery.ps1','tools/Write-UX097-Delivery.ps1')})
$taskScope=@($taskScope + @('src/Taskboard.Desktop/StageConversationPicker.cs','src/Taskboard.Desktop/BoardController.Conversations.cs','src/Taskboard.Desktop/WorkspaceRevealBatch.cs','src/Taskboard.Desktop/BoardController.CardPresentation.cs','src/Taskboard.Desktop/BoardController.Archives.cs','src/Taskboard.Desktop/TaskCardMotion.cs','src/Taskboard.Desktop/SettingsArchive.cs','src/Taskboard.Desktop/Ux098Checks.cs','tests/Taskboard.Checks/ArchivePolicyRules.cs','tests/Taskboard.Checks/IntegrationRules.cs','tools/Write-UX098-Delivery.ps1') | Sort-Object -Unique)
$taskScope | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux098-source-scope.json') -Encoding utf8
$taskReadme=@'
# 即时任务看板

当前本地试用0.9.8：运行中改为静态暂停符号，暂停阶段先变暗暂留，后续人为任务操作统一向上收起；删除/归档整卡同样收起。阶段会话按名称搜索选择、气泡显示名称；列表与已有悬浮窗口共同准备后展开。标题下新增青色分隔线。

自动归档默认开启、默认5天，天数可设置；编辑不重新计时。归档先显示20条，可加载全部；自动删除较早归档默认关闭，开启时保留最近40条。

本地入口：[即时任务看板.exe](release/即时任务看板-0.9.8-final/即时任务看板.exe)，[完整便携ZIP](release/即时任务看板-0.9.8-local-win-x64.zip)。试用前先从托盘退出旧版，保留整个目录和原数据路径。333项隔离检查通过；本轮未移动鼠标，真实桌面观感与实际会话目录连线未验收。Codebase Memory实际断连，源码及构建哈希补证。普通修改未上传GitHub，上一公开版本0.9.5。

见[本轮验收](docs/ACCEPTANCE-0.9.8.md)、[验证摘要](docs/VERIFICATION-0.9.8.json)及[使用说明](docs/USER-GUIDE.md)。
'@
Set-TaskPrelude 'README.md' 'Windows桌面任务看板' $taskReadme
$taskReadmePath=Join-Path $taskRepository 'README.md'; $taskText=[IO.File]::ReadAllText($taskReadmePath).Replace('已完成、暂搁置任务可归档，保存最近20条完整任务，并可取消归档。','已完成、暂搁置任务可手动归档；自动归档天数可设置，先显示20条、可加载全部，自动保留40条可选。'); [IO.File]::WriteAllText($taskReadmePath,$taskText,[Text.UTF8Encoding]::new($false))
$taskGuide=@'
# 0.9.8本地修订说明

先从旧版托盘退出，再运行`release/即时任务看板-0.9.8-final/即时任务看板.exe`，保留完整目录和原任务数据路径。本轮未上传GitHub。

- 运行中使用两个竖条的暂停符号；新建/编辑窗口的暂无进程三角形减亮。每任务仍至少保留一个运行阶段。
- 列表点击运行阶段改为暂无进程时，先变暗保留；再切回运行中恢复亮度。之后新建、打开/关闭任务编辑、保存、拖动、删除、归档、取消归档、撤销/恢复等明确人为任务操作会统一收起所有暂留阶段。后台消息、自动状态变化及输入暂存不触发。下次球/快捷键展开也会整理，重启后不恢复暂留显示。
- 阶段隐藏、删除任务和归档整卡采用约180ms从下向上收起，没有白条。自动归档仅收起自己的整卡，不整理其他暂停阶段；不为动画滚动用户列表。
- 会话气泡显示名称。点击后在输入框直接搜索或选择已有会话，结果显示名称和编号，只按名称匹配。输入搜索不改变关联，选中才关联，另有解除按钮；保存任务后正式生效。没有名称时显示未命名会话。
- 列表与已开的悬浮窗口共同准备后展开，保持原窗口及输入；100ms开合和1.35倍暂存切换保持。任务名称下新增与原阶段横线同尺寸的青色分隔线。
- 设置左侧“归档”：已完成超过指定天数自动归档，默认开启、默认5天，可输入正整数天数。从进入已完成起计算，编辑不重置；离开再完成重新计时。程序运行时检查；未提交编辑的任务先保留，处理完副本后再归档。
- 归档页面先显示最近20条，点击加载更多显示全部；重新进入页面或真正重开设置恢复20条，单纯整组收起恢复保留当前页内容。
- “自动删除较早归档，只保留最近40条”默认关闭，关闭时全部保留。开启时若超过40，会说明实际永久删除数量并确认；以后归档保持最近40。普通删除不产生归档。

333项规则/界面隔离检查通过。没有移动鼠标或修改正式数据；真实桌面流畅度与实际会话目录连接本轮未验收。以下为此前版本来源，与本节冲突时以本节为准。
'@
Set-TaskPrelude 'docs/USER-GUIDE.md' '# 0.9.7本地修订说明' $taskGuide
$taskIndex=@'
# 项目文档索引

2026-10-18当前本地0.9.8：静态暂停图标、暂停阶段变暗暂留与所有任务的人为操作统一整理；阶段/删除/归档整卡向上收起；会话名称气泡与按名称搜索选择；列表/悬浮共同准备后展开；完成时间计时、可设天数、默认五天自动归档、20/全部预览、可选保留40；标题青色横线。入口PRD32 U117至U125/A135至A143、TASKS20 T140至T148、IMPLEMENTATION0.9.8，[验收](ACCEPTANCE-0.9.8.md)、[验证摘要](VERIFICATION-0.9.8.json)、[完整包](BUILD-INFO-0.9.8-local.json)。333项隔离检查通过，本轮无真实鼠标和实际会话目录连线验收。Codebase Memory实际Transport closed，未刷新图，指定源码补证。普通本地修改未上传GitHub，上一公开0.9.5。下列为历史交付。
'@
Set-TaskPrelude 'docs/INDEX.md' '2026-10-17本轮0.9.7' $taskIndex
$taskHandoff=@'
# 新会话接手说明

更新时间：2026-10-18。最新用户指令及PRD32优先，以下旧版本记录只作来源。

## 当前状态

- 当前本地0.9.8，入口`release/即时任务看板-0.9.8-final/即时任务看板.exe`；PRD32 U117至U125/A135至A143，TASKS20 T140至T148。完整ZIP/SHA256与源码哈希见BUILD-INFO-0.9.8-local，验收及回执见ACCEPTANCE-0.9.8和VERIFICATION-0.9.8。普通修改未上传，上一公开0.9.5，旧包保留。
- 运行中静态双竖条，编辑器三角单独减亮。列表暂停阶段先以45%亮度暂留，阶段切换不立即消失；所有任务的明确人为操作及下次球/快捷键展开统一整理。后台消息、自动状态流转、输入自动暂存不整理。至少一个运行阶段保护保持；暂留集合不持久化。
- 阶段和删除/归档整卡180ms向上裁切，无白条；自动归档只收起自身，不清理其他暂留。只取已实现的卡片，不滚动列表；动画后再正式删除/归档，异常恢复。
- 会话气泡直接名称，输入框点击打开搜索下拉，名称匹配不查编号；结果/提示名称+编号，选择才改关联。ThreadName持久化及三方合并；目录按游标分页、短期缓存与虚拟化，不新增聊天或发送。
- 列表/悬浮窗口用共同准备屏障再启动动画，100ms原开合、1.35倍暂存切换保持；输入/位置/层次保留，整组恢复不刷新设置当前内容。标题下增加同尺寸青色横线。
- 自动归档默认开启5天，可设整数天数，从进入已完成计时，编辑不重置、重新完成重新计时；未提交编辑先跳过。默认先显示20归档，加载更多全部；默认不自动淘汰，开启时保留最近40且当前超量会说明永久删除数量并确认。
- 最终333项隔离检查（171规则、32新界面/动画、31阶段录入、82控件、17迁移）通过，未移动鼠标、注入键盘或操作正式数据/启动配置/用户进程。真实桌面观感及实际Codex目录连接本轮未验收，不沿用前轮已结束的暂停许可。
- Codebase Memory本轮实际index_status/get_architecture/search_graph均Transport closed；未更新ADR/图，不能把旧ready计数说成新鲜。指定源码与构建哈希补证，重连后再检查覆盖并刷新，详见INDEX-STATUS。
- 0.9.7共享草稿、每次列表新建预选本列、全字段暂存、编辑/设置入口开关、灵感两行与转完整窗口、可配置Ctrl+~、原球显隐菜单第一/设置第二均保持。0.9.6全屏输入/Alt+Tab/居中与32/32/恢复布局沿用，不冒充本轮重测。
'@
Set-TaskPrelude 'docs/HANDOFF.md' '## 阅读顺序' $taskHandoff
$taskHandoffPath=Join-Path $taskRepository 'docs/HANDOFF.md'; $taskText=[IO.File]::ReadAllText($taskHandoffPath)
$taskText=$taskText.Replace('[0.9.7验收](ACCEPTANCE-0.9.7.md)、[验证摘要](VERIFICATION-0.9.7.json)和[本地包清单](BUILD-INFO-0.9.7-local.json)','[0.9.8验收](ACCEPTANCE-0.9.8.md)、[验证摘要](VERIFICATION-0.9.8.json)和[本地包清单](BUILD-INFO-0.9.8-local.json)')
$taskText=$taskText.Replace('历史0.9.3收尾断连在0.9.4已恢复，ADR及full持久化成功。本轮开始实际ready为4662节点/12563边、7个PowerShell部分解析，根目录正确；路径新鲜度仍报metadata_changed，按指定源码补证。本轮最终刷新及覆盖回执见INDEX-STATUS.md，不将旧计数冒充新计数。','最近成功图为0.9.7的历史回执，见INDEX-STATUS。2026-10-18实际工具Transport closed，未更新图或ADR，不将旧计数或旧metadata_changed结果冒充本轮索引检查通过。')
[IO.File]::WriteAllText($taskHandoffPath,$taskText,[Text.UTF8Encoding]::new($false))
$taskAcceptance=@'
# 0.9.8本地验收

2026-10-18。PRD32 U117至U125/A135至A143、TASKS20 T140至T148。入口`release/即时任务看板-0.9.8-final/即时任务看板.exe`，完整便携ZIP和SHA256见BUILD-INFO-0.9.8-local。普通修改未上传GitHub，旧包、正式数据、配置与用户进程保留。

## 已完成

静态暂停双竖条、编辑器三角减亮；列表阶段暂停后变暗暂留并可恢复；所有任务的人为操作统一收起，后台通知/自动状态流转不整理。阶段切换本身不触发整理，最少一运行保护保持。重开看板会整理，重启不恢复暂留缓存。阶段、删除、归档整卡均180ms向上裁切，无白条、不滚动用户列表。

阶段会话气泡为名称，点击输入框搜索/选择，只查名称不查编号，结果和提示含名称+编号；搜索不改关联，选择/解除才改。名称持久化、合并保留后台身份与状态，目录分页与短期缓存、虚拟化。只使用原有只读功能，不新增消息或连接声明。

列表及已有悬浮窗口共同准备屏障，避免列表先播完；异常/超时释放，保留输入及设置页已加载内容。开合时长、暂存切换速度不改。任务名称下新增与原阶段横线同宽高的青色横线。

自动归档默认开启5天且天数可设；完成计时独立、编辑不重置、离开/重新完成重计。超过整天数才触发，未提交编辑先保留。归档只先显示20、加载更多全部；默认不按数量淘汰，开启时保留最近40，当前超量说明实际永久删除数量并确认。

## 检查与边界

最终Core DLL交给规则检查程序执行，171项通过；新界面/动画32项、阶段录入回归31项、控件回归82项、迁移/设置17项，共333项。报告路径及哈希见VERIFICATION-0.9.8。涵盖全局人为整理、后台不整理、重开整理、删除/归档顺序、45条归档20/全部显示、设置整组恢复保留、自定义天数、默认关闭40淘汰、完成计时边界与未提交编辑保护。离屏渲染图已查看。

检查均使用新隔离数据，不移动鼠标、不注入键盘、不改变真实前台。窗口共同批次及屏障逻辑通过，但离屏原生显示不等于实际屏幕开始帧/流畅度；本轮未做真实桌面观感或实际Codex目录连线验收，不沿用前轮暂停时段。协议字段查对官方源码也不等于本机连线测试。此前失败开发回执保留；固定等待时间检查改为等待实际状态完成，仍有明确超时，未删除需求断言。

完整包是自包含win-x64全目录，不是单exe补丁；ZIP逐文件核对内容、长度及SHA256，来源为显式相关文件。Codebase Memory实际Transport closed，未伪称启用、更新或重建；相关源码与构建哈希补证，重连后再刷新图，见INDEX-STATUS。
'@
[IO.File]::WriteAllText((Join-Path $taskRepository 'docs/ACCEPTANCE-0.9.8.md'),$taskAcceptance,[Text.UTF8Encoding]::new($false))
@{Passed=$taskVerification.Passed;SourceScope=$taskScope.Count;CodebaseConnected=$false;DesktopVerified=$false} | ConvertTo-Json
