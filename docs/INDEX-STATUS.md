# 索引检查与补证记录

## 0.9.16本轮

已实际调用index_status确认Instant-taskboard根目录；get_architecture、search_graph、trace_path和get_code_snippet用于精确定位。2026-10-19T07:25:09Z按该名称full持久化刷新，6773节点、20067边，ready。九个PowerShell文件部分解析，图标等按设计排除；本轮五个C#路径覆盖无记录问题，但freshness仍报metadata_changed。已直接核实源码并以VERIFICATION-0.9.16及BUILD-INFO-0.9.16的源码/运行DLL哈希补证，不宣称图完整或新鲜度全绿。后续文档新增由索引自动刷新，状态数量不作完整性保证。

以下为旧索引记录。

2026-10-19 0.9.15本轮：实际index_status后get_architecture，search_graph定位NewTask/RevealLiveList/CommitRestingFrame、trace_path到CompleteHandoff、get_code_snippet读取交接；按路径覆盖及新文件缺失重建full持久化。最终产品源码定稿后刷新为6476节点/18975边，UTC2026-10-18T22:16:48Z，根D:/Derp/Documents/0renwumianban，parse_partial8、unusable0；产物.codebase-memory/graph.db.zst。重建后C#（含新LiveScroll/LiveBall/CardHeading）仍报metadata_changed，Theme.xaml not_tracked，按直接源码与BUILD-INFO-0.9.15-local哈希补证，不把ready当全部新鲜。回执为test-output/index-0915-final-refresh.json、index-0915-final-status.json、index-0915-final-coverage.json。此后仅修订时序检查与交付文档，产品源码保持；时序检查直接读取和来源SHA256补证，不再循环重建。下列为历史回执。

## 2026-10-19 0.9.14：交互范围、保存与滚动路径已刷新

本轮工具先短暂无命名空间，随后恢复；实际index_status检查Instant-taskboard ready、根D:/Derp/Documents/0renwumianban，再architecture/search_graph/trace_path/get_code_snippet定位；精确覆盖metadata_changed，Theme.xaml为not_tracked，直接读取相关源码补证。新增QuickScroll及新检查后执行full、persistence=true一次刷新成功，6193节点/18160边，8个PowerShell部分解析、0不可用，持久化图已写入.codebase-memory/graph.db.zst。

最终status、coverage和symbols回执见test-output/index-0914-final-*.json；ShowOrbMenu、StepItem、CanMove已可检索。刷新后C#仍有metadata_changed，Theme.xaml未追踪，不能说完全新鲜；XAML依靠直接源码、实际滑块几何/命中测试和包来源SHA256覆盖。不循环重建。正式数据、发布目录和测试产物继续排除，未上传GitHub。

## 2026-10-19 0.9.13：右侧吸附与新交互已刷新

实际index_status核对Instant-taskboard、ready、根D:/Derp/Documents/0renwumianban；依次architecture、search_graph、trace_path、get_code_snippet定位。新增HintScreenPin与Ux0913Checks起初not_tracked，其他修改文件metadata_changed，已直接读取指定源码，并执行full、persistence=true一次刷新，6031节点/17511边，8个PowerShell部分解析、0不可用，图已写入.codebase-memory/graph.db.zst。新增符号已能精准检索。

刷新后六条精确源码路径仍报告metadata_changed，但已从not_tracked变为有记录。按工具限制如实保留新鲜度信号，不循环重建或把ready说成完全覆盖；以直接源码、最终便携包与显式来源SHA256补证。回执见test-output/index-0913-final-status.json、index-0913-final-coverage.json、index-0913-final-symbols.json。正式数据、发布目录和测试产物继续排除；普通修改未上传GitHub。

## 2026-10-19 0.9.12：真实裁切与最终包

接手实际index_status确认Instant-taskboard ready、根D:/Derp/Documents/0renwumianban，先架构后符号、关系和片段定位；覆盖或路径缺失处直接核实指定源码。最终full、persistence=true成功，5943节点/17094边，8个PowerShell部分解析、0不可用；图已写入.codebase-memory/graph.db.zst。电脑端需求规划、源码定位和交付ADR已按命名章节更新。

RevealLiveList/ClipLiveList、ConversationWidth及本轮检查已可精准搜索。精确源码路径均无记录解析问题，但工具刷新后仍报metadata_changed，不能等同于新鲜度干净通过；使用直接源码、最终包378项检查、实屏54项和显式来源SHA256补证，不重复无效重建。回执test-output/index-0912-final-status.json、index-0912-final-coverage.json、index-0912-final-symbols.json。release、test-output、preserved-data继续排除，普通修改未上传GitHub。

