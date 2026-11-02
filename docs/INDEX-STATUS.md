# 当前索引与摘要同步状态

## 2026-11-02 0.9.30 接入取舍与设置整理

本轮实际执行index_status→架构→符号／trace／片段，旧行号、同名误边与新增文件缺口均用定点源码补证。完成源码、独立包及文档检查后才full/persistence刷新：ready，根`D:/Derp/Documents/0renwumianban`，indexed_at `2026-11-02T15:25:14Z`，10495节点／30808边，18部分解析、0不可用／跳过。CodexFollowPolicy、PeekSnapshot和CheckTerminalDisplay新符号命中，PeekSnapshot的365至375行已回读。

精确路径复核仍报告CodexState／BoardEngine／CodexObserver／SettingsAiInitialization为metadata_changed、C01／M11为missing；这些原文件已直接核对，不反复重建或冒称全部新鲜。刷新／覆盖／符号回执在`output/0930/index-final.json`。ADR只改当前交付、源码定位和能力边界三个节，完整预期文本回读逐字一致；需求规划／维护／DSH原节保持。实际摘要存[同步JSON](CODEBASE-MEMORY-SYNC.json)，本版产品证据见[验收](ACCEPTANCE-0.9.30.md)。封包与最后同步回执晚于索引的部分不声称已索引；output／test-output／release／preserved-data继续排除。

## 2026-10-24 0.9.28独立评审

本轮MCP实际可用，已执行index_status→架构→符号→trace→片段；根正确、ready、9995节点／28836边，indexed_at仍为2026-10-23T18:34:33Z。RunLocalEvents索引旧行号179与当前203不符，新增CodexFileActivity无符号命中，且trace含同名误边；因此全部关键结论用定点源码和独立合成程序补证。未改产品源码，未重建索引或更新ADR摘要，不冒称0.9.28覆盖新鲜。新增反例及复现入口见[独立评审](REVIEW-CODEX-0.9.28-20261024.md)，output／test-output仍排除。

## 2026-10-24 0.9.28活动轮次订阅与本机文件投影

本轮未能实际调用Codebase Memory MCP：当前会话未提供该MCP工具（仅有本地shell与文件读写），因此index_status／get_architecture／search_graph／trace_path／get_code_snippet均未在本轮执行，索引与摘要**未刷新**，不能声称已同步或新鲜。替代核验方式为：直接读取现行文档与源码（CodexObserver／CodexLocalState／CodexLocalCatalog／CodexState／CodexLocalEvents）、按ASAR完整性校验解出已安装Electron成员做定点源码核对、以及新增的真实本机只读探针（`--activity-probe`，只读state_5／thread_history_1结构，不读正文、不联网、不启停Codex）。下一次有MCP的会话应先实际index_status→架构→符号，再对CodexFileActivity／CodexObserver.RunLocalEvents／CodexState.probe/turnProjected做覆盖缺口补证；output／test-output／release／preserved-data继续排除。

## 2026-10-24 无订阅读取接续调查

先实际核对Instant-taskboard根及ready状态，9825节点／28505边、18项部分解析，再架构、符号、trace和Connect片段。三个相关产品路径check_index_coverage仍metadata_changed；直接源码及0.9.27构建清单23项SHA256已核对，未将ready解释为全部新鲜，trace的UI同名误边已排除。现行文档与研究工具新增后才刷新索引；最终实际结果存`output/codex-passive-20261024-review/index-final.json`，摘要只更新当前交付／定位／能力三个相关节，DSH节保持。产品代码与包不改，证据入口[接续验收](ACCEPTANCE-CODEX-PASSIVE-20261024.md)；output／test-output／release／preserved-data继续排除，收尾回执不冒称全部已索引。

## 2026-10-24 0.9.27目录离线与被动监测研究

