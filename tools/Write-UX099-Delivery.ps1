param()
$ErrorActionPreference='Stop'
$taskRepository=Split-Path -Parent $PSScriptRoot
function Write-TaskText([string]$Path,[string]$Text) {
 [IO.File]::WriteAllText((Join-Path $taskRepository $Path),$Text.TrimEnd()+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
}
function Add-TaskPrelude([string]$Path,[string]$Marker,[string]$Text) {
 $existing=[IO.File]::ReadAllText((Join-Path $taskRepository $Path)); $at=$existing.IndexOf($Marker,[StringComparison]::Ordinal)
 if($at -lt 0){throw ('文档边界缺失：'+$Path)}
 Write-TaskText $Path ($Text.TrimEnd()+[Environment]::NewLine+[Environment]::NewLine+$existing.Substring($at))
}
$taskReports=@(
 @{Kind='core';Path='test-output/ux099-final-core/report.json';Expected=171},
 @{Kind='ux099';Path='test-output/ux099-verified-ux099/ux099-report.json';Expected=30},
 @{Kind='ux098-regression';Path='test-output/ux099-verified-ux098/ux098-report.json';Expected=32},
 @{Kind='stage-regression';Path='test-output/ux099-verified-stage/stage-report.json';Expected=31},
 @{Kind='compact-regression';Path='test-output/ux099-verified-compact/compact-report.json';Expected=82},
 @{Kind='integration';Path='test-output/ux099-verified-integration/integration-report.json';Expected=17}
)
$taskResults=@(foreach($entry in $taskReports){
 $path=Join-Path $taskRepository $entry.Path; $report=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
 if($report.passed -ne $entry.Expected){throw ('检查未完成：'+$entry.Path)}
 @{Kind=$entry.Kind;Report=$entry.Path;Passed=$report.passed;SHA256=(Get-FileHash -LiteralPath $path).Hash}
})
$taskRuntime=Join-Path $taskRepository 'release/即时任务看板-0.9.9-final'
if((Get-FileHash -LiteralPath (Join-Path $taskRuntime 'Taskboard.Core.dll')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskRepository 'test-output/ux099-exact-core-runner/Taskboard.Core.dll')).Hash){throw '最终Core DLL与规则检查程序不一致'}
$taskEvidence=[ordered]@{
 Version='0.9.9';Date='2026-10-18';LocalOnly=$true;Entry='release/即时任务看板-0.9.9-final/即时任务看板.exe'
 Passed=($taskResults.Passed | Measure-Object -Sum).Sum;Reports=$taskResults;CoreTestUsesPublishedCore=$true
 RuntimeHashes=@(foreach($name in @('Taskboard.dll','Taskboard.Core.dll','即时任务看板.exe')){@{Path=$name;SHA256=(Get-FileHash -LiteralPath (Join-Path $taskRuntime $name)).Hash}})
 DraftSwitchSpeed=1.35;DraftSwitchQueueThreshold='2/3 unchanged: user withdrew request 3';OtherAnimationDurationMs=100;HintDurationMs=1200
 Marquee=@{Width='six uppercase M advances in current font';InitialPauseSeconds=0.5;EndPauseSeconds=0.2;PixelsPerSecond=36;RestartOnWorkspaceOpen=$true}
 CodebaseMemory=@{Connected=$false;Attempted=@('index_status','get_architecture','search_graph');Error='Transport closed';GraphRefreshed=$false;Fallback='rg、指定源码与显式来源SHA256'}
 DesktopAttempts=@('test-output/ux099-desktop-a/ux099-desktop-report.json','test-output/ux099-desktop-b/ux099-desktop-report.json')
 DesktopInputVerified=$false;DesktopVisualVerified=$false;DesktopPreflightFailure='测试窗口未取得前台，未发出鼠标或键盘输入';MouseMoved=$false;KeyboardInjected=$false
 ProductionDataChanged=$false;StartupConfigurationChanged=$false;UserProcessesStopped=$false;RealCodexCatalogVerified=$false
 Boundaries=@('用户授权暂停后尝试实际桌面验证；前台检查失败即停止，未操作正式数据。','363项隔离检查不能代替真实点击与合成画面闪烁验收。','索引工具实际断连，未假称刷新；旧包与旧报告保留。','本轮普通修改不上传GitHub，上一公开版本0.9.5。')
}
$taskEvidence | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $taskRepository 'docs/VERIFICATION-0.9.9.json') -Encoding utf8
$taskScope=@(Get-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux098-source-scope.json') -Raw | ConvertFrom-Json)
$taskScope=@($taskScope | Where-Object { $_ -ne 'tools/Write-UX098-Delivery.ps1' })
$taskScope=@($taskScope + @('src/Taskboard.Desktop/CardSeparator.cs','src/Taskboard.Desktop/MarqueeText.cs','src/Taskboard.Desktop/Ux099Checks.cs','src/Taskboard.Desktop/Ux099DesktopChecks.cs','src/Taskboard.Desktop/WorkspaceChecks.cs','tools/Write-UX099-Delivery.ps1') | Sort-Object -Unique)
$taskScope | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRepository 'test-output/ux099-source-scope.json') -Encoding utf8