## 2026-10-18 0.9.11：已连接并改用指定名称

实际index_status检查Instant-taskboard返回未索引，列出现有taskboard；继而核对旧名ready、根D:/Derp/Documents/0renwumianban和覆盖，再调用get_architecture、search_graph、trace_path、get_code_snippet。精确路径提示metadata_changed，按用户指定名Instant-taskboard执行full、persistence=true重建，初次5706节点/16204边；最新复查5811节点/16628边，ready，indexed_at=2026-10-18T14:41:42Z，8个部分解析（PowerShell）、0不可用。.codebase-memory/graph.db.zst已写入，保留数据、运行包和测试产物继续排除。

当前电脑端需求规划及交付ADR已按命名章节更新。新增HideAtEdge/RestoreFromEdge已能search_graph定位，trace_path能连到4秒闲置、徽标刷新及原生裁形，get_code_snippet的完整方法与当前源码一致。7项指定源码覆盖均no_recorded_issue，但即使刷新后新鲜度仍标metadata_changed，不能将其说成全覆盖或新鲜度干净通过；已读取指定源码并以最终包的来源SHA256和最终301项检查补证，不因该信号反复重建。回执在test-output/index-0911-final-status.json、index-0911-final-coverage.json、index-0911-final-symbols.json。下面断连记录仅代表历史版本。

## 2026-10-18 0.9.10：连接恢复并按需刷新

接手实际index_status返回ready、根目录D:/Derp/Documents/0renwumianban，indexed_at仍2026-10-17T14:29:05Z，5260节点/14618边；随后get_architecture/search_graph/trace_path/get_code_snippet定位读取及Ui.Hint。精确覆盖显示Models、AtomicStore、ColumnSurface为metadata_changed，MarqueeText/CardSeparator为not_tracked；因此执行full、persistence=true刷新，初次5616节点/15992边，8个部分解析、0不可用，写入.codebase-memory/graph.db.zst。图缺口或元数据变化仍直接读取指定源码，不将ready等同新鲜。

新交付完成后再次index_repository/index_status/check_index_coverage全部返回Transport closed，未完成最终刷新，不能声称图涵盖0.9.10最终代码或覆盖通过。错误回执保存为test-output/index-0910-final-refresh.json、index-0910-final-status.json、index-0910-final-coverage.json；初次成功摘要为index-0910-initial-summary.json。当前按指定源码及构建来源哈希补证，重连后再刷新最终图。release、test-output和preserved-data继续排除。普通本地修改不自动提交图到GitHub。

## 2026-10-18 0.9.9：实际工具仍断连

按顺序实际调用taskboard的index_status、get_architecture，再search_graph，均返回Transport closed，无法核验根目录/覆盖/新鲜度，因此未重建或声称图已刷新，未假造trace_path/get_code_snippet结果。按相关文档与指定源码用rg补证，最终显式源码和运行文件SHA256见BUILD-INFO-0.9.9-local。

当前入口PRD33/TASKS21/IMPLEMENTATION0.9.9。重连后先核实根目录D:/Derp/Documents/0renwumianban及覆盖、新鲜度，再按需刷新；新符号MarqueeText、CardSeparator、SaveAndClose、CommitRestingFrame需覆盖。旧0.9.7图保留为历史，未冒充新鲜。实际桌面前台预检失败不等于产品交互失败，且不能计为鼠标验证通过。

## 2026-10-18 0.9.8：实际工具断连

本轮先调用taskboard的index_status检查索引，随后get_architecture和search_graph，均返回Transport closed。因此当前Codebase Memory没有可用连接，未执行重建、ADR更新或持久化刷新，不能把0.9.7的ready计数当作本轮新鲜索引。图工具不可用后按相关章节、rg与指定源码核实；最终构建清单的显式相关源码与运行文件SHA256补证，未遍历全部源码、旧聊天或历史日志。

当前0.9.8入口PRD32 U117至U125/A135至A143、TASKS20 T140至T148、IMPLEMENTATION0.9.8，验收与报告见ACCEPTANCE-0.9.8和VERIFICATION-0.9.8。下次工具重连后应核实覆盖并刷新这些新符号：WorkspaceRevealBatch、StageConversationPicker、VisibleCardStages、CleanupRetainedStages、TaskCardMotion、DueArchives、TrimArchiveHistory。旧0.9.7图仍是历史版本，不删除、不伪装为已更新。

