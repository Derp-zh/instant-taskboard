param()
$ErrorActionPreference='Stop'
$taskRepository=Split-Path -Parent $PSScriptRoot
function Write-TaskText([string]$Path,[string]$Text){[IO.File]::WriteAllText((Join-Path $taskRepository $Path),$Text.TrimEnd()+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))}
function Set-TaskPrelude([string]$Path,[string]$Marker,[string]$Text){
 $current=[IO.File]::ReadAllText((Join-Path $taskRepository $Path));$at=$current.IndexOf($Marker,[StringComparison]::Ordinal)
 if($at -lt 0){throw ('文档边界缺失：'+$Path)}
 Write-TaskText $Path ($Text.TrimEnd()+[Environment]::NewLine+[Environment]::NewLine+$current.Substring($at))
}
$taskReports=@(
 @{Kind='core';Path='test-output/ux0910-final-core/report.json';Expected=171},
 @{Kind='hint-layout';Path='test-output/ux0910-final-ux0910/ux0910-report.json';Expected=18},
 @{Kind='ux099';Path='test-output/ux0910-final-ux099/ux099-report.json';Expected=30},
 @{Kind='ux098';Path='test-output/ux0910-final-ux098/ux098-report.json';Expected=32},
 @{Kind='stage';Path='test-output/ux0910-final-stage/stage-report.json';Expected=31},
 @{Kind='compact';Path='test-output/ux0910-final-compact/compact-report.json';Expected=82},
 @{Kind='integration';Path='test-output/ux0910-final-integration/integration-report.json';Expected=17}
)
$taskResults=@(foreach($item in $taskReports){
 $path=Join-Path $taskRepository $item.Path;$report=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
 if($report.passed -ne $item.Expected){throw ('检查不完整：'+$item.Path)}
 @{Kind=$item.Kind;Report=$item.Path;Passed=$report.passed;SHA256=(Get-FileHash -LiteralPath $path).Hash}
})
$taskRuntime=Join-Path $taskRepository 'release/即时任务看板-0.9.10-final'
if((Get-FileHash -LiteralPath (Join-Path $taskRuntime 'Taskboard.Core.dll')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskRepository 'test-output/ux0910-exact-core-runner/Taskboard.Core.dll')).Hash){throw '规则测试未使用最终Core'}
$taskLayout=Get-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux0910-final-ux0910/ux0910-report.json') -Raw | ConvertFrom-Json
$taskVerification=[ordered]@{
 Version='0.9.10';Date='2026-10-18';LocalOnly=$true;Entry='release/即时任务看板-0.9.10-final/即时任务看板.exe'
 Passed=($taskResults.Passed | Measure-Object -Sum).Sum;Reports=$taskResults;CoreTestUsesPublishedCore=$true
 RuntimeHashes=@(foreach($name in @('Taskboard.dll','Taskboard.Core.dll','即时任务看板.exe')){@{Path=$name;SHA256=(Get-FileHash -LiteralPath (Join-Path $taskRuntime $name)).Hash}})
 LegacyHintFailureReproduced=$taskLayout.legacyNullWindowReproduced;HintNativeDisplayVerified=$taskLayout.hintDisplayed;HintSingleBorderVerified=$true;HintTimeoutMs=1200;SystemTooltipFadeDisabled=$true
 ObservedGapAbove=$taskLayout.gapAbove;ObservedGapBelow=$taskLayout.gapBelow;Screenshots=@('test-output/ux0910-final-ux0910/hint-bubble.png','test-output/ux0910-final-ux0910/compact-name-spacing.png')
 MouseMoved=$false;KeyboardInjected=$false;ProductionDataChanged=$false;StartupConfigurationChanged=$false;UserProcessesStopped=$false
 CodebaseMemory=@{Connected=$false;InitialConnectionRestored=$true;RootVerified='D:/Derp/Documents/0renwumianban';InitialIndexedAt='2026-10-17T14:29:05Z';InitialSourceFreshness='metadata_changed and new files not_tracked';InitialFullRefreshSucceeded=$true;Persistence=$true;InitialRefreshNodes=5616;InitialRefreshEdges=15992;PartialFiles=8;UnusableFiles=0;FinalRefreshSucceeded=$false;FinalError='Transport closed';FinalGraphFreshnessVerified=$false;Notes='docs/INDEX-STATUS.md'}
 DataMigrationChanged=$false;RequestOneClarification='用户明确第1项为最后运行阶段提示变成错误悬浮窗口，已复现空WPF窗口引用；沿用现有数据迁移规则。'
 Boundaries=@('提示本身真实显示与消失通过，其他任务窗口位于屏幕外；全程没有鼠标或键盘注入。','此前桌面真实点击与开合观感边界保持，不冒充本轮实测。','普通本地修改未上传GitHub，上一公开0.9.5，旧包与正式数据保留。')
}
$taskVerification | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $taskRepository 'docs/VERIFICATION-0.9.10.json') -Encoding utf8
$taskScope=@(Get-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux099-source-scope.json') -Raw | ConvertFrom-Json)
$taskScope=@($taskScope | Where-Object {$_ -ne 'tools/Write-UX099-Delivery.ps1'})
$taskScope=@($taskScope + @('src/Taskboard.Desktop/Ux0910Checks.cs','src/Taskboard.Desktop/TaskCardLayout.cs','tools/Write-UX0910-Delivery.ps1') | Sort-Object -Unique)
$taskScope | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux0910-source-scope.json') -Encoding utf8

$taskPrd=@'
# 即时任务看板电脑端PRD

版本：0.9.10。更新日期：2026-10-18。第34节为本轮提示修复、名称占位和分隔留白，覆盖第33节冲突的填满剩余空间要求。用户澄清第1项实际为提示气泡异常，沿用现有数据迁移规则。暂存切换门槛仍2/3、切换1.35倍、普通开合100ms。
'@
Set-TaskPrelude 'docs/PRD.md' '## 1 ' $taskPrd
$taskSection=@'
## 34 2026-10-18 提示异常与紧凑名称（0.9.10）

用户指定版本0.9.10；第1项经补充确认是至少保留一运行阶段的提示变成悬浮窗口，不扩大为未明确的历史数据改写。

| 编号 / 验收 | 已确认规则 |
| --- | --- |
| U136 / A154 | 修复最后运行阶段提示定位抛异常而触发通用错误窗口。新建/编辑和列表均从控件所属HwndSource取得原生句柄，固定在当前屏幕工作区居中、62%高度；未连接控件安静跳过。保留单框气泡及1.2秒，关闭系统额外淡出延迟，不创建通用悬浮窗口、不改变最少一运行阶段规则 |
| U137 / A155 | 任务/阶段名称恢复原来的自然宽度和左侧对齐，不将短名称气泡拉满整行；仍受右侧名称栏/状态图标限制，只有无法完整显示时才滚动。会话名称栏六字符及原0.5秒/0.2秒循环、展开重置规则保持 |
| U138 / A156 | 青色线到首个阶段名称气泡之间增加留白，与该气泡到其下方正文实际字形上沿的距离对齐。计入字体自身留白和当前DPI，横线仍为一物理像素、布局高度0；本次仅新增明确要求的顶部留白，替代旧“任何内容站位都不改变”的冲突描述 |
'@
$taskPrdText=[IO.File]::ReadAllText((Join-Path $taskRepository 'docs/PRD.md'))
if(-not $taskPrdText.Contains('## 34 ')){Write-TaskText 'docs/PRD.md' ($taskPrdText.TrimEnd()+[Environment]::NewLine+[Environment]::NewLine+$taskSection)}
$taskTasks=@'
# 电脑端任务面板工作清单

## 22 0.9.10提示与名称占位（2026-10-18）

| 任务 | 工作 | 验收 |
| --- | --- | --- |
| T160 | 复现提示的空Window引用，改为HwndSource定位和未连接保护 | A154 |
| T161 | 单框气泡、关闭额外淡出、真实原生显示及1.2秒消失检查 | A154 |
| T162 | 恢复自然宽名称，空间不足才滚动 | A155 |
| T163 | 青线到气泡按下方正文字形间距对齐，像素线保留 | A156 |
| T164 | 381项隔离回归、完整便携包、文档和索引恢复/刷新 | A154至A156 |

以上完成。用户澄清第1项为提示异常，数据规则与迁移未改。381项检查包含一次仅显示提示气泡的原生验证，无鼠标/键盘注入；正式数据、自启、用户进程及旧包未操作，未上传GitHub。
'@
Set-TaskPrelude 'docs/TASKS.md' '## 21 ' $taskTasks
$taskImplementation=@'
# 新版电脑端实施记录

## 0.9.10提示定位与自然宽名称（2026-10-18）

对应PRD34 U136至U138/A154至A156、TASKS22 T160至T164。用户指定0.9.10，补充确认第1项实际是至少一个运行阶段的提示变成了悬浮窗口，故保留现有数据迁移行为。

实际原生隔离窗口中的编辑、列表控件属于GlassContentHost创建的子HwndSource，Window.GetWindow(anchor)为null；旧Ui.Hint使用WindowInteropHelper(null)可复现ArgumentNullException，随后全局异常处理会创建Ui.Error的SurfaceWindow。这是之前只测试提示模板而未走原生定位路径漏掉的错误。现在Ui.CreateHint读取PresentationSource/HwndSource.Handle定位屏幕，未连接或跨内容根安静跳过；OpenHint共用计时和清理。真实提示打开时关闭Popup的系统淡出，保持1.2秒而不额外延迟。

任务标题MarqueeText及阶段名Border恢复HorizontalAlignment.Left，短名称只占自然宽，长名称仍按右侧会话栏/图标约束裁切和滚动。会话六字符、首停0.5秒、末停0.2秒及展开重置保持。

TaskCardLayout.MatchTitleGap按正文上边距与首行字形上沿计算气泡下方的可见空隙，再给第一个阶段标题增加对应上边距，扣除气泡原上边距并计入一物理像素线。当前夹具上下均约5.85单位。只测首行最多128字符以避免为大段详情生成无用字形；加载后按继承字体/DPI再核对。标题线仍0布局高度、一物理像素；新增的是用户本轮要求的留白。离屏图片已查看。

最终Core DLL交给规则检查程序执行171项；本轮18项（包含真实原生提示显示、未创建SurfaceWindow及自动关闭），上轮界面30、再上一轮32、阶段31、控件82、迁移17，共381项。所有任务窗口使用隔离数据、位于屏幕外，仅按用户“试一下”显示一次提示气泡；未移动鼠标、注入键盘、修改正式数据/自启或关闭用户进程。提示PNG及卡片PNG已查看；未新增真实鼠标交互或开合观感验收。

Codebase Memory初次重连：先index_status确认根目录及0.9.7旧索引，再get_architecture/search_graph/trace_path/get_code_snippet；覆盖确认metadata_changed且MarqueeText/CardSeparator未跟踪，随后full持久化刷新成功，初次5616节点/15992边、8部分解析、0不可用。交付完成后的index_repository/index_status/check_index_coverage再次Transport closed，错误回执已保存，不能声称最终图已涵盖本轮最终源码；按指定源码及来源哈希补证。普通本地修改不上传GitHub，旧包保留。
'@
Set-TaskPrelude 'docs/IMPLEMENTATION.md' '## 0.9.9' $taskImplementation
$taskHandoff=@'
# 新会话接手说明

更新时间：2026-10-18。最新用户指令及PRD34优先。

## 当前状态

- 本地0.9.10，入口`release/即时任务看板-0.9.10-final/即时任务看板.exe`；完整ZIP/SHA256及来源哈希见BUILD-INFO-0.9.10-local。PRD34 U136至U138/A154至A156、TASKS22 T160至T164；上一公开0.9.5，普通修改未上传，旧包保留。
- 第1项已由用户澄清为至少保留一个运行阶段的提示异常，数据迁移未修改。实际控件在子HwndSource中，Window.GetWindow为空，旧定位会抛异常并被Ui.Error变成通用悬浮窗口。已改从原生内容句柄定位；未连接安静跳过，单框、固定屏幕居中偏下、1.2秒，去除额外淡出。
- 任务/阶段名称恢复自然宽与左对齐，不再拉满整行；不足才滚动。右侧会话名六字符宽、原循环和展开重置保持。青线到首个气泡按气泡到正文字形的实际空隙对齐；增加该处留白，线仍一物理像素、0布局高度。
- 最终381项检查通过：171规则、18本轮、30/32界面回归、31阶段、82控件、17迁移。本轮真实显示提示气泡并确认1.2秒消失、无SurfaceWindow增加；其余测试窗口屏幕外，无鼠标或键盘注入，PNG已查看。没有新增真实鼠标触发或开合观感验收；不沿用旧暂停许可。
- Codebase Memory接手曾恢复连接，发现0.9.7索引过旧及新增文件未跟踪后full持久化刷新成功；交付复查再次Transport closed，当前连接不可用，最终图新鲜度未确认。初次成功摘要及末次错误回执见INDEX-STATUS，用指定源码和运行包哈希补证。
- 0.9.9其他修复及0.9.8阶段暂留/人为整理、归档规则保持；用户撤销过1/3门槛，仍2/3、暂存1.35倍及其他100ms。正式数据、自启配置和用户进程未操作。
'@
Set-TaskPrelude 'docs/HANDOFF.md' '## 阅读顺序' $taskHandoff
$taskHandoffText=[IO.File]::ReadAllText((Join-Path $taskRepository 'docs/HANDOFF.md')).Replace('[0.9.9验收](ACCEPTANCE-0.9.9.md)、[验证摘要](VERIFICATION-0.9.9.json)和[本地包清单](BUILD-INFO-0.9.9-local.json)','[0.9.10验收](ACCEPTANCE-0.9.10.md)、[验证摘要](VERIFICATION-0.9.10.json)和[本地包清单](BUILD-INFO-0.9.10-local.json)')
$taskHandoffText=$taskHandoffText.Replace('最近成功图为0.9.7的历史回执，见INDEX-STATUS。2026-10-18实际工具Transport closed，未更新图或ADR，不将旧计数或旧metadata_changed结果冒充本轮索引检查通过。','0.9.10接手曾重连并full持久化刷新；交付复查再次Transport closed。最终图新鲜度未确认，见INDEX-STATUS，按指定源码补证。').Replace('0.9.10本轮连接已恢复并full持久化刷新；此前0.9.8/0.9.9的Transport closed仅为历史。最终覆盖、新鲜度及回执见INDEX-STATUS，元数据变化处仍按指定源码核实。','0.9.10接手曾重连并full持久化刷新；交付复查再次Transport closed。最终图新鲜度未确认，见INDEX-STATUS，按指定源码补证。')
Write-TaskText 'docs/HANDOFF.md' $taskHandoffText
$taskIndex=@'
# 项目文档索引

2026-10-18当前本地0.9.10：修复提示定位空窗口异常，恢复单框固定位置1.2秒气泡；名称恢复自然宽，超出才滚动；青线到气泡留白与气泡到正文字形间距对齐。第1项已澄清为提示问题，数据迁移规则不改。入口PRD34 U136至U138/A154至A156、TASKS22 T160至T164、IMPLEMENTATION0.9.10，[验收](ACCEPTANCE-0.9.10.md)、[验证摘要](VERIFICATION-0.9.10.json)、[完整包](BUILD-INFO-0.9.10-local.json)。381项检查通过，包含真实原生提示显示/自动关闭，无鼠标或键盘注入。Codebase Memory初次重连刷新成功，但交付复查再次断连，最终图新鲜度未确认，回执见INDEX-STATUS。普通修改未上传GitHub，上一公开0.9.5。以下历史状态不代表当前连接。
'@
Set-TaskPrelude 'docs/INDEX.md' '2026-10-18当前本地0.9.9' $taskIndex
$taskIndexStatus=@'
# 索引检查与补证记录

## 2026-10-18 0.9.10：连接恢复并按需刷新

接手实际index_status返回ready、根目录D:/Derp/Documents/0renwumianban，indexed_at仍2026-10-17T14:29:05Z，5260节点/14618边；随后get_architecture/search_graph/trace_path/get_code_snippet定位读取及Ui.Hint。精确覆盖显示Models、AtomicStore、ColumnSurface为metadata_changed，MarqueeText/CardSeparator为not_tracked；因此执行full、persistence=true刷新，初次5616节点/15992边，8个部分解析、0不可用，写入.codebase-memory/graph.db.zst。图缺口或元数据变化仍直接读取指定源码，不将ready等同新鲜。

新交付完成后再次index_repository/index_status/check_index_coverage全部返回Transport closed，未完成最终刷新，不能声称图涵盖0.9.10最终代码或覆盖通过。错误回执保存为test-output/index-0910-final-refresh.json、index-0910-final-status.json、index-0910-final-coverage.json；初次成功摘要为index-0910-initial-summary.json。当前按指定源码及构建来源哈希补证，重连后再刷新最终图。release、test-output和preserved-data继续排除。普通本地修改不自动提交图到GitHub。
'@
Set-TaskPrelude 'docs/INDEX-STATUS.md' '## 2026-10-18 0.9.9' $taskIndexStatus
$taskGuide=@'
# 0.9.10本地修订说明

先从旧版托盘退出，再运行完整目录中的“即时任务看板.exe”，沿用原任务数据路径。

- 修复“至少保留一个运行中的阶段”的提示变成错误悬浮窗口：恢复为屏幕中央偏下的单框气泡，1.2秒自动消失。不会修改阶段或迁移数据。
- 名称气泡恢复原来的紧凑宽度，短名称不再占满整行，空间不够时才滚动；会话名称和原滚动时间规则保持。
- 青色横线与首个阶段名称气泡之间增加留白，与气泡下方正文的间距对齐；线仍一样细。

381项隔离检查通过，包含真实提示显示和自动消失；没有移动鼠标或输入按键。正式数据、自启及用户进程未操作。本轮未上传GitHub；以下旧版说明与本节冲突时以本节为准。
'@
Set-TaskPrelude 'docs/USER-GUIDE.md' '# 0.9.9本地修订说明' $taskGuide
$taskReadme=@'
# 即时任务看板

当前本地0.9.10：修复运行阶段提示定位异常，恢复固定位置的单框1.2秒气泡；名称恢复自然宽，超出才滚动；青线与气泡留白按下方正文间距对齐。现有数据迁移规则保持。

[运行新版](release/即时任务看板-0.9.10-final/即时任务看板.exe) · [完整便携ZIP](release/即时任务看板-0.9.10-local-win-x64.zip)。先从旧版托盘退出，保留完整目录及原数据路径。381项检查通过，含真实原生气泡显示/消失，无鼠标或键盘注入。Codebase Memory初次重连刷新后在交付复查时再次断连，最终图新鲜度未确认，指定源码补证。普通修改未上传GitHub，上一公开0.9.5。

见[本轮验收](docs/ACCEPTANCE-0.9.10.md)、[验证摘要](docs/VERIFICATION-0.9.10.json)和[使用说明](docs/USER-GUIDE.md)。既有其他功能和专项验证边界保持。
'@
Set-TaskPrelude 'README.md' 'Windows桌面任务看板' $taskReadme
Write-TaskText 'README.md' ([IO.File]::ReadAllText((Join-Path $taskRepository 'README.md')).Replace('本轮0.9.9仅本地交付。','本轮0.9.10仅本地交付。'))
$taskAcceptance=@'
# 0.9.10本地验收

2026-10-18。PRD34 U136至U138/A154至A156、TASKS22 T160至T164。用户指定0.9.10；第1项已澄清为运行阶段提示异常，数据迁移规则未改。入口`release/即时任务看板-0.9.10-final/即时任务看板.exe`；完整便携ZIP、SHA256和显式来源清单见BUILD-INFO-0.9.10-local。

在实际自绘控件的HwndSource中复现Window.GetWindow为空，旧WindowInteropHelper调用抛ArgumentNullException，由全局Ui.Error变成错误悬浮窗口。现从PresentationSource/HwndSource原生句柄定位，未连接控件跳过。新建、编辑和列表提示构建均通过；真实原生气泡打开、不增加SurfaceWindow数量，1.2秒自动关闭和清理；关闭系统额外淡出。提示PNG已查看，只有一框。

阶段气泡和任务名称左对齐、按自然宽度占位；短名称无滚动，长名称在右侧会话/状态占位约束下裁切并实际平移。青线到气泡的距离与气泡到正文字形上沿的距离一致，当前夹具均约5.85单位；增加用户要求的留白，线仍0布局高度、一物理像素。卡片PNG已查看。

最终381项：171规则、18本轮提示及布局、30/32界面回归、31阶段、82控件、17迁移。规则程序使用最终发布Core DLL；报告路径、哈希及边界见VERIFICATION-0.9.10。任务窗口使用新隔离目录并位于屏幕外，仅按用户“试一下”显示提示气泡；没有鼠标/键盘注入，不操作正式数据、自启或用户进程。真实鼠标触发和其他开合观感未新增实测，既有边界保持。

Codebase Memory接手曾恢复连接，确认旧索引不新鲜及新文件未跟踪后full持久化刷新成功；交付后的刷新、状态与覆盖复查再次Transport closed，最终图新鲜度未确认。初次成功摘要及末次失败回执见INDEX-STATUS，按指定源码及来源哈希补证。ZIP逐文件核对字节、长度与SHA256，旧包保留；本轮未上传GitHub。
'@
Write-TaskText 'docs/ACCEPTANCE-0.9.10.md' $taskAcceptance
Write-Output ('检查：'+$taskVerification.Passed+'；来源：'+$taskScope.Count+'；文档已写入')
