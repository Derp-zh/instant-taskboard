# 共享代码职责、影响范围与回归选择

核对日期2026-11-02，当前公开0.9.23、本地0.9.30。职责以定点源码核对为准，图中缺口和同名误边用源码补证；本轮Codex初始化/观察范围与回归见文末，既有0.9.22阶段/设置/提示保持。

## 阅读方法

2026-10-24接续研究不改共享产品代码及消费者：CodexLocalEvents／LocalState、Observer、CodexState、控制器选择／保存均保持。只新增磁盘工件提取、原方法受控回放和PE元数据研究工具，不加入普通启动。回归选择实际包目录、准确状态投影、重连、核心保存／多阶段／绑定保护和包内离屏初始化；没有鼠标、剪贴板或原桌面问答／批准操作。定向管道路由及无follower广播revision反例、完整外部读取缺口见[接续验收](../ACCEPTANCE-CODEX-PASSIVE-20261024.md)。

先找所改入口，再选下表相关行。对每次变更记录：本次改变、必须保持的动作/范围、受影响消费者、所选检查及未验收项。只读相邻依赖，不通读全部源码或历史证据。`partial`拆文件仍共享一个类的字段，不能把文件数当成独立模块数。

## 现有职责和依赖

| 共享入口与真实源码 | 当前职责、消费者和依赖 | 修改必须保持的边界 |
| --- | --- | --- |
| [BoardEngine](../../src/Taskboard.Core/BoardEngine.cs)、[Models](../../src/Taskboard.Core/Models.cs)、[History](../../src/Taskboard.Core/BoardEngine.History.cs) | 任务/草稿、Submit/Merge、运行阶段约束、撤销/归档；被编辑器、列表、后台更新共用 | 用户字段合并与后台状态分别归属；编辑副本不覆盖最新状态；至少一个运行阶段；不在规则层操作原生窗口 |
| [BoardController](../../src/Taskboard.Desktop/BoardController.cs)及同名分文件 | 组合窗口、Engine/Store、监听服务与UI线程。SetExpanded协调球/列/草稿/编辑/辅助窗；OnChanged协调刷新及会话watch；SubmitSession负责提交与Flush | 窗口显隐不等于提交/放弃；先验证保存结果再关闭；整组恢复保留输入及层次；局部显示变更不随意扩大状态通知 |
| [SurfaceWindow](../../src/Taskboard.Desktop/SurfaceWindow.cs)、[LiveScroll](../../src/Taskboard.Desktop/SurfaceWindow.LiveScroll.cs)、[LiveBall](../../src/Taskboard.Desktop/SurfaceWindow.LiveBall.cs)、[LiveBackdrop](../../src/Taskboard.Desktop/LiveBackdrop.cs)、[SurfaceContentRoot](../../src/Taskboard.Desktop/SurfaceContentRoot.cs) | 共用原生窗口、父子内容宿主、背景/轮廓/裁切与开合；列表、球、悬浮窗及辅助窗口共用或覆写 | 背景视觉、系统命中、WPF内部命中分别验证；固定真实画布不扩大外部范围；禁止普通开合恢复截图交接；子宿主释放与旧回调保护 |
| [ColumnSurface](../../src/Taskboard.Desktop/ColumnSurface.cs)、[Inline](../../src/Taskboard.Desktop/ColumnSurface.Inline.cs)、[CardDragSession](../../src/Taskboard.Desktop/CardDragSession.cs)、[ColumnLayout](../../src/Taskboard.Desktop/BoardController.ColumnLayout.cs) | 列表、卡片复用、alpha1透明交互面、灵感快捷录入、拖动排序/两种缩放与恢复 | 列表空白也能右键/拖动；整组22×22矩形全区可操作；新建整轮廓；卡片手势与空白外点关闭分开 |
| [CardHeading](../../src/Taskboard.Desktop/CardHeading.cs)、[MarqueeText](../../src/Taskboard.Desktop/MarqueeText.cs)、[TaskCardLayout](../../src/Taskboard.Desktop/TaskCardLayout.cs)、[CardSeparator](../../src/Taskboard.Desktop/CardSeparator.cs) | 名称行Measure/Arrange、先状态后会话再阶段余宽、溢出滚动/像素线；列卡片及相关预览使用 | 会话3至5中文字宽；同卡缩放重新预算、极窄装饰裁切；不因视觉改动改变根背景或卡片点击/拖动 |
| [EditorSurface](../../src/Taskboard.Desktop/EditorSurface.cs)、[DraftStackSurface](../../src/Taskboard.Desktop/DraftStackSurface.cs)、[DraftSwitchMotion](../../src/Taskboard.Desktop/DraftSwitchMotion.cs) | 编辑副本、SaveAndClose/取消/暂存、单宿主堆叠与抽牌、ClearClosed延迟清理 | 操作表[M04](../current/M04-新建编辑与草稿.md)；真实关闭结束后才清理、快速新建取消旧清理；暂存全字段与正式保存分开 |
| [OutsideClicks](../../src/Taskboard.Desktop/BoardController.OutsideClicks.cs)、[GameInput](../../src/Taskboard.Desktop/BoardController.GameInput.cs)、[GlobalHotkey](../../src/Taskboard.Desktop/GlobalHotkey.cs)、[StageConversationPicker](../../src/Taskboard.Desktop/StageConversationPicker.cs) | 捕获物理按下/前台归属、钩子及原始输入去重、系统阈值双击、菜单/阶段区边界；分派到保存、暂存或整组隐藏 | 只实际外部点击收起，不因失焦/Alt+Tab；列表卡片不当外点；不吞其他应用输入；阶段内外收起层级不同 |
| [CardPresentation](../../src/Taskboard.Desktop/BoardController.CardPresentation.cs)、[AutomaticMoves](../../src/Taskboard.Desktop/BoardController.AutomaticMoves.cs)、[TaskCardMotion](../../src/Taskboard.Desktop/TaskCardMotion.cs)、[WorkspaceRevealBatch](../../src/Taskboard.Desktop/WorkspaceRevealBatch.cs) | 暂留阶段及人为操作整理、自动移列的退出/进入动画、共同开合准备屏障 | 人为整理与后台自动更新不同；自动状态/保存不等180ms动画；取消/反向/隐藏恢复不留重复/零高卡片；准备超时不锁死组 |
| [CodexState](../../src/Taskboard.Core/CodexState.cs)、[Observer](../../src/Taskboard.Desktop/CodexObserver.cs)、[FileActivity](../../src/Taskboard.Desktop/CodexFileActivity.cs)、[TurnTracker](../../src/Taskboard.Desktop/CodexTurnTracker.cs)、[DesktopBridge](../../src/Taskboard.Desktop/CodexDesktopBridge.cs)、[PendingBridge](../../src/Taskboard.Desktop/CodexPendingBridge.cs) | 原身份/传输/准确轮次、结构化请求、聚合、代次/epoch和重连保护；Observer内的ICodexActivitySource给出空闲期文件投影（轮次/投影进度/rollout字节与写时间）；Controller分派事件、通知卡片与保存 | 来源验证不在控件里猜；已确认等待不被Unknown/另一运行盖过；真实恢复整卡才清红点；不扩大只读操作 |
| [安装发现](../../src/Taskboard.Desktop/CodexInstallationDiscovery.cs)、[初始化流程](../../src/Taskboard.Desktop/CodexInitialization.cs)、[控制器入口](../../src/Taskboard.Desktop/BoardController.AiInitialization.cs)、[设置控件](../../src/Taskboard.Desktop/SettingsAiInitialization.cs) | 首次一次提示、实际连接优先、动态登记与签名身份、升级路径修复备份、观察模块合同、只读关联聊天测试；消费者为启动、设置、Observer及阶段目录 | 原服务不清空、不自动选聊天/发消息、不重启Codex；测试不发任务事件、旧绑定结果丢弃；分项能力和原桌面生命周期分开；六列、共享背景/命中/动画不改 |
| [ReminderClock](../../src/Taskboard.Core/ReminderClock.cs)、[Archives](../../src/Taskboard.Desktop/BoardController.Archives.cs)、[AtomicStore](../../src/Taskboard.Core/AtomicStore.cs)、[ConfigurationBackup](../../src/Taskboard.Core/ConfigurationBackup.cs)、[DataPaths](../../src/Taskboard.Desktop/DataPaths.cs) | 日历到期、完成天数/归档上限、串行写入/备份、配置隔离/路径互斥；由Controller协调定时和Flush | 到期原因和周期配置分开；未提交编辑保护；配置不含任务；数据写入不依赖动画完成；路径切换不自动合并/覆盖 |
| [NotificationListeningService](../../src/Taskboard.Desktop/NotificationListeningService.cs)、[NotificationSoundLease](../../src/Taskboard.Desktop/NotificationSoundLease.cs)、[StartupLinks](../../src/Taskboard.Desktop/StartupLinks.cs) | 外部通知/原音租约和系统启动入口，涉及真实电脑环境 | 与纯UI、隔离数据测试分开；不误改真实声音、自启或用户Codex；真实系统效果需专项证据 |

