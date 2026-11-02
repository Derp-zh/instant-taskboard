# 当前实施入口与文档整理记录

更新日期：2026-11-02。本地0.9.30接入取舍与设置整理，公开仍0.9.23。源码和验证按本页及本版验收定位；旧版本细节仅追溯。

## 桌面操作确认约定修订（2026-11-02）

将根AGENTS.md及相关现行页中的“先协调／开始前提示”明确为先询问用户是否方便，收到明确同意后才开始；未回复、仅告知、历史授权及命令获批均不能替代本次确认。同一已同意的连续范围不逐动作重复问，扩大或另行开始重新问。本次只修改协作说明并运行文档检查，不启动产品、不操作鼠标/键盘/前台/剪贴板，不改变产品版本、不发布。

## 0.9.30 接入取舍与设置整理

CodexObserver内CodexFollowPolicy使用独立的发现截止、已尝试轮次和一次性重连恢复资格；文件记录只发现候选，不能给出执行或空请求集合。真实active／等待继续跟随，idle收束，缺帧后进入固定宽限；先缺帧再断线仍有一次恢复资格，恢复失败不能循环续期。CodexFileActivity在同一只读事务读取轮次／偏移，偏移必须等于文件长度，拒绝网络文件；Snapshot仅允许补曾实时确认过的同轮终态。Core拒绝旧probe/turnProjected入口。

CodexInstallationDiscovery.Find的inspectCli=false用于普通初始化；旧CLI无效不阻断当前连接。CodexSetupEnvironment.Check只检查直接本机目录与管道身份，握手不能冒充逐聊天提问批准。Controller.TestAiConversations→Observer.PeekSnapshot只读既有结构快照，不连接／订阅／刷新或发任务事件，重新检查调用EnsureConnection而非重建已有观察器。

封包审计复现“同轮等待→断线→新epoch未知→文件完成”被Core拒绝：新增仅运行时的ObservedTurn，核对绑定ID／代次／聊天／轮次，在当前TurnId因Unknown清空时允许同轮终态补齐。该证据不序列化；BoardEngine.MergeStage仅同绑定保存合并时从live保留，改绑／恢复不继承。Controller.AttachCodex在通道和身份有效时保留当前epoch已确认终态，不以全局无实时能力覆盖；真实断线仍Unknown并保留等待。专项覆盖真实观察循环、编辑合并边界和实际Dispatcher回调，不接原Codex。

SettingsSurface保留七个旧导航key，新名称按用户用途显示；SettingsLayout移原控件保事件／选项，统一页头与卡片，把启动修复和存储位置折叠。SettingsAiInitialization突出错误和下一步，分开目录、通道和逐聊天能力；长测试结果、路径、兼容工具与诊断按需展开，旧端口／服务输入撤出但原值保留。SettingsListening折叠高级捕获和应用匹配；未接入平台只说明实际状态。Ui、SurfaceWindow、透明命中及动画不改。

新检查：tools/CodexObserverChecks --cleanup-0930；产品--settings-0930-verify（CodexSetup0930Checks与Settings0930Checks）；其余核心／目录／重连／初始化／整合回归按[本版验收](ACCEPTANCE-0.9.30.md)。Build-Portable从本版起不再附旧观察说明；Package-0930.py只处理独立包并逐项回读ZIP，不覆盖旧包／旧ZIP。源码根Git不是现代基线，不git add -A。

## 0.9.28 旧方案追溯

原设计与当时结果见[该版验收](ACCEPTANCE-0.9.28.md)，后续反例见[独立评审](REVIEW-CODEX-0.9.28-20261024.md)。其中文件伪执行、窗口自动续期与未知轮次补报不再作为现行实现；0.9.30的取舍及缺口以上节为准，不复用旧通过数量。

## 0.9.27 目录离线与被动监测研究

2026-10-24接续仅增加研究工具与文档，产品源码／版本／运行包不改。`tools/Inspect-CodexPassiveRoutes.py`只读选定三成员并核验ASAR完整性，`Replay-CodexPassiveRoutes.mjs`提取原方法、合成socket和Map调度13项回放，`Inspect-CodexV8Metadata.py`只读磁盘PE版本／导出。证据在`output/codex-passive-20261024-review/`：路由器按targetClientIds转发，owner注册器不含四个内部getter；无follower时广播revision不变，无法充当外部一致性锁。REA三成员语义图partial，未穷尽整包接口。V8 15.5.35.23锚点不等于外部decoder；真实根定位／完整请求／恢复／GC一致性尚未实现。实际包目录26、状态25、重连15、核心334、包内初始化离屏44均为本轮新运行。入口与边界见[接续验收](ACCEPTANCE-CODEX-PASSIVE-20261024.md)，原版验收不改写。