本轮先实际index_status核对根／覆盖，再架构、符号、trace和片段，关键源码直接补证。代码和现行页更新后刷新full/persistence，ready，2026-10-23T16:50:49Z，9825节点／28505边、18部分解析、0不可用／跳过。新CodexLocalCatalog.Read符号与8行源码已读回。ADR仅更新3个相关章节，回读并确认DSH原节未变化；同步JSON存实际全文。后补包回执／测试宿主细节不宣称已索引，不重复重建；output／release／test-output／preserved-data排除保持。

## 2026-10-23 0.9.26自动连接与目录恢复

本轮实际index_status→架构→LoadConversations/RunLocalEvents符号、关系和片段定位；空关系/旧覆盖用rg和直接源码补证。修改及文档检查后full/persistence刷新，最终ready，indexed_at 2026-10-23T13:15:33Z，9609节点/28011边、18部分解析、0不可用/跳过。CodexCatalog.Load新符号可检索；Catalog/Observer/ColumnSurface精确路径仍metadata_changed，已定点核实，不反复重建冒称完全新鲜。ADR已更新0.9.26本地/0.9.23公开及本轮模拟/离屏边界，整份回读逐字一致，同步为synced。最终包/测试回执及本段后补，不宣称已索引；output/test-output/release/preserved-data排除保持。

## 2026-10-23 0.9.25本机状态与流量修复

本轮先实际index_status核对ready/根/覆盖，再架构、符号、路径与片段定点定位旧轮询；修改及文档检查后full/persistence刷新。最终ready，indexed_at 2026-10-23T11:36:02Z，9458节点/27562边，18部分解析、0不可用/跳过。新CodexLocalState符号和Apply片段已读回；两新源码路径仍metadata_changed，C01仍missing，已直接核实现行文件，不重复重建冒称全部新鲜。output/test-output/release/preserved-data排除保持。

ADR已更新0.9.25本地/0.9.23公开、C01新本机路线、职责与未验范围，并完整回读逐字一致；同步JSON为synced。包/最后测试回执及本段在刷新后补记，不声称这些收尾内容已索引。产品证据看[0.9.25验收](ACCEPTANCE-0.9.25.md)，索引/文档检查不冒充产品验收。

## 2026-10-23 DSH只读复核

实际index_status为ready、根D:/Derp/Documents/0renwumianban、9273节点/26914边、18部分解析/0不可用；随后get_architecture、search_graph、trace_path、get_code_snippet定点定位Codex聚合/阶段模型/Controller。3条精确源码仍metadata_changed、C02诊断missing，直接核实源码与现行页；图中无关同名误边不作依据。没有修改产品源码或运行包，状态与路线只看[C02](current/C02-DSH接入探索.md)。本轮文档更新后的校验、索引和摘要结果见`output/dsh-research-20261023/docs-verification.json`与`index-final.json`，不复用下方旧结果。

现行文档更新并通过完整性检查后，按name=Instant-taskboard/full/persistence=true刷新；最终ready，indexed_at 2026-10-22T21:10:29Z，9307节点/26948边、18部分解析/0不可用/0跳过。C02新增“公开API路径补查”可检索；精确路径诊断仍metadata_changed/missing，以本地定点核实为准，不反复重建。ADR新增C02权威路由及能力边界并整份回读逐字一致；本段与同步JSON收尾字段在刷新后记录，不声称已覆盖其新鲜度。output、release、preserved-data继续排除。

## 2026-10-23 0.9.24主开发集成

本轮实际先index_status核对Instant-taskboard、ready及根D:/Derp/Documents/0renwumianban，继而get_architecture、search_graph／trace_path／get_code_snippet定点分析；缺口和图同名误边用源码补证。修改与文档校验后按name=Instant-taskboard、full、persistence=true刷新。最终ready，indexed_at 2026-10-22T18:25:35Z，9273节点／26914边，18个部分解析、0不可用／0跳过；FinishConversationInput新符号可检索且片段读回匹配。

引用的三个源码仍metadata_changed、X01／M09诊断仍missing，已直接核实现行内容，不反复重建宣称全新鲜。output、test-output、release和preserved-data继续排除。回执output/drag-integration-20261023/index-final.json；包清单及本页等收尾回执在刷新后补记，不声称已全部覆盖其新鲜度。