$taskPrd=@'
# 即时任务看板电脑端PRD

版本：0.9.9。更新日期：2026-10-18。本轮以第33节为准，覆盖冲突旧条款；第3项加速点击门槛需求已由用户撤销，仍为切换动画2/3以后最多接收一次后续切换。
'@
Add-TaskPrelude 'docs/PRD.md' '## 1 ' $taskPrd
$taskPrdBody=[IO.File]::ReadAllText((Join-Path $taskRepository 'docs/PRD.md'))
$taskSection=@'
## 33 2026-10-18 分隔线、初始数量、会话交互及画面交接（0.9.9）

用户撤销本轮第3项：暂存切换点击门槛维持2/3，切换1.35倍，普通开合100ms不变。

| 编号 / 验收 | 已确认规则 |
| --- | --- |
| U126 / A144 | 标题与详情之间青色线和阶段灰色线共用一物理像素厚度、同宽并对齐像素；标题线叠在现有间隔，布局高度为0，不增大卡片，不移动内容 |
| U127 / A145 | 程序启动、悬浮球首次显示前按已载入的待操作任务刷新数量；无需先展开。后续数量仍随数据变化更新 |
| U128 / A146 | 关联搜索下拉松开鼠标后保持；删除常规搜索说明行，目录为空或失败时保留反馈。再次点击关联气泡或点击展开区以外的编辑区域收起，输入和选择结果不误关；搜索不改绑定，选中才改 |
| U129 / A147 | 看板内部列表空白/标题外部双击：新建仍暂存全部字段，编辑改为正式保存并关闭，与保存按钮共用验证及落盘逻辑；失败保留窗口和输入。任务卡片手势例外、再次点击同一任务入口保留副本、点击其他应用整组收起保留窗口规则不变 |
| U130 / A148 | 至少保留一个运行阶段提示仅一个气泡边框，1.2秒自动消失（原2秒的3/5）；位置固定于当前屏幕工作区水平中心、约62%高度，而非跟随按钮 |
| U131 / A149 | 列表阶段名称右侧、运行图标左侧显示无边框的会话名称，无绑定则无文本。会话栏上限六个英文M字符的当前字体宽度；阶段名称或单阶段任务名称占其余空间。溢出才滚动：首停0.5秒、36单位/秒滚到末尾、末停0.2秒后循环。每次球/快捷键展开从头播放，收起冻结当前画面供动画使用，隐藏停止计时；不保存滚动偏移 |
| U132 / A150 | 灵感库列表卡片不显示关联会话名称及运行中/暂无进程图标，完整编辑窗口仍可配置阶段和关联 |
| U133 / A151 | 展开结束交接不让动画画面和正式内容重叠显示；正式背景恢复与动画背景移除同次合成提交，内容准备期间原生输入表面保持隐藏。仍保留快速反向和动画代次保护。源码原因已向用户说明，实际桌面闪烁是否消除需视觉验证，不以离屏检查冒充 |
| U134 / A152 | 单列右键菜单文字改成“本窗口恢复到默认”，功能不变 |
| U135 / A153 | 编辑窗口右上角“×”改为“取消保存”，保持原放弃副本的含义；为完整显示文字宽84、高40。新建窗口仍为原X及暂存规则 |
'@
if(-not $taskPrdBody.Contains('## 33 ')){Write-TaskText 'docs/PRD.md' ($taskPrdBody.TrimEnd()+[Environment]::NewLine+[Environment]::NewLine+$taskSection)}

