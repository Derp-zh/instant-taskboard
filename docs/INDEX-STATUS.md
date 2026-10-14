# Codebase Memory 文档索引刷新记录

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
