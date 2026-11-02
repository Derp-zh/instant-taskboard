# C02 DSH接入探索

核对日期：2026-10-23（复核10月21日定向调查并补查公开API访问路径）。**仅探索获授权；接入路线待用户选择，产品监听未实施。** 用户报当前正式本地版本0.9.19；接手文档当前记录公开0.9.23、本地开发0.9.24。三者用途分开，未启动或替换看板，实施前核对实际目标包，不自动升级正式包。

## 本轮目标与边界

- C02-01：调查原DSH Desktop真实结构化状态及现有看板接入点，不通过聊天正文、窗口文字、截图或文件更新时间推断执行状态。目录、通道、来源、准确会话、原桌面生命周期、实际卡片及保存分别验收。
- C02-02：待验证目标是：真实开始/恢复→进行中；选项、自由输入、计划审阅、批准等待→待操作；回答同卡或批准后，只有准确同轮实际执行才可恢复。正常整轮结束、整轮失败、用户停止、额度/服务限制致整轮停止→待操作。命令报错后仍执行不算整轮失败。
- C02-03：目标沿用[C01](C01-Codex接入.md)的运行阶段、仅内置两列、多阶段等待优先、断线/Unknown保护、旧轮次隔离、红点、保存/编辑合并及解绑重绑规则；具体实现不能复制Codex专用观察桥或假造Codex事件。DSH与Codex混合阶段也要共同聚合，另一来源运行不能覆盖等待。
- C02-04：本轮只读，不向现有DSH聊天发消息、回答或批准，不切前台/移动鼠标，不退出重启DSH/Codex，不操作正式数据/配置、自启及preserved-data。后续专用聊天和隔离`--data-dir`测试先协调；不主动耗尽额度，不自动上传GitHub。

## 已核实的接口与证据

### 安装与运行身份