$taskTasks=@'
# 电脑端任务面板工作清单

## 21 0.9.9交互、滚动和画面交接（2026-10-18）

| 任务 | 工作 | 验收 |
| --- | --- | --- |
| T149 | 分隔线共用像素绘制、标题线不占高度 | A144 |
| T150 | 启动首次球显示前初始化计数 | A145 |
| T151 | 下拉鼠标捕获修复、外部收起与删除说明 | A146 |
| T152 | 外部双击编辑与保存按钮共用正式保存路径 | A147 |
| T153 | 单框提示、固定屏幕位置和1.2秒时长 | A148 |
| T154 | 会话名称六字符、剩余空间名称及溢出滚动 | A149 |
| T155 | 灵感库排除关联名称与运行图标 | A150 |
| T156 | 单次背景交接、原生内容显示时序及动画保护 | A151 |
| T157 | 单列恢复和取消保存按钮文字 | A152、A153 |
| T158 | 363项最终包隔离检查与桌面输入前置检查 | A144至A153 |
| T159 | 完整本地便携ZIP/SHA256、来源哈希及文档 | 本地交付 |

以上实现及363项隔离检查完成。用户已撤销第3项，2/3点击门槛不变。经用户协调后两次桌面前台检查均失败，零项真实输入通过，未移动鼠标；实际点击与闪烁观感仍待验。普通修改不上传GitHub。
'@
Add-TaskPrelude 'docs/TASKS.md' '## 20 ' $taskTasks

$taskImplementation=@'
# 新版电脑端实施记录

## 0.9.9分隔、会话交互及交接修订（2026-10-18）

对应PRD33 U126至U135/A144至A153、TASKS21 T149至T159。第3项已撤销，SurfaceWindow原2/3暂存切换门槛未改，普通100ms和暂存1.35倍保持。

标题线先前是布局中的独立1单位Border并额外占间隔，紧凑布局的分数位置还可能造成跨像素采样。现在CardSeparator统一按当前DPI绘制一物理像素并对齐屏幕像素；标题线Height0覆盖现有空隙，灰线原间隔保留。高度对比和像素行检查通过，最终离屏卡片图已查看。

启动球徽标缺失来自Start未主动RefreshData，只有后续OnChanged/展开才更新。现在先刷新再显示，夹具直接装入任务、不发送Notify也可在未展开时得到初始计数。

StageConversationPicker原StaysOpen=false在TextBox按下时打开会捕获鼠标，与文本选择捕获及松开竞争而关闭。改为不抢捕获的常开Popup，由编辑根的鼠标/键盘焦点、外部按下和收起路径明确管理展开区；Pane/Input/Trigger/下拉内部例外。删除常规说明，仅保留目录空/失败及选中反馈。未改变只按名称搜索和选择才绑定规则。

编辑外部双击先前调用Suspend，只保存会话副本，正式列表自然没有新的运行阶段。现在使用EditorSurface.SaveAndClose，与保存按钮共用状态/提醒验证及候选先落盘、正式合并后关闭；失败保留副本。新建暂存、卡片点击例外和同卡片重复入口的保留副本规则保持。编辑右上角改为84×40的“取消保存”，仍调用原Discard路径。

Ui.Hint原默认ToolTip模板包住自绘Border，可能叠成两个框。改为纯ContentPresenter模板只保留一个自绘框，关闭默认阴影，时长1200ms；自定义定位到当前屏幕工作区中心偏下62%高度，不跟随按钮。

MarqueeText以自然文本宽度测量、按视口裁切，只有溢出时在TranslateTransform上播放0.5秒首停、36单位/秒平移、0.2秒末停的循环。会话名称在名称与状态图标之间，六M字符宽；任务/阶段名称占剩余空间。收起冻结当前偏移并刷新快照，展开重置并刷新快照，隐藏/卸载停计时；灵感库不建名称栏或运行按钮。窄行裁切防止文字溢出邻卡。