ADR工具本轮实际可用，已读取旧摘要、更新为0.9.24本地／0.9.23公开和current权威路由，逐字回读一致；[同步记录](CODEBASE-MEMORY-SYNC.json)为synced。下方旧断连／pending均是历史时点，不代表本轮状态。


## 2026-10-23 拖动绑定集成交接文件（准备阶段历史）

实际调用index_status和get_architecture均返回`Transport closed`，本轮未连接、未刷新索引或摘要；定向读取本地X01、测试验收与功能门控源码补证。仅新增并入主开发版的提示词/实施交接/源文件清单及相关导航，不执行集成、构建或产品回归。新文件尚待工具恢复后索引，不能用下方旧ready状态代表本轮已连接。入口：[集成提示词](DRAG-BINDING-INTEGRATION-PROMPT.md)、[实施交接](DRAG-BINDING-INTEGRATION.md)。

## 2026-10-22 拖动绑定独立试验

接手实际index_status为ready、根目录正确，随后架构、TaskCard.Heading符号/关系/片段定位，源码路径metadata_changed，使用直接源码补证。r2接续复核为8907节点/26236边，新菜单和剪贴板类not_tracked，确认变更后才执行full刷新并持久化。

最终复查ready，根`D:/Derp/Documents/0renwumianban`，indexed_at `2026-10-22T13:54:04Z`，9083节点/26697边，15个部分解析、0不可用。CodexConversationMenu.Resolve/TryLocate均可检索；菜单/剪贴板源码路径仍metadata_changed、X01仍missing，已按本地现行文件核实，不反复重建宣称完全新鲜。output和preserved-data继续排除。回执`output/drag-binding-20261022/index-r2-verification.json`；本页及本地摘要回执在刷新后补记。

本轮manage_adr的outline调用被工具权限策略拒绝（`MCP tool call requires approval, but approval policy is never`），没有绕过策略读写摘要；待提交路由保留pending，不称摘要已同步。普通产品基线沿用已有记录，拖动绑定仅独立试验，权威入口[X01](current/X01-拖动绑定探索.md)及[实测边界](DRAG-BINDING-PREVIEW-GUIDE.md)。

## 2026-10-22 Codex连接自检与适配

先实际index_status（Instant-taskboard、ready、根正确、8751节点/24910边、11部分解析），再架构、初始化符号、Run关系/片段，直接源码补证同名误边。代码变更后已按name=Instant-taskboard、full、persistence=true刷新，复查ready、根正确，8547节点/25360边、12部分解析/0不可用/0跳过；TestAiConversations能检索。output安装提取与生成证据整体排除。发布回执文档在刷新后补记，不声称其新鲜度已全部验证；摘要工具缺失，pending。

首轮刷新参数名错误产生路径自动别名，已纠正指定项目并重新持久化。多余别名的删除接口被工具审批策略阻止，未删除；日常仅使用Instant-taskboard，不使用路径别名。根工作区还有并行的未发布拖放实验，公开0.9.23使用独立审查副本，不以根全量源码冒充发布源。


发布补记：0.9.22源码/包已上传并下载核验；随后更新了发布文档和待提交摘要。下方索引刷新为开发时点，不声称覆盖这次发布文档的新鲜度；摘要工具仍缺失，pending保持。

鼠标补验收记录：仅新增外部测试工具和文档，产品源码/发布包未改；目标X与一次主屏悬停19项/39帧见ACCEPTANCE-0.9.22-mouse.md。本节不声称索引已刷新覆盖新增脚本或文档，摘要仍pending。

## 2026-10-22 阶段删除与设置优化

本轮先实际index_status核对ready、根目录及8524节点/24431边/11部分解析，再读架构、RenderStages关系和BallSurface.EdgeHide片段；四个精确源码路径仍metadata_changed，以直接源码核实。新增RemoveStage、SettingsLayout及相关规则后刷新结果见`output/ux0922-20261022/index-refresh.json`；摘要工具仍未提供，待提交路由已更新至0.9.22，pending不冒称同步成功。