## 2026-10-17 0.9.7阶段运行与菜单修订

接手先实际index_status，taskboard ready、根目录D:/Derp/Documents/0renwumianban，开始5029节点/13487边、8个部分解析、0不可用；再get_architecture、search_graph、trace_path、get_code_snippet精准定位。末个运行阶段补充前再次读取图中SetStageRunning片段，覆盖或新鲜度不明处直接核实指定源码，未遍历全部历史源码或聊天。

ADR按名更新当前入口、规则、实施、Codex阶段会话、规划与交付六章，保留其余内容。0.9.7最终源码、文档及完整本地包完成后full持久化成功，5260节点/14618边、8个部分解析、0不可用；index_status ready，indexed_at=2026-10-17T14:29:05Z。回执test-output/index-097-final-refresh.json、index-097-final-status.json、index-097-final-coverage.json，图写入.codebase-memory/graph.db.zst；release/test-output/preserved-data继续排除。

立即核对9个指定路径仍全部metadata_changed，不宣称图已证明源码新鲜。通过指定源码、52个相关来源SHA256、最终Core DLL与规则检查使用DLL一致、运行文件哈希及476文件ZIP逐字节核对补证。151规则、31阶段/录入/原生注册、82控件、17迁移设置通过。实际输入尝试默认注册成功但测试窗口未取得前台，不能写成中英文触发、真实点击或气泡画面已验收。当前PRD31/TASKS19，至少保留一运行阶段及原球显隐开关第一/设置第二；普通本地修改未上传GitHub，上一公开0.9.5。详见ACCEPTANCE-0.9.7和VERIFICATION-0.9.7。

## 2026-10-15至16 0.9.6本地及桌面验收

本轮开始实际index_status为taskboard ready、根目录D:/Derp/Documents/0renwumianban、4788节点/12801边、8个PowerShell部分解析，随后get_architecture、search_graph、trace_path与get_code_snippet定位输入观察和布局。路径metadata_changed，直接读取指定源码补证；未遍历历史源码或聊天。

ADR按名更新当前入口、规则、实施、规划与交付五章。最后full持久化成功，4964节点/13401边、8个部分解析、0不可用解析，index_status ready且indexed_at=2026-10-15T15:38:17Z。回执test-output/index-096-refresh.json、index-096-status.json及index-096-coverage.json，图写入.codebase-memory/graph.db.zst。release/test-output/preserved-data继续排除。

立即核验9个指定路径仍metadata_changed，不声称图确认源码新鲜；新增CapturePress、MousePressGate、PositionFloating、RestoreColumnDefault均可检索。结论以直接源码和BUILD-INFO-0.9.6-local来源/运行文件SHA256、476文件ZIP逐个核验补证。16项不显示窗口的布局/配置/原始输入登记/样式检查和82控件通过；实际游戏、Alt+Tab及移动鼠标的桌面检查尚待用户协调回复，不冒充已完成。当前PRD30/TASKS18，0.9.6普通本地修改未发布GitHub，公开仍0.9.5。

2026-10-16收到用户协调同意后，22项桌面布局、4项普通外点、5项仅原始输入全屏模拟、6项手势及真实Alt+Tab画面核验通过；前文待协调为此前状态。实际游戏未操作。最终入口verified完整目录，截图与运行输出排除公开。新增隔离测试入口及工具后再次更新ADR并full持久化，最终回执见test-output/index-096-desktop-refresh.json、index-096-desktop-status.json和index-096-desktop-coverage.json；计数以实际回执为准，metadata_changed限制继续直接源码/构建哈希补证。

## 2026-10-15 0.9.5本地版：统一进度与最终错位32/32

本轮先实际index_status检查taskboard ready、根目录及覆盖，再get_architecture、search_graph、trace_path和get_code_snippet定位；开始为4662节点/12563边，7个PowerShell部分解析。metadata_changed路径按指定源码读取补证，未遍历历史源码或聊天。

manage_adr(set_sections)已更新当前入口、产品规则、实施、电脑端规划及交付五章，保留其他章节。完成0.9.5源码、完整运行包及验收后，full持久化返回indexed，4762节点/12775边，8个PowerShell部分解析、0不可用解析；index_status为ready，indexed_at=2026-10-15T07:46:30Z、根目录正确。回执为test-output/index-095-refresh.json、index-095-status.json与index-095-coverage.json，持久图.codebase-memory/graph.db.zst。release/test-output/preserved-data继续排除。