闪烁原因先向用户报告后处理：Finish曾在动画快照仍存在时显示实时子表面，之后CommitRestingFrame又先恢复正式背景、再另一步移除动画背景，存在重复半透明绘制。现在实时内容准备期间原生子表面保持隐藏；恢复正式背景和移除动画场景在同次提交中完成，并在交接点显示实时内容。保持代次检查，旧异步回调不得取消新动画。离屏连续开合可完成交接且无动画异常；不声称实际桌面观感已证明无闪烁。

最终运行包检查363项：171规则、30本轮界面、32上轮界面、31阶段录入、82控件、17迁移。最终Core DLL复制给规则程序执行。用户授权后两次真实桌面前台预检失败，未发鼠标/键盘输入，及时释放暂停时段；真实点击、固定提示外观及实际合成画面仍待验。Codebase Memory实际index_status/get_architecture/search_graph均Transport closed，未刷新图或ADR；指定源码与构建哈希补证。未改正式数据、自启配置或用户进程，未上传GitHub。
'@
Add-TaskPrelude 'docs/IMPLEMENTATION.md' '## 0.9.8' $taskImplementation

$taskHandoff=@'
# 新会话接手说明

更新时间：2026-10-18。最新指令和PRD33优先。

## 当前状态

- 本地0.9.9：`release/即时任务看板-0.9.9-final/即时任务看板.exe`，完整ZIP/SHA256见BUILD-INFO-0.9.9-local。PRD33 U126至U135/A144至A153、TASKS21 T149至T159。上一公开0.9.5，普通修改未上传，旧包保留。
- 标题线不占高度，两色横线均一物理像素；启动球计数预载；关联下拉不抢捕获、松开保留、外部编辑区域收起，删除常规说明。
- 列表空白/标题外部双击：新建暂存，编辑正式保存并关闭；失败保留。卡片手势例外与重复同卡入口暂存副本规则不变。编辑右上角“取消保存”放弃副本；其他应用点击仍整组收起保留窗口。
- 最后运行阶段提示仅一框、固定当前屏幕中心偏下、1.2秒。列表会话名称六M字符宽，剩余空间用于阶段/任务名称；溢出首停0.5秒、36单位/秒、末停0.2秒循环，重开重置。灵感库不显示名称栏与运行图标。
- 画面交接期间实时原生内容保持隐藏，恢复正式背景和移除动画背景同次提交，防止短暂叠亮。第3项用户已撤销，点击门槛保持2/3、暂存1.35倍、开合100ms；不要改为1/3。
- 最终包363项隔离检查通过；用户协调后两次桌面前台检查失败，未移动鼠标或注入键盘，真实点击和闪烁观感未验收，已通知用户恢复操作。不能沿用这个已结束的暂停时段；正式数据、自启与用户进程未操作。
- Codebase Memory本轮实际index_status/get_architecture/search_graph均Transport closed，图未刷新，指定源码与显式来源哈希补证；重连后检查覆盖及新鲜度。不会把0.9.7旧图当成当前。
- 0.9.8的阶段暂留、人为统一收起、删除/归档180ms、完成时间、自定义自动归档天数、20/全部预览及可选保留40均沿用。真实会话目录/实时桥接等既有边界不扩大。
'@
Add-TaskPrelude 'docs/HANDOFF.md' '## 阅读顺序' $taskHandoff
$taskHandoffPath=Join-Path $taskRepository 'docs/HANDOFF.md'
$taskHandoffText=[IO.File]::ReadAllText($taskHandoffPath).Replace('[0.9.8验收](ACCEPTANCE-0.9.8.md)、[验证摘要](VERIFICATION-0.9.8.json)和[本地包清单](BUILD-INFO-0.9.8-local.json)','[0.9.9验收](ACCEPTANCE-0.9.9.md)、[验证摘要](VERIFICATION-0.9.9.json)和[本地包清单](BUILD-INFO-0.9.9-local.json)')
Write-TaskText 'docs/HANDOFF.md' $taskHandoffText