CodexLocalCatalog通过系统winsqlite3只读打开本机state_5，单次查询准确UUID／名称／更新时间；兼容已知可选name，读取最新WAL后立即释放，不用immutable或主库复制。支持CODEX_HOME及顶层字面量sqlite_home，拒绝网络路径、未知新版库和未知结构，不执行配置、不读rollout或auth。Observer默认Catalog与CodexSetupEnvironment.Check均改走此入口，正常路径不启动目录app-server；RPC注入只留给隔离协议夹具。原有效手动路径／旧服务地址保留，目录不再依赖CLI，配置诊断明确订阅影响闲置释放尚未解决。

监测LocalEvents/LocalState及状态聚合／保存不改。针对已安装3个JS成员校验哈希，原闲置方法合成回放证实订退重置；REA现有9339入口不可达。只读进程探针只读64字节PE头验证权限，不读取V8聊天堆，不等同可用状态源。已发现内部getConversation/getThreadExecutionState getter，但所查外部路由没有建立可用无订阅调用；准确对象定位、请求集合及更新一致性未完成。不能用文件猜测或周期订退代替。

隔离目录205条／WAL及原状态回归、离屏初始化／选择／保存与新包入口证据见[验收](ACCEPTANCE-0.9.27.md)。代理请求捕获不等于完整TCP/UDP；ETW缺少系统管理员权限，记录失败未报零流量。没有操作原聊天、鼠标、剪贴板、正式数据或用户进程；仅结束一次参数错误的自建隔离测试实例。

## 0.9.26 自动连接与目录恢复

实际核实原Start已有本机自动连接，新增EnsureConnection合并/限速唤醒、异常后继续循环，保留同SID/Session/包身份。CodexCatalog独立管理目录reader健康度；CodexRpc读循环结束后IsConnected为false，避免重用死连接。新连接通过当前系统登记及有效手动路径重新发现CLI，失败最多追加一次重试；仍只thread/list/useStateDbOnly。Controller空/失败/取消不缓存，并发共用一次加载；StageConversationPicker区分加载和失败，不修改原选择/解绑/保存。

ColumnSurface使用原空的底部Auto行放10号低透明文字，仅内置Running/Action角色显示，不拦鼠标、不改新建滚动位置或其他窗口层次。控制器在状态变更更新文字，展开/设置/列表操作时检测未连即唤醒；不恢复聊天查询。消费者和回归见共享影响表；本轮只隔离模拟/离屏，不操作原Codex/鼠标/正式数据。新包0.9.26独立目录，证据见[验收](ACCEPTANCE-0.9.26.md)。

## 0.9.25 Codex本机状态与流量修复

消费者：CodexObserver、初始化/设置、准确阶段状态、红点/多阶段汇总与保存合并；不改背景、命中、六列、动画或鼠标范围。旧逐绑定read_thread循环会触发非纯本地工作，正常启动改为CodexLocalEvents固定本机命名管道，复用DesktopBridge同SID/Session/当前登记包/签名验证。CodexLocalState按协议11已有快照和连续revision更新结构投影，问题答复仅提取固定信封ID，全文不留存。重复绑定只订阅一次；解绑/退出取消，缺帧只重同步已有内存，不hydrate/接管/激活/创建聊天。

产品默认RunLocalEvents；旧RPC协议夹具路径只在discoverDesktop:false显式隔离入口运行，界面不新增查询回退开关。无CDP/CLI运行依赖；目录仍按用户操作查询本地元数据。初始化不创建观察端口、不以旧端口无效阻塞新连接、保留旧配置。回答或批准后需新的准确轮次实际执行增量才恢复；等待期间的旧执行证据不能复用，来源切换/缺帧废弃执行证据而保留等待。

本轮实际证据/测试和未验收见[0.9.25验收](ACCEPTANCE-0.9.25.md)。REA用于定向安装包/运行证据调查：ASAR全量分析完整性失败，双模块分析内存限制partial，实际结论由精确哈希文件片段及本机探针补证，不声称整个安装包分析成功。文件SQLite历史投影明显落后且缺完整等待，未用其冒充实时。构建新独立0.9.25目录，不替换0.9.24/公开包；本轮未发布。

## 0.9.24 拖动绑定主开发集成