立即覆盖核验的13条指定路径仍全部metadata_changed，不能宣称工具确认源码新鲜。新IsOutsideDismissArea、FreezeScroll、LightExpression已可在图检索；相关源码直接核实，构建清单来源及运行文件SHA256逐一匹配，476文件ZIP逐一核验。PowerShell解析缺口由脚本与实际隔离测试补证；元数据诊断现象未作为任务看板产品问题修改。

当前入口为HANDOFF、INDEX、PRD29、TASKS17、IMPLEMENTATION的0.9.5段、ACCEPTANCE-0.9.5、VERIFICATION-0.9.5与BUILD-INFO-0.9.5-standard。用户最终指定编辑错位横32纵32，替换试用52/44；最终构建通过，真实鼠标报告为微调前，不能冒充32/32实测。旧0.9.4及以下为历史。普通本地修改未上传GitHub。

## 2026-10-15 0.9.4本地常用版：连接恢复

本轮实际调用index_status确认taskboard ready、根目录D:/Derp/Documents/0renwumianban，旧图indexed_at=2026-10-14T21:51:15Z、4357节点/12007边、5个PowerShell部分解析。随后get_architecture、search_graph、trace_path、get_code_snippet和check_index_coverage定位；动画/外点路径metadata_changed，相关当前源码用rg和精准读取核实。

manage_adr(set_sections)已收到sections_updated回执，更新当前文档入口、已确认规则、电脑端规划、从零重写实施与交付入口五章，其余内容保留。接口章节名不带Markdown井号；一次名称格式拒绝后已按接口要求重试成功，不宣称拒绝请求已写入。

交付阶段full重新索引返回indexed，4662节点/12563边，7个PowerShell部分解析、0个不可用解析，已写.codebase-memory/graph.db.zst。随后对完成记录再次刷新并实际index_status、路径覆盖核验；最终工具原始回执保存为test-output/index-094-refresh.json与test-output/index-094-coverage.json，计数及新鲜度以回执为准，不复用旧计数冒充本轮计数。release、test-output、preserved-data继续排除，PowerShell部分解析继续由指定源码和实际隔离测试补证。

当前入口：HANDOFF、INDEX、PRD第28节U86至U90/A104至A108、TASKS第16节T109至T114、IMPLEMENTATION的0.9.4段、ACCEPTANCE-0.9.4、VERIFICATION-0.9.4、BUILD-INFO-0.9.4-standard。1.35倍已选为常用，普通修改未上传GitHub。下方0.9.3断连为历史记录；本轮成功回执完成其待补收尾。

新鲜度限制：两次full均成功后，最终index_status为ready、indexed_at=2026-10-15T00:29:56Z，但24路径的check_index_coverage仍返回metadata_changed，不能声称工具确认所有路径新鲜。新加入的CanQueueContentSwap、IsEarlyContentSwapPress及DeliverPresses均可从图检索到，精确片段与当前源码对应；相关源码SHA256与构建清单逐一匹配，隔离运行包验收通过。图可作定位入口，引用结论仍需直接源码/哈希核实；当前元数据诊断现象未作为项目程序缺陷修改。

## 2026-10-15 0.9.3本地交付：收尾连接断开，刷新未确认

本轮开始及继续工作时实际调用index_status：taskboard ready，根目录D:/Derp/Documents/0renwumianban，4357节点、12007边，indexed_at=2026-10-14T21:51:15Z；5个PowerShell部分解析、0不可用解析。随后实际调用get_architecture、search_graph、trace_path、get_code_snippet和check_index_coverage。改变文件metadata_changed，新增DraftCardGeometry未追踪，因此用rg及指定源码、隔离实际运行核实，不把旧图作为最终源码证明。

程序、两包及文档完成后，manage_adr(set_sections)返回Transport closed；再次index_status及index_repository(full,persistence=true)也返回同一错误，未取得成功回执，不能声称ADR写入或索引刷新完成。旧持久图保留；恢复连接后先核对index_status，更新当前文档入口与规则ADR并对该根目录full重新索引，检查新增几何、外点/动画和文档路径覆盖。不得把旧计数写成0.9.3的新计数。