$taskIndex=@'
# 项目文档索引

2026-10-18当前本地0.9.9：无增高一像素横线、启动球计数、搜索下拉与外部收起、编辑外部双击正式保存、取消保存、固定单框1.2秒提示、名称六字符及溢出滚动、灵感显示例外、单次背景交接。第3项已撤销，2/3门槛不变。入口PRD33 U126至U135/A144至A153、TASKS21 T149至T159、IMPLEMENTATION0.9.9，[验收](ACCEPTANCE-0.9.9.md)、[验证摘要](VERIFICATION-0.9.9.json)、[完整包](BUILD-INFO-0.9.9-local.json)。最终363项隔离检查通过；实际桌面前台检查失败，零项真实输入通过，鼠标未移动，真实点击和闪烁观感待验。Codebase Memory实际断连，指定源码补证。普通修改未上传，上一公开0.9.5。以下为历史来源。
'@
Add-TaskPrelude 'docs/INDEX.md' '2026-10-18当前本地0.9.8' $taskIndex
$taskIndexStatus=@'
# 索引检查与补证记录

## 2026-10-18 0.9.9：实际工具仍断连

按顺序实际调用taskboard的index_status、get_architecture，再search_graph，均返回Transport closed，无法核验根目录/覆盖/新鲜度，因此未重建或声称图已刷新，未假造trace_path/get_code_snippet结果。按相关文档与指定源码用rg补证，最终显式源码和运行文件SHA256见BUILD-INFO-0.9.9-local。

当前入口PRD33/TASKS21/IMPLEMENTATION0.9.9。重连后先核实根目录D:/Derp/Documents/0renwumianban及覆盖、新鲜度，再按需刷新；新符号MarqueeText、CardSeparator、SaveAndClose、CommitRestingFrame需覆盖。旧0.9.7图保留为历史，未冒充新鲜。实际桌面前台预检失败不等于产品交互失败，且不能计为鼠标验证通过。
'@
Add-TaskPrelude 'docs/INDEX-STATUS.md' '## 2026-10-18 0.9.8' $taskIndexStatus

$taskGuide=@'
# 0.9.9本地修订说明

从旧版托盘退出后，运行完整目录中的“即时任务看板.exe”。保留原任务数据路径，本轮未上传GitHub。

- 标题青色横线与阶段横线一样细，不增加任务卡片高度；悬浮球启动即显示待操作数量。
- 关联搜索框点击松开后下拉保持；只按会话名称搜索，选中结果才改变绑定。点击任务名称等其他编辑区域自动收起，再点关联气泡也可收起。
- 看板内部列表空白/标题处双击，新建窗口暂存，编辑窗口正式保存并关闭；保存失败保留窗口。编辑右上角“取消保存”放弃当前副本。再次点同一任务卡片关闭编辑仍保留副本；点击其他应用仍收起整组而不关闭悬浮窗口。
- 列表在阶段名称与状态图标间显示关联会话名称，六个英文M字符宽；长名称滚动，先停0.5秒，末尾停0.2秒再循环。任务或阶段名称占其余空间，过长同样滚动。每次展开从头播放。灵感库不显示会话名称与运行状态图标。
- 至少保留一个运行阶段的提示只有一个框，固定于当前屏幕中央偏下，1.2秒消失。
- 展开结束的画面交接已调整。暂存切换保持1.35倍，前2/3忽略后续点击、之后最多接收一次；您已撤销改为1/3的需求。其他开合仍100ms。
- 单列右键菜单改称“本窗口恢复到默认”，仅恢复该列。球菜单的恢复窗口默认仍恢复整组。

最终363项隔离检查通过；您允许后的桌面前台检查未通过，未发出鼠标或键盘输入。真实点击、提示观感及闪烁是否完全消除仍待验；实际Codex目录连线也不算本轮已验。以下为旧版来源，冲突时以本节为准。
'@
Add-TaskPrelude 'docs/USER-GUIDE.md' '# 0.9.8本地修订说明' $taskGuide

$taskReadme=@'
# 即时任务看板