用户最新修订要求预设新版本0.9.24；项目版本与构建说明已更新，旧0.9.23发布文件不改。核对14项交接源码哈希后，定向整合当前根工作区，不覆盖目录、不提交根旧master。Codebase Memory实际ready、根正确后经架构／符号／关系／片段定位，部分解析及图误边用源码补证。

TaskCard普通名称区域提供ConversationDragButton，卡片手势始终排除名称按钮；归档／灵感例外及名称预算保持。Program与BoardController.ConversationDrag将功能启用与ConversationPreviewMode拆开：普通启动原数据、配置、自启和集成保持，不注入示例／强制展开／测试托盘／常态回执；--drag-binding-verify也不启用预览。

来源采用子HwndSource的PresentationSource，拖出才临时隐藏其他可见列。松开重读原桌面目标，菜单复制准确深度链接、完整物化剪贴板并按序号恢复。取消后的迟到结果不提交，捕获数据域并拒绝跨切换结果。实机发现右键取消的外点异步排队后可能收起已恢复列，ConversationBindingDrag结束记录物理时间界限，OutsideClickObserver在UI交付时过滤此前旧按下；不吞系统输入，新外点收起保持，并补两项有区分力断言及连续实机复验。BoardEngine仅改单阶段，同UUID大小写／名称刷新保留有效等待保护，新编号重置旧请求／epoch；保留撤销、其他阶段和未提交编辑。原humanTaskGate／Flush链准确反馈保存失败，测试回执IO失败不掩盖业务保存。

新工具Build-DragBindingMain生成独立普通0.9.24包；Verify-DragBindingMain由Core准备隔离样本，仅传--data-dir启动，无预览参数，并用独立原菜单编号核对实际board.json及重开。外部遮挡时安全停止，取得本轮前台协调后再测，不修改用户程序。实际结果见[0.9.24验收](ACCEPTANCE-0.9.24.md)。r2历史路线及40／34项证据仍见[测试说明](DRAG-BINDING-PREVIEW-GUIDE.md)，不算本轮通过。

## 阶段删除、列表保护、设置与提示交接

实际核对索引根/ready后，使用架构、RenderStages调用关系及边缘球源码片段，定点读取直接源码。四个引用路径覆盖仍metadata_changed，源码补证，不假称完全新鲜。

- BoardEngine.RemoveStage仅操作编辑副本，拒绝最后一阶段及唯一运行阶段；删除定位阶段转向剩余运行阶段。EditorSurface阶段名后增加24×24圆形X，复用Ui.Hint，重绘前保留剩余搜索输入/展开状态并释放旧picker。Validation取消“阶段0必须存在”旧隐含约束，仍校验有效指针、至少一阶段/运行及编号唯一有序。原Merge与撤销实现复用，验证剩余阶段后台等待不丢。
- BoardColumn.Protected覆盖全部BuiltinLane；Preferences读取旧版曾可删的三列时恢复隐藏/缺失定义，不改变原三类受保护列的损坏拒绝。自定义删除空列表规则保持。
- SettingsLayout移动现有控件做用途分组和操作换行，保留事件/所有可选项；原导航、窗口层次和毛玻璃模板不改。SettingsSurface增加保存与结果反馈、数量标签、名称Enter提交；开关/滑块继续原即时生效。
- BallSurface.EdgeHide不再让数字Collapsed；隐藏用opacity=0，保留实际布局。ApplyEdgeBadgeFrame在同一回调更新红点和数字，最后一帧同刻交接；处理反向起点红点已零的情况。100ms边缘位移、球几何、输入及菜单不改。

本轮检查、失败修正与未验收统一见[0.9.22验收](ACCEPTANCE-0.9.22-ux.md)，不混入旧产品数量。新增`--ux0922-verify`必须明确隔离`--data-dir`，仅屏外无激活呈现源及离屏控件/确定性帧，不移动鼠标。

## 发布后的鼠标补验收

用户本轮明确同意鼠标使用并要求开始前提示。仅新增外部测试工具`Verify-Ux0922Mouse.ps1`及验收记录，不修改产品代码/发布二进制，不再次发布。真实输入逐次核对隔离PID，UIA仅定位；补拍即时提示避免1.2秒气泡消失期间读取旧节点。测试脚本处理UIA重复树、缓存单对象数组计数及Windows PowerShell无BOM种子问题，失败记录保留并与产品结果分开。