当前可靠入口：HANDOFF、INDEX、PRD第27节U79至U85/A97至A103、TASKS T101至T108、IMPLEMENTATION的0.9.3段、ACCEPTANCE-0.9.3、VERIFICATION-0.9.3及两份BUILD-INFO。程序修订与两份476文件的完整ZIP交付已完成；索引恢复属于待补收尾，普通修改未上传GitHub。release、test-output及preserved-data继续排除。

## 2026-10-15 0.9.2本地修订

当前入口为HANDOFF、INDEX、PRD第26节U69至U78及TASKS T91至T100；实施、验收、验证摘要和便携清单对应0.9.2。122规则、61控件、77窗口、39画卷及真实外点/重复调用、球展开、横向抽牌采样通过。运行包476文件，完整ZIP逐文件校验通过，未上传GitHub。

实际调用index_status确认taskboard ready及根目录后，get_architecture、search_graph、trace_path、get_code_snippet定位，再直接核实改变的源码。manage_adr以set_sections更新当前文档入口、已确认规则、电脑端需求规划、从零重写实施与交付入口，保留其他章节。交付阶段以full模式索引D:/Derp/Documents/0renwumianban并持久化至.codebase-memory/graph.db.zst；节点/边及解析计数以工具实际结果为准。release、test-output、preserved-data继续排除；部分PowerShell或非追踪样式缺口由相关源码和隔离测试核实，图索引不替代实际画面验证。

## 2026-10-15 新会话接手入口

新增 HANDOFF.md，记录当前0.9.1交付、阅读顺序、索引调用方法、实际接入边界和独立发布克隆注意事项；AGENTS.md加入接手入口，INDEX.md的当前表格改为0.9.1/T90并澄清历史研究。该轮仅整理文档，不修改程序、不重跑桌面测试、不自动发布GitHub。接手时先调用index_status确认实际状态和覆盖，最新计数以工具结果为准。

## 2026-10-15 0.9.1完整交付

PRD第25节U55至U68/A73至A86、T77至T90、使用说明、验证摘要和验收记录已同步。新增紧凑卡片、真实外点观察、草稿编号、200毫秒扑克牌动画及实机像素检查；后台仅切换、X整组关闭与恢复原前台。114规则、45本轮隔离、17迁移、12气泡、49监听、77窗口、64真实交互、39画卷和3启动回读通过。正常产品源码与实机一致，仅调整屏幕外测试性能断言，分别记录DLL哈希及源码证明。

ADR仅更新当前入口与电脑端需求规划，保留平台专项章节。当前项目taskboard完成full索引并持久化到.codebase-memory/graph.db.zst：4194节点、11524边，无不可用解析；4个PowerShell部分解析点已直接核实；索引用于定位源码，不作为真实点击或恒定120帧证明。release、test-output与preserved-data继续排除。PowerShell部分解析与ICO排除保持直接源码核实边界；源码及证据入口以VERIFICATION-0.9.1.json和BUILD-INFO-0.9.1.json为准。

## 2026-10-14 0.9.0整合与持久保存

PRD第23、24节U41至U54/A59至A72、T64至T76、使用说明与当前交付入口已同步。索引优先定位ArchiveTask、DataPaths、StartupLinks及缩放关系后直接核实源码。新增独立系统配置、迁移及恢复、归档和恢复历史、最新版启动登记，并纳入监听/默认音、画卷和气泡；111规则、17迁移/设置、12气泡、49监听、39画卷、77窗口、49真实交互及13类实时背景移动采样通过，真实自启入口回读成功。当前证据为VERIFICATION-0.9.0.json和BUILD-INFO-0.9.0.json，真实通知音替换与飞书同步仍专项，未执行重启或正式数据迁移。

full索引刷新并持久化，3950节点、10674边，全部C#无不可用解析；orb.ico按设计排除。四个PowerShell部分解析点为Desktop-Interaction.ps1第82行、Package-Portable.ps1第43行、Verify-Desktop.ps1第23行、Verify-ModuleIntegration.ps1第22行，已直接核实并实际运行。关键C#路径无记录覆盖问题，但新鲜度工具仍返回metadata_changed，因此以直接读取及最终运行文件实测补充；不把索引作为产品验收证明。ADR只更新当前入口与“电脑端需求规划”，保留Codex和飞书专项记录。release、test-output、preserved-data与持久图排除公开代码提交。

## 2026-10-14 0.8.1通知监听