当前本地0.9.9：标题细线不增高、启动悬浮球数量、会话下拉保留和外部收起、编辑外部双击正式保存、取消保存、固定单框短提示、列表会话名称及溢出滚动、灵感显示例外、展开画面交接修复。第3项已撤销，暂存切换门槛保持2/3，1.35倍与其他100ms动效不变。

本地入口：[即时任务看板.exe](release/即时任务看板-0.9.9-final/即时任务看板.exe)，[完整便携ZIP](release/即时任务看板-0.9.9-local-win-x64.zip)。先从旧版托盘退出，保留完整目录及原数据路径。最终363项隔离检查通过；实际桌面前台预检失败，未移动鼠标，真实点击和闪烁观感仍待验。Codebase Memory实际断连，指定源码与构建哈希补证。未上传GitHub，上一公开0.9.5。

见[本轮验收](docs/ACCEPTANCE-0.9.9.md)、[验证摘要](docs/VERIFICATION-0.9.9.json)与[使用说明](docs/USER-GUIDE.md)。可设天数的自动归档、阶段暂留与人为收起等0.9.8功能沿用。
'@
Add-TaskPrelude 'README.md' 'Windows桌面任务看板' $taskReadme
Write-TaskText 'README.md' ([IO.File]::ReadAllText((Join-Path $taskRepository 'README.md')).Replace('本轮0.9.7仅本地交付。','本轮0.9.9仅本地交付。'))

$taskAcceptance=@'
# 0.9.9本地验收

2026-10-18。PRD33 U126至U135/A144至A153、TASKS21 T149至T159。入口`release/即时任务看板-0.9.9-final/即时任务看板.exe`；完整ZIP、SHA256、显式相关来源与运行文件清单见BUILD-INFO-0.9.9-local。

标题青色线与灰线同为一物理像素，标题线不占高度；启动球计数主动刷新。搜索下拉不抢文本框鼠标捕获，松开后保留、编辑区外点收起，删除常规说明。编辑外部双击与保存按钮共用候选落盘和验证后正式保存，列表立即显示新的运行阶段；新建仍暂存，取消保存放弃副本。提示只保留一个自绘边框、固定屏幕中心偏下、1200ms。

列表会话名称无框、六个英文M宽；阶段和单阶段任务名称占剩余空间，超长首停0.5秒、36单位/秒滚动、末停0.2秒循环，展开重置、收起冻结、隐藏停止。灵感不建名称栏或运行图标。单列菜单改称本窗口恢复到默认。

闪烁已先报告源码原因：实时子表面过早显示、正式背景先恢复而动画背景稍后才移除，可能重复半透明绘制。现实时内容准备时隐藏，正式背景恢复与动画背景移除一次提交。连续开合交接完成且无动画异常；不把离屏状态通过冒充实际桌面无闪烁。第3项已撤销，2/3点击门槛、1.35倍切换及普通100ms保持。

最终包363项隔离检查通过：171规则、30本轮界面、32上轮界面、31阶段录入、82控件、17迁移。最终Core DLL交给检查程序执行；离屏分隔像素和高度、真实平移动画属性、再次展开重置、选择输入例外、正式保存落盘与列表、取消保存、新建只暂存及连续交接均有断言。最终离屏卡片图已查看。

用户授权暂停后两次尝试真实桌面输入，测试窗口未取得前台，前置检查立即停止，零项真实输入通过；未移动鼠标或注入键盘，已通知恢复操作。真实搜索点击、提示外观及合成画面仍待验。桌面失败回执与隔离报告路径/哈希见VERIFICATION-0.9.9，不能沿用本次已结束的暂停时段。

不改正式数据、自启配置或用户进程，不自动上传GitHub，旧包保留。Codebase Memory三项实际调用均Transport closed，未重建或声称刷新，按指定源码与来源哈希补证。完整自包含win-x64包逐文件核对ZIP内容、大小及SHA256，完整性以BUILD-INFO为准。
'@
Write-TaskText 'docs/ACCEPTANCE-0.9.9.md' $taskAcceptance
Write-Output ('文档与回执已写入；隔离检查：'+$taskEvidence.Passed+'；来源：'+$taskScope.Count)