## 已核实的关键链路

0. **阶段删除与设置排版**：EditorSurface圆形X→BoardEngine.RemoveStage，仅改副本，保留最后阶段和至少一个运行，当前指针转向剩余运行阶段，经原SubmitSession链验证/合并/落盘。BoardColumns保护全部内置角色，Preferences读取旧删三列恢复隐藏，Validation保留有效剩余指针、不再要求阶段0永远存在。SettingsLayout只移动原控件分组/换行，SettingsSurface保存按钮复用Flush，不删原选项。BallSurface.EdgeHide的ApplyEdgeBadgeFrame同帧更新红点/数字，预布局避免结束帧重排，不改100ms位移、几何或输入。

1. **正式保存**：EditorSurface.SaveAndClose → BoardController.SubmitSession/SubmitSessionLocked → 复制会话和状态、临时BoardEngine.Submit验证候选 → Store.SaveAsync先落盘 → 正式Engine.Submit合并/通知 → 成功才关闭。普通脏数据另经OnDirty/Flush延迟保存。不能把正式保存误写成“先改正式内存、等待动画后再Flush”；失败需保留窗口及输入。
2. **外点操作**：OutsideClickObserver/原始输入 → 物理时间及输入归属/去重 → ObserveWorkspaceOutsidePress或ObserveOutsidePress → 整组隐藏、草稿暂存、编辑SaveAndClose、设置隐藏。不是统一“窗外关闭”，也不是焦点变化自动隐藏。
3. **Codex后台变化**：桥/观察器/Tracker验证来源及轮次 → Controller.AttachCodex → CodexState.Apply → NotifyCodexChange → Engine.Notify/OnChanged/保存通知 → 自动移列动画。NotifyCodexChange先记录状态与顺序，再启动装饰动画。
4. **时间提醒**：Controller.TickReminders → ReminderClock.Tick → Engine.Notify → 保存及显示 → 动画。Codex恢复清红点由真实整卡状态决定，不能由通知/动画完成决定。
5. **草稿生命周期**：关闭整个宿主 → 暂存编辑并布局 → DraftStackSurface.ClearClosed延迟清理；Layout/快速重开取消旧清理。抽牌的合成快照保留，不把它和普通开合真实裁切混用。

