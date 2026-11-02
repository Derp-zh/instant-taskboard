# 0.9.28 被动监测：活动轮次订阅与本机文件投影

2026-10-24，本地未发布。本版只改Codex监测路线与相应验证入口；窗口、动画、拖动绑定、保存及其他操作保持。

## 结论先行

- 已消除：正常目录与初始化目录的app-server启动及其插件预热／同步外联（0.9.27）；空闲聊天的follower登记，即看板存在不再阻止空闲聊天的原本闲置释放（本版）。
- 仍存在：轮次结束后最多约30秒的跟随尾巴，以及本机尚无记录的刚建聊天每次连接最多90秒的探测跟随；两者只把该聊天的闲置计时起点推后同样时长，3小时阈值、15秒节流、每批10个的释放条件本身不变。
- 没有做到“完全没有订阅”：Codex当前本地协议没有任何免订阅的读取请求方法，待回答／待批准集合只存在于订阅快照中。本版把订阅压缩到轮次运行期间（该期间聊天本就处于活动状态），这是目前唯一能同时保住准确自动移列的做法。
- 没有把未验证的内存直读方案放进日常包；不订阅的内存读取仍是研究项。

## 核实的机制（源码事实，非推测）

安装包OpenAI.Codex_26.1016.8622.0（`C:\Program Files\WindowsApps\OpenAI.Codex_26.1016.8622.0_x64__2p2nqsd0c76g`）：

- `thread-stream-following-changed` → 登记／移除follower → `emitStreamFollowersChanged` → `updateConversationInactivityTracking`；`hasOwnedStreamFollowers` 为真时该聊天不进入闲置计时，退订那一刻重新计时。
- 闲置常量 `ox=108e5`（3小时）、`sx=15e3`（15秒检查节流）、`cx=10`（每批最多10个聊天）；`shouldKeepConversationLoaded` 另有活动轮次、未落盘聊天、特定条目等保留条件，不是“超过10个就全部释放”。
- `sendConversationSnapshot` 要求 `threadStore.getConversation(e)` 已存在，订阅一个未被桌面加载的聊天不会加载它（也就不产生聊天加载副作用），但也拿不到快照。
- `codex-ipc` 协议方法表（`src-CsH31EEu.js` offset 8031）只有 `thread-stream-state-changed`／`thread-stream-following-*`／`thread-follower-*` 等广播与命令；`thread-follower-*` 全部是命令（start-turn、interrupt、审批决策、提交回答），**没有任何读取会话状态的方法**；`thread-owner-discovery` 只回 `handledByClientId`／`supportsUntrustedAppInput`，不含状态。

## 本版路线

空闲期只读本机文件：`state_5.sqlite` 的 `threads.rollout_path`、`thread_history_1.sqlite` 的 `thread_turns`（最新轮次编号／状态／`rollout_ordinal`）与 `thread_history_projection_state`（投影已消费字节数），外加rollout文件长度与写时间的stat。不读聊天正文、不解析自然语言。

- 跟随条件：最新轮次为**已追尾**的 `inProgress` 且未被live判定过，或该聊天rollout文件在上次轮询后真的增长（`ActivityWindowMs=30s` 窗口）。首次读取只建立基线，不因“第一次看见”而跟随。
- 收束：收到真实 `idle` 快照立即退订并清窗口；`FollowMaxMs=90s` 上限到达后重新判定；本机无该聊天记录时每次连接仅探测一次。
- 新终态：绑定之后才出现的本机终态轮次由 `probe/turnProjected` 一次性入待操作；绑定前已存在的旧终态永不移列新卡片。
- 失败闭合：投影落后 → `local-file-projection-behind`（不可用、不订阅补齐）；缺history库／表／列、缺rollout、未知状态 → 状态待确认；流连上但暂时描述不了该聊天 → 用本机记录保持已确认状态且不放弃有界跟随，只有真实idle才关闭。

## 本轮机器验证

- 文件投影专项26项（`test-output/fp-3/file-projection.json`、驱动回执 `file-projection-verify.json`）：活动轮次才跟随、空闲与落后投影从不登记follower、文件终态准确入待操作、live idle后退订且ReadMode回到 `local-file-projection`、旧终态不移列新绑定、投影落后失败闭合、流快照暂不可用时由本机记录保持进行中、全程 `chatQueries=0`／`turnQueries=0`、夹具哈希不变、代理0请求、进程树仅1个。
- 真实本机只读探针（`test-output/oc0928-probe/activity-probe.json`）：对当前 `~/.codex` 的8个最近聊天只读执行文件投影读取，7个产出准确轮次状态（failed/completed，含最新WAL），1个未产出（guardian子代理线程无 `thread_turns` 行，正确闭合为状态待确认，不是猜测）；其中 `11111111-1111-4111-8111-000000000001` 投影落后触发 `caughtUp=false`。探针不读聊天正文、不联网、不启动或重启Codex。
- 本机投影25项（`test-output/oc-0928u/local-unit.json`）、观察器协议73项（`test-output/oc-0928a/report.json`）、重连15项（`test-output/oc-0928b/reconnect-report.json`）、核心334项（`test-output/core-0928/report.json`）、目录只读24项（`test-output/lc-0928b/local-catalog-report.json`，主库／WAL哈希不变）。
- 运行包 `release/即时任务看板-0.9.28`：初始化44项（`test-output/oc0928-package-init/ai-initialization-report.json`）、离屏缓存提示10项（`test-output/oc0928-package-reconnect/reconnect-ui-report.json`）、启动就绪 `readyMs=1031.9265`（`test-output/oc0928-package-start/startup-report.json`）、包内实际程序集（0.9.28.0）目录检查26项（`test-output/oc0928-package-catalog-c/local-catalog-report.json`）。
- 汇总见[VERIFICATION](VERIFICATION-0.9.28.json)。

计数分别属于各检查入口，不合并成真实场景数；合成夹具不等同原桌面完整生命周期。真实本机探针只验证“只读文件能读出准确轮次状态”，不验证订阅跟随、退订时序或闲置释放（这部分由专项26项与安装代码事实支撑）。

## 未证明与未支持

- 未取得系统级零流量计量：ETW需要管理员，本轮未取得；代理感知请求捕获0次不等于完整TCP/UDP为0；看板自身0字节不能排除间接流量。
- 未做原桌面完整问答／批准生命周期（未操作真实聊天、鼠标、剪贴板），未重启用着的Codex，未改安装文件／全局配置／代理／遥测。
- 轮次运行期间的订阅与无看板时等价，这一点依据安装代码的闲置判定（active轮次本就不计时）与既有实测（缓存保留期间仅服务器Ping得到Pong、无新增模型请求）；未做公网速率换算。
- 目录可选不等于聊天实时能力；未知未来schema须重新适配；复杂TOML／多行sqlite_home明确失败。

后续路线见 `docs/AI-HANDOFF-CODEX-PASSIVE-0.9.28.md`：免订阅读取只有在Codex新增只读接口、或外部内存读取先解决对象定位／GC与并发一致性／版本失效后才可能替换本路线。
<!-- Public copy: private chat identifiers replaced with fixture identifiers. Original evidence retained locally. -->