新增核心监听规则、设置分区、双通道捕获、Windows通知音设置与恢复、系统音频解码及43项隔离检查。PRD第22节、T59至T63、文档入口与独立监听说明已同步，ADR“电脑端需求规划”已更新。最终包以test-output/listening-package-02验证，真实应用捕获和原通知音消除仍未验收。full索引成功并持久化：3468节点、9264边；图标按设计排除，未发现不可用解析。三个PowerShell脚本保留部分解析边界，以直接源码补充核实。图仅用于代码定位，不作为真实声音生效的证据。

## 2026-10-14 0.7输入与卡片交互

PRD第20节U27至U31、A45至A49、任务T47至T51和文档索引已同步。用户确认仅卡片内部纵向留白缩至3/5。新增RoundedInput、OptionMenuToggle、CardDragSession和SurfaceContentRoot；修正搜索视口、关闭手势、透明子表面的自动化定位，按目标列预览拖动。56规则、最终67窗口、39真实操作和13类动态背景/移动采样通过，96圆外点与最终同步动画通过；见VERIFICATION-0.7.0。ADR只替换当前文档入口、已确认规则和重写实施，保留独立Codex及飞书专项记录。

交付前以full模式刷新并持久化.codebase-memory/graph.db.zst；覆盖缺口仍通过直接源码与隔离实测补充，索引成功不等于功能通过。正式data与preserved-data未操作，发布目录和隔离产物排除索引。

本轮full结果：2831节点、6620边，未索引文件0、不可用解析0；三处PowerShell部分解析为Desktop-Interaction.ps1第78行、Package-Portable.ps1第40行、Verify-Desktop.ps1第23行，均已直接核实。七个关键路径覆盖无记录问题，源码仍返回metadata_changed、Theme.xaml为not_tracked，继续以本轮直接源码和实测证据为准，不将索引元数据当成完整性保证。

## 2026-10-14 Codex独立模块验证

新增tools/codex_probe与tests/codex_probe，13项规则/通信检查通过；两个真实临时会话及4轮次验证结束、提问恢复、运行后关联、交错会话和权限请求。CODEX-PROBE-RESULTS.md与CODEX-PROBE-20261014.json为实测入口。当前桌面编号可读，共享代理未接通，另起服务返回notLoaded；不虚称正式面板已接入。PRD、任务与文档入口已同步；ADR和taskboard持久索引随本轮刷新。

## 2026-10-13 0.6实时背景与输入表面

PRD0.6、U22至U26、A40至A44及T43至T46已同步；撤回0.5将窗口区域视为无方板证明的结论。LiveBackdrop提供同窗口实时局部背景，MotionChecks记录动态画面、原生隐藏事件和圆外像素；静态CaptureBackdrop已移除。ADR已同步当前规则和证据边界。56规则、53最终包窗口、23实操，具体构建边界见VERIFICATION-0.6.0.json。正式data与preserved-data未操作；干净发布目录与受测运行文件逐文件一致后打包。

图关系用于定位，Theme.xaml和PowerShell缺口仍直接读源；源码、文档及ADR在交付前full刷新并持久化，下文0.5及以前记录为历史。后续图回传的节点、边和解析覆盖以本次MCP结果为准。


## 2026-10-13 Codex接入技术探索

已同步PRD第19节R85至R87、D12、A18、C01至C04和文档索引，新增CODEX-INTEGRATION-RESEARCH.md。用户已确认仅进行中与待操作双向自动流转，时间提醒右上红点保留至主动操作任务。Codebase Memory ADR追加“Codex会话关联和自动流转”节，保留原有章节；taskboard full索引已刷新并持久化至.codebase-memory/graph.db.zst，未索引文件0、不可用解析0，PowerShell两处部分解析保留直接源码核实边界。该记录不表示Codex实际接入完成。

## 2026-10-13 0.5单宿主和配置备份重构

PRD0.5、U13至U21、A31至A39、T36至T42以及最大范围术语已同步。EditorSurface从真实窗口改为编辑控件，TaskEditorWindow只包已有任务，DraftStackSurface包全部新建草稿；旧DraftAffordanceSurface已移除。新增ConfigurationBackup及DataPaths，设置与窗口层级、透明度、滚动模板已重构。ADR已同步新规则和53/48/23测试边界；交付前full刷新并持久化，结果见本轮MCP调用。