红点当前显式源码入口：BoardEngine.Edit打开编辑、Submit正式保存、SetStageRunning列表运行标记、Move移列会清点；CodexState.Apply仅可信实际恢复且整卡回进行中清点。此处是实现定位，不把所有鼠标点击一概解释为清点，也不因整理而改这些入口。

以上链路由源码直接核对，非运行验收。外部信号生命周期、鼠标路由、实时背景仍需实际检查。

## 按变更选择回归

| 修改范围 | 必须覆盖的控件/操作 | 可定位的已有检查入口 | 仍需人工或专门环境 |
| --- | --- | --- | --- |
| 背景/透明度/窗口模板 | 列表空白右键、标题拖动、22×22整组/单列缩放、卡片、新建、滚动条；新建与编辑首开/重开/设置响应/多稿/抽牌 | Ux0914Checks、Ux0915RenderChecks、Ux0917Checks、VisualChecks | 同背景黑/白/细纹理透底和模糊；展开后1至6秒及异常帧；真实系统+内部命中+操作 |
| 球形状/边缘隐藏 | 自由/吸附矩形左右键、拖动、透明角、菜单8DIP位置、1/4条带/数字红点、快速反向 | Ux0913Checks、Ux0913RenderChecks、Ux0914DesktopChecks、球LiveBall | 当前旧离屏动画失败不可跳过；混合DPI/相邻屏幕裁切/真实鼠标 |
| 普通开合/关闭/清理 | 新建/编辑/设置首开、第一次外点、第二次保存/暂存、关闭开始/结束、最后草稿、快速重开、关联区/保存中 | Ux0914ClosingChecks、Ux0915Checks、Ux0917Checks、SharedMotionChecks、WorkspaceChecks | 冷帧/忙碌帧像素与输入；不能以单个模拟Click通过代替 |
| 名称行布局/滚动 | 3/4/5/6中文、英文混合，短长阶段，同卡窄宽变化，双文本滚动，单阶段/灵感/未绑定例外 | Ux0920Checks、Ux0910Checks、Ux0914Checks | 最新布局离屏结果不代替鼠标/多屏；卡片操作范围也检查 |
| 编辑/草稿/保存 | M04动作表全部分支、全字段暂存、未选状态、空草稿、重复保存、写入失败、后台并发、重启独立回读 | Taskboard.Checks、StageFeatureChecks、DraftDealChecks、FloatingInteractionChecks | 新建/已建分别检查，真实双击需协调 |
| 阶段/拖动/归档 | 至少一运行、多运行暂留/全局整理、后台不整理、拖动取消、20步撤销恢复、完成天数及40条确认、未提交编辑保护 | StageRules、ArchivePolicyRules、Ux098Checks、CardPresentation、CardDragSession | 真实手势和列表滚动，不滚用户列表凑动画 |
| Codex/提醒/合并 | 多阶段等待优先、跨重连、旧终态隔离、改绑/归档恢复、ID更换、整卡恢复清点、编辑合并、实际卡片/保存 | CodexRecoveryRules/CodexStopRules/CodexQuickStopRules、CodexTurnTrackerChecks、CodexObserverChecks、CodexDesktopLifecycleChecks | 获准原桌面完整生命周期，真实failed/额度仍缺；不耗尽额度、不重启当前Codex |
| AI初始化/设置/连接保存 | 首次/稍后/重开、重检、原路径/服务、目录可读但观察不可用、未知版本、端口冲突、选择器及配置落盘 | `--ai-init-verify`离屏夹具；`--ai-init-environment-verify`实际登记和CLI版本检查；[本地验收](../ACCEPTANCE-0.9.21-ai-init.md) | 不将离屏事件当物理鼠标；不将准备好当具体聊天生命周期；用户确认后实际打开及原桌面问答/批准仍待协调 |
| 数据/配置/启动/外部声音 | 路径互斥、损坏拒绝、配置无任务/引用、恢复、防旧版降级、原音租约恢复 | Taskboard.Checks、IntegrationChecks、NotificationListeningChecks | 正式自启登录、真实通知替换需专项，不碰正式数据 |