已full刷新并持久化，复查ready、根正确、8751节点/24910边、11部分解析/0不可用；RemoveStage、OrganizeSettingsPage、ApplyEdgeBadgeFrame均可检索。三个精确路径的metadata_changed/missing诊断仍有限制，已用现行文件核对，不重复刷新声称修复诊断；详情见`output/ux0922-20261022/index-verification.json`。

## 2026-10-22 AI初始化

实际依次调用index_status（ready、根目录正确、8315节点/23501边、11个部分解析文件）、架构、search_graph、AttachCodex关系和桥源码片段；图中出现同名误边，直接源码补证。新增初始化路径覆盖实际返回not_tracked，桥metadata_changed、C01 missing，确认本轮代码/规则变动后索引需刷新，刷新回执见`output/codex-init-20261022/index-refresh.json`。

本轮工具没有摘要读写接口，`CODEBASE-MEMORY-SYNC.json`只更新待提交路由，状态仍pending，未假称已同步。安装产物核验目录已明确排除索引，避免把提取的Codex源码当本项目实现。现行规则仍以本地current为准，索引成功不代替源码新鲜度或原桌面能力验收。

代码与规则变更后已实际full刷新并持久化，复查ready、根目录正确、8524节点/24431边、11部分解析/0不可用；新增初始化类和设置方法可检索。覆盖诊断仍把两个新源码报metadata_changed、C01报missing，直接源码及现行页已核对，不反复重建冒称完全新鲜。完整状态/精确路径诊断见`output/codex-init-20261022/index-verification.json`。

## 2026-10-21 DSH定向探索

实际调用index_status核对ready、根目录正确，随后使用架构/符号/关系/源码片段。5条源码覆盖检查metadata_changed，关系图存在同名误边，以直接源码补证。新增现行[C02](current/C02-DSH接入探索.md)后刷新结果见`output/dsh-research-20261021/index-refresh.json`。本轮工具列表没有项目摘要读写接口；原摘要中“本次文档整理”等范围已不能代表本轮探索，摘要同步明确待后续有工具时完成，不能声称已更新。现行需求直接读本地current。

## 此前文档整理回执

2026-10-21，产品0.9.20-widthfix，文档与职责整理。

- 初次index_status/get_architecture实际返回Transport closed；稍后恢复，实际核对Instant-taskboard、根D:/Derp/Documents/0renwumianban、ready，基线7961节点/23113边。
- 已依次使用架构、search_graph、trace_path及get_code_snippet定位保存、外点、后台移列、ClearClosed和文字宽度。五条精确C#路径仍metadata_changed；关系图含疑似同名误边（SubmitSessionLocked下出现无关Stop/Start/Release），均以直接源码核对实际链路。
- 已完成full重建并持久化，实际index_status为ready，根目录正确；2026-10-20T22:31:58Z，8064节点/23246边、11个PowerShell文件部分解析、0不可用。历史目录docs/history确认排除。现行17页均能从图中定位，共66个节点；新建/编辑M04已实际读回全文。
- 摘要已整份替换为现行入口、当前能力边界和维护要求，重建后再次读回逐字一致。结果见[同步状态](CODEBASE-MEMORY-SYNC.json)。
- 覆盖诊断仍有工具限制：M04/M05路径显示freshness=missing，其余抽查路径metadata_changed；同次图检索/正文读回已能取得M04，因此不能把此诊断解释成已完全覆盖或完全未索引。当前规则直接读取本地文档，源码引用定点核实；不反复重建冒充修好工具诊断。
- ready不证明摘要有效，索引成功不代表规则语义已合并。现行行为只以[current](current/README.md)为准；索引摘要只存当前版本、权威路径和能力边界，不保留平行生效的旧“当前交付”。

此前索引记录只在追溯时读[原始记录](history/20261021-before-current/INDEX-STATUS.md)。本次未启动产品、操作正式数据、自启、桌面鼠标或用户Codex。