仍以直接源码和隔离证据补充图关系；Theme.xaml和部分解析的PowerShell直接核实。发布包、测试数据和preserved-data排除索引。当前入口VERIFICATION-0.5.0.json、BUILD-INFO-0.5.0.json，历史记录不冒充本轮验收。

## 2026-10-13 0.4界面修订索引

PRD0.4、U01至U12、任务T29至T35、A19至A30及交付记录已回写。当前源码新增DraftAffordanceSurface、VisualChecks和可复用打包校验脚本；草稿堆叠、统一圆角、阶段输入、悬浮球DPI及窗口恢复均更新。Codebase Memory的“已确认的产品规则”和“从零重写实施”段已同步；交付前重新索引并持久化图文件，结果见本次MCP调用。

使用图定位后直接核实相关源码；索引覆盖和新鲜度不是验收证明。WPF资源Theme.xaml及可能部分解析的PowerShell脚本直接读取核实。隔离数据、发布二进制及preserved-data均排除活跃索引；最新验收摘要为VERIFICATION-0.4.0.json，历史0.3证据仍保留。

本轮full索引成功并持久化：2048节点、4324边，未索引文件0、不可用解析0；部分解析为Desktop-Interaction.ps1第64行和Package-Portable.ps1第39行，两处已直接核实。四个关键窗口源码覆盖无记录问题，但新鲜度信号仍返回metadata_changed，因此没有把图当作源码与实际测试的替代。最终文档记录补齐后再刷新一次索引。

## 2026-10-13 新版源码索引

用户已授权从零开发电脑端。新源码位于src/Taskboard.Core与src/Taskboard.Desktop；规则与控件检查位于tests及DesktopChecks.cs。PRD0.3、任务清单、实施记录和新版使用说明已更新，D01至D11已确认，D12仍为后续专项。

已通过MCP执行full索引并写入.codebase-memory/graph.db.zst，覆盖新版C#源码。交付前检查记录1912节点、3859边，未索引文件0、不可用解析0；桌面测试脚本tools/Desktop-Interaction.ps1第64行有部分解析，已直接读取核实，不以其图关系作为验证证据。已使用search_graph、trace_path和get_code_snippet定位提交与保存链路；coverage仍返回metadata_changed，图中同名方法可能误关联，相关结论以直接读取源码和隔离测试补充核实。preserved-data、历史图快照、bin/obj、release和test-output排除活跃索引。

以下为2026-10-12的历史刷新记录，不表示当前仍未开发。

日期：2026-10-12。索引项目：`taskboard`。

本地文档入口已更新：`docs/INDEX.md`、`docs/PRD.md`、`docs/TASKS.md`，项目协作入口为`AGENTS.md`。

MCP工具连接返回`Transport closed`。已改用本机Codebase Memory CLI，使用现有配置中的运行目录完成全量索引，持久化文件已更新。结果为`indexed`，PRD和任务清单均已纳入仓库索引。

既有索引限制：`tools/DesktopCheck.ps1`第44和59行部分解析；发布二进制、构建输出、隔离测试和压缩包不作为可分析源码。这些限制不涉及本次需求文档。

已通过文件章节与符号检索验证PRD进入索引。覆盖辅助检查仍返回`metadata_changed`，不能据此保证所有新鲜度信号正常；需求内容以本轮读取核实的落盘文件为准。路径受限的文本检索未返回标题，文件章节检索可正常定位。

最新文档包含12模块、28项电脑端任务、80条需求和19项验收。用户已确认手动选择当前阶段、灵感库保留全部阶段，以及已建任务保存才生效、X放弃、悬浮球收起保留编辑、点击窗外不关闭。

## 从零重写清理后的索引

旧程序的20个源码配置脚本已拆解，30组片段、架构及181方法导出、REA静态记录和历史图快照均归档于docs/legacy。旧源码和运行包已从当前工作区删除；preserved-data中的4个个人数据文件与旧图快照排除活跃索引。

当前taskboard索引将重新对应需求、重写思路、任务映射和历史文档。它不应把旧C#类作为当前可实现源码；旧符号通过历史方法导出查找。删除前图快照不能覆盖当前图。最终刷新与检索结果另存于docs/legacy/evidence/final-verification.json。

该记录只说明文档和代码索引维护，不代表新版程序已开发或验收。

0.6最终full回传：2404节点、5605边，持久化成功；全部C#可用，Desktop-Interaction.ps1第74行和Package-Portable.ps1第40行部分解析，使用源文核实。