表中“入口”是选择和定位用，不承诺任一入口完整覆盖该行。阅读Program.cs确认参数及副作用，始终明确隔离`--data-dir`；会占用桌面输入、切换前台或临时使用剪贴板的工具，必须按根AGENTS.md先询问用户是否方便，收到明确同意后才开始，不能只提示就执行。合成规则、离屏控件、真实鼠标/像素、原桌面生命周期分别记录，历史检查数量不能计作本次通过。

## 后续维护边界

0.9.24拖动绑定消费者：TaskCard原会话文字位置在普通构建包裹ConversationDragButton，不依赖试验开关；归档只读／灵感例外与名称预算保持；ConversationBindingDrag只临时隐藏其他原可见列，不改变SetExpanded/隐藏组配置，主动收起或销毁取消；BoardController.ConversationDrag和BoardEngine.ConversationBinding处理单阶段、代次、撤销及保存。新增CodexConversationMenu在松开后定位原侧栏会话/主聊天操作菜单，只调用复制深度链接；ConversationClipboard在动作前物化完整格式，动作后恢复并核对，剪贴板被其他动作改变时不覆盖。原整卡拖动、按钮、背景、系统命中、菜单位置和普通裁切保持。选择检查为名称预算/文字外空白内部命中、真实子宿主来源、窗口恢复、过期绑定、其他阶段等待/编辑合并/撤销/实际文件，以及原核心规则；本轮重点增加原桌面真实链接、实际阶段绑定和重开落盘、文字/HTML剪贴板恢复，不以旧拒绑原型数替代。详见[X01](../current/X01-拖动绑定探索.md)。

0.9.22消费者：EditorSurface、BoardEngine/Validation、BoardColumns/Preferences、SettingsSurface各页及SettingsLayout、BallSurface.EdgeHide。原模板/背景、系统命中、普通裁切、菜单/拖动范围不改。回归选择为StageDeletionRules/Taskboard.Checks、`--ux0922-verify`、AI初始化和数据配置整合；[本轮验收](../ACCEPTANCE-0.9.22-ux.md)区分离屏事件/WPF内部命中/确定性帧与未做的物理鼠标和真实像素。旧Ux0913位移动画失败边界不被新提示交接检查覆盖。