完整一轮19项（新建/编辑X、保护、保存重开/取消、球真实隐藏/悬停/再隐藏）通过；39帧主屏150%像素与字形分析未采到交接空白/重叠；两种提示文字已看图确认。最后恢复鼠标/前台实际回读通过。见[补验收](ACCEPTANCE-0.9.22-mouse.md)和[机器摘要](VERIFICATION-0.9.22-mouse.json)。不重跑旧核心/离屏数量，不动正式数据、自启或Codex；其他区域/多屏/快速反向仍未覆盖。

## AI平台初始化实现

先实际核对Codebase Memory状态/根/覆盖、架构、设置/桥符号、AttachCodex关系和源码片段，再直接源码补证；关系存在同名误边，metadata_changed不等于完全新鲜。

- `CodexInstallationDiscovery`读取PackageManager、manifest主桌面Application及系统App Paths；排除AppListEntry=none的命令runner。CLI优先有效手选、选定桌面resources、PATH及原本机bin位置。PE控制台检查先排除GUI桌面/同名启动器，再执行只读`--version`；安装路径动态登记，版本取包登记而非Chromium版本。
- `CodexInitialization`先实际检查，不再用固定版本/安装exe哈希阻断连接；原服务地址保留并自动回退，商店升级失效路径按同包同相对位置刷新并备份。模块内容哈希与已核实投影仍保护内部观察。新增TestAiConversations只读准确绑定，拒绝旧配置/绑定结果，不发布任务状态事件。
- `BoardController.AiInitialization`串接首次提示、原子保存、异步配置冲突保护、设置重检及独立的确认后启动动作。仍运行则拒绝，绝不杀桌面进程；重开需用户自行退出且界面确认，待完成不假报成功。重连清理旧目录缓存，不改变绑定、聚合或保存合并规则。
- `SettingsAiInitialization`分项显示目录/实时连接/提问批准观察，新增测试已关联聊天，移除“改用原Codex桌面连接”。手动路径和原配置均留在可展开诊断。新增imn/nct观察适配，只在实际模块哈希配对通过后使用；支持旧Hfn/Dot结构。
- 无包身份直启核对同用户/Session、当前系统登记文件最终路径及有效OpenAI签名，不再以固定exe哈希拒绝新版；内部观察仍单独核对模块内容合同、监听PID与准确主页面。

本轮26.1016.8120.0的两模块静态核验、合成协议/投影和最终包实际目录/原桌面连接分别记录在[0.9.23验收](ACCEPTANCE-0.9.23.md)；无观察端点，运行时对象及原桌面生命周期仍待验证。0.9.21等历史结论不覆盖当前规则。

## DSH定向探索

2026-10-23复核既有DSH Desktop，只读连接、54个准确双ID空闲状态及3组空notice通过；不提权Win32补齐端口监听PID/路径/同用户/同Windows Session。版本仍2.0.18/runtime 0.2.1-rc.2；本机同进程HTTP无凭据HEAD返回401，尚未建立官方Gateway认证或订阅；桌面CLI版本/帮助可读，旧独立npm CLI已不存在。安装静态核查补充官方interactions=none、单会话follow/projections及桌面访问门槛，REA覆盖partial并定点源码补证。结论统一见[C02](current/C02-DSH接入探索.md)，本轮脱敏证据/复现脚本在`output/dsh-research-20261023/`，10月21日证据保留原时点。

路线待用户选择：推荐A最小状态插件原型；B先验证官方被动API（受支持认证/完整pending/恢复及正文传输范围待确认）；C暂缓。没有读取聊天正文、订阅全量内容、发送/回答/批准、前台/鼠标操作或重启，未实施产品监听/构建/实际移列保存。用户报正式本地0.9.19与文档公开0.9.23/开发0.9.24分开，实施前核对目标包；不替换正式包、不上传GitHub。本轮仅文档完整性检查，记录`output/dsh-research-20261023/docs-verification.json`，不算产品回归。

## 当前实现定位

职责、消费者、实际关键链路及回归入口统一维护在[SHARED-CODE](architecture/SHARED-CODE.md)，避免在多个摘要复刻职责。需求行为在[current](current/README.md)，实际交付在[STATUS](current/STATUS.md)。

- 0.9.20-widthfix：MarqueeText按实际字体3/5个“中”字计宽，CardHeading右侧优先后给阶段剩余空间；同卡缩放和极窄装饰裁切。最新验收为1532布局断言/216组、提示15、控件30，未操作鼠标/键盘、正式数据、自启或Codex。
- Codex沿用0.9.19可选模式，实际能力/失败记录和未验收项见STATUS；不将旧0.9.18“异步提问未支持”覆盖当前观察模式，也不把真实failed/额度未触发当已验收。