- 现存进程路径`C:/Program Files/DSH Desktop/DSH Desktop.exe`；文件版本2.0.18，产品版本2.0.18.0。安装包`resources/app/package.json`与之相符，Electron桌面壳组合Cordis插件；包含`@deepseek-ai/dsh` 0.2.1-rc.2及`@agents-anywhere/dsh-bridge-next` 2.1.0-desktop.c39b82d4fffb1.r2e406d2a。桌面项目为[社区开源项目](https://github.com/anywhere-labs/dsh-desktop)，不能把桌面桥当作DeepSeek官方长期稳定契约。
- 实测已有本机127.0.0.1 TCP、逐行JSON-RPC、token握手成功，identity.runtime=dsh，runtimeVersion=0.2.1-rc.2，protocolVersion=1.0，projectionVersion=3、syncMode=events。功能声明approval/userQuestions=true只证明声明。
- 固定发现文件`C:/Users/Derp/.agents-anywhere/dsh-bridge/endpoint.json`与兼容文件`C:/Users/Derp/.dsh/agents-anywhere/bridge/endpoint.json`的host/port/token/pid均相等；没有记录token。10月23日以不提权Win32只读API实际核验：127.0.0.1:7128监听PID=50672，与端点、现存DSH Desktop路径/版本一致，同当前用户、同Windows Session=1。CIM/Get-NetTCPConnection仍拒绝，但不再构成此次来源身份缺口；不代表生命周期验收。PID/端口只是本次观察，未来重读发现文件，不能复用。

### 已实际调用

| 接口 | 本轮结果 | 能证明什么 |
| --- | --- | --- |
| initialize / doctor | 成功，版本和能力声明与安装产物相符 | 已连接既有本机桥；来源另经系统监听PID/路径/同用户/Session核验 |
| session.list（分页） | 54个sessionId/externalSessionId准确双ID | 可发现目录；live/persisted不代表执行中 |
| session.getState（精确双ID） | 54个idle/available，3个attached | 能按准确会话读取空闲状态；attached不等于执行中 |
| session.getNotices（3个attached） | 均为空 | 只证明接口可读，未证明真实问题/批准交付 |

以上4项已于10月23日重新实测，仍为54个空闲会话、3个attached、3组notice为空。没有调用getSnapshot、全量同步订阅或消息/回应接口，没有读取聊天正文。**桥本身支持写操作，当前调查客户端仅调用只读白名单，不把桥声明readOnly=false当作此次写入。** 本轮脱敏证据：状态样本（本地归档）、来源核验（本地归档）、只读复现脚本（本地归档）、来源复现脚本（本地归档）。10月21日同名证据保留原时点。

### 公开API路径补查（10月23日）

- 安装随包`dsh-client-connection/README.zh.md:41–53`明确提供`GET /connect?token=...&interactions=none`被动API连接，以及`/api/remote.mux`流；`none`不参与回答。它仍需合法认证，不能拿agents-anywhere端点令牌直接套用，独立CLI也不自动拥有原桌面Host连接。
- `session/follow`按准确地址先挂增量监听，再返回快照和有序事件；`session/projections`返回`sequenced/asOfSeq`或`migration-required`。这比全量聊天同步更适合精确绑定，但仍传正文/其他投影，未发现仅取状态字段的过滤。当前安装代码`session-controller/lib/index.js:1510、3281`及[上游公开说明](https://github.com/deepseek-ai/deepseek-harness/blob/master/packages/api/session-controller/README.md)相互印证；上游master不作为本机版本替代。
- 当前桌面壳另外有generation级原生renderer capability；`desktop-browser-access-5-Ph3Uv7.js:47`在普通浏览器访问关闭时拒绝非原生访问，[桌面项目说明](https://github.com/anywhere-labs/dsh-desktop/blob/master/dsh-desktop-next/README.md)也声明这层隔离。没有取原生capability、浏览器cookie、凭据文件或绕过该控制。
- 对已核实归属同DSH进程的4个非桥端口，各一次无凭据HEAD `/`：43120返回HTTP 401；7115/7127/7129为HTTP解析错误。**只证明存在受控HTTP响应，尚不能确认43120就是可用官方Gateway，更不能诊断普通浏览器访问开关当前值。** 没有读响应正文/头、跟随重定向或建立WebSocket。见监听归属（本地归档）、HTTP证据（本地归档）。
- 公开API仍需证明：受支持的地址/认证发现方式、不接管问题的被动连接、完整pending（含continued/Plan/批准）、同轮真实恢复、重连补齐。现阶段不能把官方API写成“已连接”或保证不需要插件。
- 本轮REA实际分析仅`dsh-api-gateway`安装目录：18相关文件、15个JS、0解析失败；语义图因`lib/types/index.js`结果预算而partial。parent Evidence `ev_796f73eaa5f442199bfe2bc486e36a788477f565b60290a94e09126575986581`，并用7个定点安装文件直接补证，见文件哈希与范围（本地归档）。静态证据不能代替运行往返。

### 安装产物静态核查

以下主要为10月21日安装代码/随包文档定点结论，10月23日版本未变，并复核getState/getNotices、turn/step/end、userQuestions及follow/projections关键范围，**没有证明当前原桌面真实往返已经通过**。桥包根为`C:/Program Files/DSH Desktop/resources/app/node_modules/@agents-anywhere/dsh-bridge-next/`。

| 来源 | 核实结果及限制 |
| --- | --- |
| 桥lib/index.js:6622、6671 | getState把原生问题/批准合成waiting_approval；getNotices返回问题与批准集合。getState不提供turnId或原子完整pending快照 |
| 桥lib/index.js:4813、4849、4865 | 问题notice有eventId/noticeId、revision、responseRequired；input_request及source.component区分普通问题与plan_review。批准另为interactionType=approval。notice无turnId，closed可能是他端回答或中断，不能当执行恢复 |
| 桥lib/index.js:5597；dsh-agent-loop/lib/index.js:790 | status来自agent.status；idle/maintenance以外为running。等待问答/批准时agent可能仍running，因此请求消失+running不能单独证明恢复 |
| 桥lib/index.js:6489、6159、6457、6327 | runtime.sync.subscribe初始遍历可见聊天并同步内容，未发现按绑定ID且仅状态的过滤参数；同步timeline过滤turn.start/end；结束通知有outcome/时间但无turnId。不能把一次订阅成功当准确同轮事件验收 |
| 桥lib/index.js:6768、3775、3800 | 单会话getSnapshot有turn.start/end、turnId及source.seq，额度/失败原因可随turn.end保留。但它读/投影整个会话，limit只裁尾，不是增量状态接口；轮询可能漏掉短轮次和等待。正常未知reason不应直接套桥的done默认值 |
| 随包USER_QUESTIONS.md | 普通/选项/自由输入及plan-review来自user-questions/request；批准来自approval/request；Gateway重放eventId。文档描述恢复能力，不能替代本轮真实验收 |
| @deepseek-ai/dsh-agent-loop/lib/index.js:945、970、1029 | 原生turn/start、step/start、turn/end含准确turn、seq。turn/end.reason明确整轮completed/error/aborted等；普通tool/result错误不是整轮终态。blocked/max-tokens等须单独映射，不默认完成 |
| @deepseek-ai/dsh-user-questions/lib/index.js:48、267、536 | 官方userQuestions投影有callId与active集合，含open/continued；超时仍可回答的问题可继续存在。桥只跟踪前台waterfall等待，不能据此声称覆盖所有continued等待；须专项验证 |
| @deepseek-ai/dsh-api-session-controller/lib/index.js:1510、3281；typert.host.js:1554、1749 | 官方session/follow为单准确会话有序快照+事件；session/projections返回sequenced/asOfSeq或migration-required。返回还含正文/其他投影，未发现状态字段专用过滤。本轮只静态读其声明/实现，没有建立官方Gateway连接，也未验证认证、权限或运行中可用性 |
| @deepseek-ai/dsh-api-remotes/lib/index.js | 公共转发白名单包括api-session/status/error、user-questions/request和approval/request；没有直接完整转发所有agent/step/原始session事件。不能凭白名单宣称外部客户端已有全部恢复信号 |
| 技能scripts/client.mjs及dsh.mjs | 现有客户端丢弃无RPC id通知；wait针对脚本自己发出的clientMessageId查snapshot。不能原样用作任意原桌面聊天监听器 |

REA桥包静态分析parent Evidence：`ev_156bc68eeb08f4a98749eb95178a6a3bad4a5284d69e781ff29e4021f3ba19bb`；路由source-map原文件身份Evidence：`ev_d49b7508ddeae159b4384f77b1de23a4d7e8c7b51b7f6328f3eefb3683e2122d`。7个相关文件、2个JS，无解析失败；语义图达到100000节点上限而partial，定点源码补证，不能声称完整调用关系均已证明。静态证据（本地归档）。

## Codex参照与DSH CLI补查

Codex只作为分层架构和已确认行为的参照。当前没有选择接入路线；不能直接复用Codex特定版本的内部观察桥。

- Codex现有实现是三层：CodexDesktopBridge只读连接已运行原桌面，通过read_thread读取准确会话的状态/轮次；CodexPendingBridge额外读取原桌面的问题/批准集合和执行标记；CodexState及BoardController完成等待优先、移列、提醒和保存。CLI的app-server/proxy用于目录及可用的共享服务/回退，独立历史服务不能冒充原桌面实时状态。依据：`src/Taskboard.Desktop/CodexObserver.cs:89`、`CodexDesktopBridge.cs:104`、`CodexRpc.cs:28`及[Codex观察桥说明](../CODEX-PENDING-BRIDGE.md)。
- 10月23日当前shell的`Get-Command dsh -All`无PATH命中；10月21日曾存在的`C:/Users/Derp/AppData/Roaming/npm/dsh.cmd`本轮已不存在，旧0.1.5-rc.2不能再当当前本机版本。未扩大磁盘查找或执行旧shim。
- 本轮实际对桌面安装内`@deepseek-ai/dsh/lib/bin.js`执行`--version`得到0.2.1-rc.2，`--help`仍列出web/headless/tui和插件管理等启动/管理用途；没有启动profile或resume。10月21日SDK入口静态说明为启动独立stdio JSON-RPC runtime，不是已经证明可旁观原桌面的连接命令。见本轮CLI证据（本地归档）。
- 有CLI、能够resume同一日志或运行一个SDK进程，都不自动证明可读取另一个正在运行的桌面Host的实时问题/批准/恢复。当前未发现已验证的CLI旁观命令，不能据此断言所有CLI插件/官方接口都不支持；准确连接原桌面Host的只读路径仍需继续定点核查。
- 此次只执行版本/帮助，无profile启动、resume、发消息、回答/批准、用户配置修改或产品测试。

## 目标覆盖与缺口

| 目标 | 当前可信依据 | 缺口/本轮验收 |
| --- | --- | --- |
| 原聊天开始 | 原生turn/start+准确会话/seq | 静态可行，未观察真实开始 |
| 普通/选项/输入/Plan等待 | Gateway请求ID、notice类型；原生userQuestions集合补continued | 无活跃样本；原子完整集合、轮次归属和超时继续需验证 |
| 批准等待 | approval/request及approval/asked、decided | 无活跃样本；回答/批准不直接清等待 |
| 同卡作答后真实恢复 | 同轮、请求已结清、晚于回答/关闭的实际执行事件组合 | 现有getState/notice不够。step/start可晚于同一步内继续执行，需工具恢复/LLM请求边界等候选，不能仅靠agent.status或回答成功 |
| 正常结束/整轮失败/停止 | 原生turn/end.reason+turn/seq | 未实测。历史冷状态idle/error不能作为新终态移列 |
| 额度/服务限制整轮停止 | 真实turn/end error及结构化错误码/取消原因候选 | 未核实各服务映射，不猜错误正文/余额；若只是重试保留执行；不主动耗尽额度 |
| 普通命令错误后继续 | tool/result与turn/end层次不同 | 静态可区分；原桌面继续执行未验收 |
| 多阶段/断线/旧轮次/红点/编辑保存 | 沿用项目规则并拓展跨来源聚合 | 看板未实施，全部未验收；连接恢复不清已确认等待 |

## 待选择方案

以下是基于当前实际缺口的候选，不代表用户已选。**建议A先做最小状态原型**，通过准确恢复/完整pending门槛后再接看板；若用户优先避免新增插件，可选择B先验证公开API。桥getState+notice本身不足以交付全部目标，不建议当完整版上线。

| 方案 | 实际效果 | 成本与限制 |
| --- | --- | --- |
| A 最小只读DSH插件（推荐） | 用受支持Cordis插件机制订阅原生Session/Agent事件，结合准确投影与请求身份；只向看板输出已绑定聊天的状态编号，不传问题/答案/命令正文。现有桥可辅助目录 | 需新增、加载插件并协调专用聊天验证；官方SDK为rc版本，需版本/协议兼容检查。恢复边界及完整集合能力须先原型证明，未保证全部信号；是否可热加载待验，不擅自重启。避免依赖Electron私有对象或复制其capability |
| B 先验证官方被动API | 合法认证后以interactions=none连接原桌面Host，按准确聊天follow并读取投影；若覆盖足够可不新增插件 | 尚无受支持认证/地址发现实测，可能需要用户开启本机浏览器访问；会在本机接收绑定聊天的正文与投影，接收层可立即丢弃正文，但这不等于状态专用接口。完整pending/恢复仍未证明，失败后需重新选择A或C |
| C 保留调查，暂不实施 | 不增加产品行为 | 等桥提供按聊天且仅状态、准确轮次、完整pending及执行恢复事件后复查 |

若另选“现有桥接保守版”，只能做部分等待提醒；依据不足时保留待操作，短等待/短轮次仍可能漏检，不能满足本轮完整自动双向目标。截图/OCR/文件时间推断及Codex内部观察桥均不采用。A/B都先隔离验证，绝不因接口名称/版本相同就宣称目标全支持。

A的拟议状态协议至少需要：来源版本/实例epoch、准确sessionId、turn及其起始序号、严格递增seq、绑定代次、完整pending集合（ID/类型/所属轮次/完整性及overflow）、执行进展水位、终态分类。读取不可用、序号断档、投影migration-required、交互流不完整均保持Unknown及已确认保护。请求消失只是候选结清，必须联合对应轮次的后续执行，不能伪造resume。公开插件事件消费者不可接管问题回答者、写日志、改权限或阻塞原执行；实现时要证明这一点。

## 看板接入点与影响范围

10月23日已重新按index_status→get_architecture→search_graph→trace_path→get_code_snippet定位；Instant-taskboard索引ready、根正确（9273节点/26914边）。本轮3条抽查源码metadata_changed、C02为missing，关系图包含无关同名误边，已用定点rg/源码补证；不以ready宣称全新鲜，不因同一诊断反复重建。

- 当前Stage.Codex/CodexBinding、StageConversationPicker、SettingsSurface和BoardController观察均为Codex专用。增加DSH绑定/观察来源；一阶段允许哪种来源及切换表现是后续重要界面选择，尚未确认，不用分类选项猜绑定。
- CodexState.Apply目前仅汇总运行阶段的Codex绑定；不能让DSH直接套用此聚合而忽略另一平台等待。建议抽出最小公共阶段状态汇总，Codex专用身份/协议仍独立，完整回归C01。
- 消费者：Models/Copies、BoardEngine保存合并/归档恢复、阶段选择器、状态文字/设置、Controller事件分派、自动移列和AtomicStore。现有NotifyCodexChange经Engine.Notify触发刷新/保存，动画并行；DSH需接同一保存机制，不以动画结束作为保存条件。
- 拟改变：增加状态来源、绑定及跨来源聚合。保留六列窗口、现有入口和操作范围、背景/命中/动画规则。当前不改任何产品源码。具体共享影响见[SHARED-CODE](../architecture/SHARED-CODE.md)。

## 后续实施与真实验收

1. 用户选择A/B/C，核对正式0.9.19与当前源码/开发包的实施基线；不自动替换正式包。阶段关联建议沿用原会话入口增加来源选择，一阶段一个准确来源、不同阶段可混用；仍为建议，界面选择在实施前确认。先在项目内准备可审阅原型/独立构建，未协调前不加载用户DSH插件、不启用浏览器入口、不重启。
2. 实现来源核验和状态协议原型，只读白名单，拒绝所有发送/回答/批准/改权限方法；先证明准确会话、同轮恢复和完整待处理集合，再扩大看板接入。
3. 隔离`--data-dir`验证合成/规则：多平台多阶段等待优先、Unknown/重连保护、乱序/旧轮次/旧绑定、解绑重绑/归档、同数量换请求ID、编辑合并、红点、真实文件回读。模拟结果单列。
4. 协调专用原桌面DSH聊天和操作时段，逐项验证开始、原选项/自由输入同卡作答、Plan、批准、真实恢复、正常结束、停止、命令错误后继续；覆盖continued问题、多等待、另一阶段运行和断线恢复。优先由用户操作原卡片，看板只读观察。鼠标/前台操作须当轮协调。
5. 失败/额度/服务限制仅在可控且获准环境或自然出现时实测；未触发继续保留未验收。逐场景留准确事件、实际卡片、隔离存储和红点证据，CLI/夹具不能代替原桌面。

**本轮交付边界**：10月23日只读连接/54个空闲状态、来源身份、无凭据HTTP及桌面CLI版本/帮助复核；安装产物静态分析、方案与文档。没有原桌面真实执行/问题/批准/恢复样本，无产品构建、无产品运行回归、无实际移列/保存验收、无GitHub上传。只运行文档完整性检查，结果在`output/dsh-research-20261023/docs-verification.json`。

<!-- Public copy: device snapshots and private local evidence are retained locally; unavailable archive links are plain text. -->