0.9.24只接管名称拖动入口，不改共享背景、裁切或原生透明命中。OutsideClickObserver同时服务整组、编辑器、草稿、设置和全屏输入；本次只在拖动结束记录物理时间界限，拒绝本次操作遗留的迟到点击，之后新外点仍保持原收起。新增回归选择：普通启动、子宿主来源、取消迟到结果、跨数据域拒绝、同编号保护、保存失败及重试；物理鼠标与原操作范围检查单列于[0.9.24验收](../ACCEPTANCE-0.9.24.md)。

本次不拆类、不改其他事件/窗口范围。以后局部变更先沿上述链路圈出共享消费者，必要时再逐步分离显示/命中、隐藏/提交、真实状态/动画；是否重构由具体问题和验证结果决定。修改代码入口、消费者或检查参数时，同步更新本表，不能把这里写成与代码脱节的理想架构。

0.9.23消费者为安装发现/初始化/设置/控制器与CodexObserver、DesktopBridge、PendingBridge。改变连接发现与能力呈现，保持只读方法、绑定代次/epoch、等待优先和保存合并。回归为初始化/路径/旧地址/只读测试夹具、两代模块协议及投影、核心状态与存储；本轮不改共享表面、鼠标范围或六列动画，原桌面生命周期分开报告。

0.9.25消费者为Observer、初始化/设置、Controller阶段聚合与保存；新增CodexLocalEvents/CodexLocalState承担已有本机订阅、身份/协议/revision及无正文投影。原正常轮询改为事件驱动，不改共享表面/输入/动画。回归选核心规则、Observer/TurnTracker协议、25项本地状态投影、初始化离屏和原桌面只读当前状态/准确落盘；未测试全问答/批准生命周期和整机字节差分。见[验收](../ACCEPTANCE-0.9.25.md)。

0.9.26：Observer/CodexCatalog/Rpc/安装发现分别承担本机状态与目录恢复；Controller.Conversations及StageConversationPicker处理操作重试/缓存/提示，ColumnSurface仅两内置角色原底部Auto行显示不拦输入文字。保持背景/裁切/标题拖动/菜单/新建/缩放/六列层次；回归启动顺序/掉线、目录reader重建、本地限定、空/失败/取消缓存、并发、离屏两列/同名自定义/恢复消失，以及等待/旧终态/编辑合并。真实启动先后及鼠标范围本轮未实测，见[验收](../ACCEPTANCE-0.9.26.md)。

0.9.27消费者：CodexLocalCatalog／Catalog、Observer目录入口及初始化Check；目录由CLI app-server改为系统SQLite只读，不影响正常监测LocalEvents/LocalState、阶段聚合、红点／编辑合并和保存。保留控制器目录缓存／并发／重试、选择器、六列、背景、裁切、动画、鼠标和菜单范围。回归选目录原生读／WAL／路径／未知schema／失败恢复、默认Observer入口、包内实际程序集、初始化／选择／保存／重开、核心等待／多阶段／旧绑定和重连。持续订阅与闲置保留问题未解决；内存读取只做独立研究。详见[验收](../ACCEPTANCE-0.9.27.md)，不把代理捕获当完整公网计量。
0.9.30消费者：CodexObserver／FollowPolicy、FileActivity、Core状态、初始化／安装发现及Controller设置入口；SettingsSurface／Layout／AiInitialization／Listening重排实际控件，原事件和选项保持。文件只候选与同轮终态，不再伪造执行；Peek只读已有快照，不新订阅；普通初始化不启动CLI，不以旧地址作条件。设置保留七个旧导航key、手动保存、任务／配置路径及快捷键独立应用，撤出未接入平台无效输入但不清旧值。

本次不改共享Ui／SurfaceWindow、背景、透明命中、层次、动画、六列菜单或拖动绑定；设置内部布局变化由用户明确授权。回归：cleanup-0930时钟／实际观察链、文件夹具、重连／多阶段等待、核心／保存、实际包目录读取、settings-0930七页与长结果渲染、初始化／整合／原设置控件。原桌面问答／批准恢复、系统级流量、真实鼠标／多屏不冒充通过。详见[0.9.30验收](../ACCEPTANCE-0.9.30.md)。0.9.28旧路线及反例只按历史验收追溯。