## 本次完成的两项整理

1. 整理前AGENTS/HANDOFF/INDEX/PRD/TASKS/IMPLEMENTATION按字节快照并记录SHA256，见[历史入口](history/20261021-before-current/README.md)。没有删除旧版本验收、机器证据或运行包。
2. 将原PRD历次修订合并为M01至M12、C01、N01及共享表面的现行条目。覆盖旧外点、唯一当前/四阶段、多稿入口、归档上限、球吸附默认/时间、会话宽度、失败/红点及真实控件裁切等冲突；每页写验收和来源编号，建议未自动转确定。
3. HANDOFF只保留当前状态，INDEX/PRD只路由，TASKS保留当前缺口与模块依赖；AGENTS指向按需阅读和原位维护，不再默认加载历史。
4. 定点读取源码建立共享职责、五条关键链路及按变更回归表，区分视觉与命中、隐藏与提交、状态与动画。本次没有实施第三步代码重构。
5. 新快照历史排除代码索引，新增`tools/check_current_docs.py`检查活跃链接/锚点、来源编号覆盖、快照完整性和关键失效规则。它是文档检查，不等于产品运行回归；索引和摘要实际同步情况见INDEX-STATUS。

并行README重写与上传记录已合并保留：中英文README以真实功能为主，main提交117b84b、远端文件与0.9.20 Release/ZIP及SHA256已有该项工作的核验回执；无产品改动。原文前后版本均在快照目录留存，本次未重复远端核验或发布。

## 验证记录

文档检查的实际结果在[整理验证](DOCS-VERIFICATION.json)。此前纯文档整理没有启动看板或重新核验GitHub；本轮产品检查与新增发布核验分别见上文及发布回执，不将历史数量计入本轮。Codebase Memory恢复后已full重建，17页现行文档均在图中，摘要重建后逐字读回一致；路径新鲜度诊断仍有限制，详见[索引记录](INDEX-STATUS.md)。

以往按版本实施细节只在追溯时查[原实施记录](history/20261021-before-current/IMPLEMENTATION.md)及对应版本证据，不日常通读。


## 0.9.22 GitHub发布与回读

用户在本轮明确追加“该版本开发完后直接上传到GitHub”。使用从main 117b84b新建的独立发布克隆，未推送根旧仓库、正式数据/配置、认证、聊天、测试数据或Codex安装提取物。

- 源码提交`a2735c9cb59a86978add58f21526ecfd42543727`，tag `v0.9.22`指向该提交；129个产品/测试源文件与本地实现及远端git blob逐一核对。中英文README、现行模块、公开使用/兼容/验收同步；旧公开入口文档保留只读历史快照。
- 公开源码独立自包含构建通过，保留Windows系统提示音构建回填；首次在线依赖还原失败，沿用已有依赖缓存后通过。公开克隆核心规则再跑334条通过，隔离目录`test-output/github0922-public-core`，不新增鼠标实测声明。
- [Release 0.9.22](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.22)附完整ZIP和SHA256。先验证原测试包476文件，再独立加入公开说明，共479文件；程序二进制仍为原隔离验收版本。
- ZIP为77,382,985字节，SHA256 `f871bc1a598228352054f22d023e16fdc0161298c44abecb48950b26a04a7e2b`。实际从公开Release重新下载，服务器digest、SHA256文件、全部479条文件的大小/内容哈希及无正式数据均通过，非仅查看目录。
- 发布回执[JSON](GITHUB-RELEASE-0.9.22.json)、公开包清单[JSON](BUILD-INFO-0.9.22.json)。真实鼠标/显示器像素、新版Codex兼容和原桌面生命周期缺口保持，发布不等于补验收。

## 0.9.23发布与回读

0.9.23源码和完整便携ZIP/SHA256已发布；源码提交8d3d23e，129个源文件及重新下载的479个包内文件逐项核验通过，见[发布回执](GITHUB-RELEASE-0.9.23.json)。新版Codex目录/身份/只读通道通过，完整观察及原桌面生命周期仍待验证。

最终公开包从独立克隆构建，排除并行拖放实验，原工作区文件保留；最终包初始化44、核心334、配置17、原设置保存38，观察协议73、投影22、身份30、轮次50分别报告，见验收。Codebase Memory指定项目已刷新；摘要写接口缺失，保持pending。
