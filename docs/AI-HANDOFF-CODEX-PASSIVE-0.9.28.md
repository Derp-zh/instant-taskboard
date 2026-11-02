# AI 交接：Codex 接入的被动监测（0.9.28）

面向后续 AI／开发者。目标不是概述功能，而是给出可继续推进的技术方向、已核实的事实、已实现的做法、被否的路线与原因、验证方法与下一步的验收门。所有引用均为本机实际读取或实际运行结果，行号／偏移来自安装包与本仓库当前源码。

## 1. 任务与不可越过的边界

目标（用户原话要点）：尽量被动读取电脑中已存在的 Codex 数据与运行状态，不主动下载／加载／分析聊天，不因观察而长期保留原本应释放的联网资源，同时保留准确自动移列。

- 不得为观测关闭、重启、暂停、注入正在使用的 Codex；不修改安装文件、全局配置、代理、遥测开关；不阻断 Codex 整体联网。
- 不得主动加载／激活／恢复／接管聊天，不发消息、不代答、不批准。
- 不得按内存字符串、自然语言、界面文字或猜测地址判断状态；不保存原始聊天堆、令牌或账号内容。
- 不得以“本机管道”“降频”“定时订退”冒充完成；不能用未验证的内存方案替换日常监测。
- 看板验证必须显式 `--data-dir` 隔离目录；原生实验必须独立 `CODEX_HOME`／用户目录／`APPDATA`／临时目录。

## 2. 结论摘要

已实现（0.9.28，本地包 `release/即时任务看板-0.9.28`，未发布）：

- 目录与初始化：仍是直接只读本机 SQLite（0.9.27 成果），不再为列聊天启动 Codex app-server，未知结构明确失败、不联网补齐。
- 状态监测：**空闲聊天不再登记任何 follower**；只在“该聊天本机记录显示轮次正在执行”或“该聊天 rollout 文件刚刚真的增长”时开通有界订阅，收到真实 idle 快照立即退订。
- 新增本机文件活动源（`CodexFileActivity`）与 `probe/turnProjected` 事件：绑定之后才出现的终态轮次由文件投影准确入待操作，绑定前的旧终态永不移列。
- 失败闭合：投影落后／缺表／缺列／缺 rollout／未知状态一律“状态待确认”，不订阅补齐、不猜轮次。

未实现（必须如实交接）：

- **没有做到“零订阅”**。Codex 本地协议不存在免订阅的状态读取方法（见 §3），待回答／待批准集合只出现在订阅快照里。
- 仍有两种有界跟随窗口（§9）：轮次结束后最多约 30 秒尾巴；本机尚无记录的新聊天每次连接最多 90 秒探测。它们只把该聊天的闲置计时起点推后同样时长，不改变 3 小时阈值与释放条件本身。
- 没有取得系统级零流量计量（ETW 需管理员）；不能用代理感知 0 请求证明 TCP/UDP 为 0。
- 未做原桌面完整问答／批准生命周期（未操作真实聊天）。

## 3. 事实基础 A：`codex-ipc` 协议面（决定“能不能不订阅”）

安装包 `OpenAI.Codex_26.1016.8622.0_x64__2p2nqsd0c76g` 的 `.vite/build/src-CsH31EEu.js`（1664092 字节，offset 8031）方法表原文：

```
"thread-stream-state-changed":11, "thread-stream-following-changed":1,
"thread-stream-following-status-requested":1, "ipc-connection-reset":1,
"thread-read-state-changed":3, "thread-archived":2, "thread-unarchived":1,
"thread-owner-discovery":1, "thread-follower-start-turn":2, ... (其余 thread-follower-* 均为命令)
"thread-queued-followups-changed":2
```

- 广播只有状态推送（`thread-stream-state-changed`，版本 11）与 follower 协调；**没有任何“读取会话状态”的请求方法**。
- `thread-follower-*` 全部是命令：start-turn、interrupt、steer、审批决策、提交用户输入、编辑上一条、压缩、加载完整历史等。调用它们会**驱动**聊天，属于禁止动作。
- `thread-owner-discovery` 只返回 `handledByClientId`（或 `error:'no-client-found'`）与 `supportsUntrustedAppInput`，不含轮次／请求信息。
- 未认领请求会走 `client-discovery-request`，被问的客户端回 `canHandle:false` 才回 `no-client-found`。看板现在的做法是明确拒绝所有命令（不回 canHandle:true），这是正确的被动姿态。
- 快照下发路径：`sendConversationSnapshot` 要求 `transport!=null`、`getStreamRole(id).role==='owner'`、且 `threadStore.getConversation(id)` **已存在**（否则返回 false，不会因此加载聊天）。因此订阅一个未被桌面加载的聊天既不加载它，也拿不到快照。

推论：在现有协议下，“准确轮次 + 完整待回答／待批准集合”只能是订阅的产物；要让自动移列保持准确，只能把订阅限制在“本来就处于活动状态、不计闲置”的窗口内。这就是 0.9.28 路线的事实依据。

## 4. 事实基础 B：闲置释放机制（决定“副作用有多大”）

同一文件（`src-CsH31EEu.js`）中：

- 常量：`var ox=108e5, sx=15e3, cx=10` → 闲置 TTL **3 小时**、检查节流 **15 秒**、每批最多释放 **10 个**聊天；`xo=5e3` 是 follower 重连宽限 **5 秒**。
- `updateConversationInactivityTracking(e,t)`：只有 `t` 存在、`ownsConversationHistoryStream`（owner）、`resumeState==='resumed'`、`!hasActiveConversationView`、`!hasOwnedStreamFollowers` 时才登记／更新 `inactiveOwnerConversationSinceById`；否则删除并记 `inactive_thread_inactivity_tracking_cleared`。
- `handleThreadStreamFollowingChanged`：登记 follower 后 `emitStreamFollowersChanged` → 上述追踪立即被清除；`following:false` 时同样触发，于是**退订那一刻重新计时**（部有证据：安装包原方法 + 合成时钟回放 `output/codex-network-20261024/idle-contract-replay.json` 6 项）。
- `ensureInactive` 侧：`unsubscribeInactiveConversation` 要求 `resumeState==='resumed'` 且 owner，并跳过 `hasActiveConversationView || hasOwnedStreamFollowers || shouldKeepConversationLoaded`；真正释放时清空 turns、`resumeState='needs_resume'`。
- `shouldKeepConversationLoaded`：follower 直接返回“不保留”；此外有活动轮次、未落盘聊天、特定条目等保留条件 —— 所以“超过 10 个就全部释放”是误读。
- `releaseInactivePassiveConversation` 对 `t.requests.length>0`（有挂起请求）另设保留。
- `handleIpcConnectionReset`：连接重置时给相关聊天 5 秒“待重连 follower”，随后自动清除。

关键推论（本版路线的支点）：轮次正在执行时聊天 `threadRuntimeStatus.type==='active'`，本就在 `shouldKeepConversationLoaded` 的保留集合内、**不进入闲置计时**。因此“只在运行期订阅”与“完全不订阅”对该聊天闲置释放的影响相同，差别只落在轮次结束后的收尾窗口。

## 5. 事实基础 C：本机持久化数据（本版读取面）

全部只读，不读聊天正文。

`~/.codex/state_5.sqlite`（0.9.27 起用于目录）：

- `threads(id, rollout_path, created_at, updated_at, source, model_provider, cwd, title, ..., archived, created_at_ms, updated_at_ms, name, ...)`，338 行；目录查询过滤 `archived=0 AND source IN ('appServer','cli','vscode','exec','unknown')`，名称取 `COALESCE(NULLIF(name,''),title)`。
- 0.9.28 只用 `id, rollout_path`（外加 stat 文件长度／写时间）。

`~/.codex/thread_history_1.sqlite`（135307264 字节，Rust/sqlx 写入，非 Electron 侧）：

- `thread_turns(thread_id, turn_id, rollout_ordinal, status, error_json, started_at, completed_at, duration_ms, ...)`，1560 行；`status ∈ {completed, interrupted, failed, inProgress}`。
- `thread_items(thread_id, turn_id, item_id, rollout_ordinal, created_at_ms, item_json, item_type, ...)`，25822 行；`item_type` 含 reasoning／commandExecution／agentMessage／fileChange／userMessage／mcpToolCall／webSearch／…。
- `thread_history_projection_state(thread_id, next_rollout_byte_offset, next_rollout_ordinal)`，360 行；实测对最近活跃聊天**等于 rollout 文件字节数**（追尾 tailer），是可用的“投影是否追平”依据。
- 坑一：`thread_turns.started_at/completed_at` 在本机换算后落在 2026-01，**不是真实时间**；只能信 `rollout_ordinal` 与 `thread_items.created_at_ms`。
- 坑二：`inProgress` 会永久滞留（本次实测 4 条 inProgress 无对应活轮次，属崩溃／中断残留），因此“文件说 inProgress”不能直接当作“正在运行”，必须叠加 rollout 写入活动或投影追平判据。
- 坑三：投影会停滞（`11111111-1111-4111-8111-000000000001` 停在 371948，rollout 2461516）→ 必须失败闭合，不能拿旧轮次当当前状态。
- 坑四：真实读取会混入 subagent／guardian 线程（无 `thread_turns` 行，只有 projection state）→ 无记录即不产出，不猜。

`rollout-*.jsonl`（129 个）：只 stat 长度／写时间，不解析内容。写入节奏与活跃轮次一致。

`logs_2.sqlite`（76881920 字节 + 34MB WAL）：只有 Rust 侧 target，方法级事件（`app-server event: thread/status/changed targeted_connections=N`）**不含 thread id 与 payload**；`-> request:` 审批请求行缺失；保留约 3 天。可作活动佐证，不能作状态源。

`queue_1.sqlite`／`goals_1.sqlite`／`memories_1.sqlite`：排队条目、目标、记忆，与“当前轮次／待批准集合”无直接对应，未采用。

## 6. 已实现的路线（0.9.28）

新增 `src/Taskboard.Desktop/CodexFileActivity.cs`（`ICodexActivitySource.Read(ids)` → `CodexActivity(TurnId, Status, Ordinal, ProjectionCaughtUp, RolloutBytes, RolloutWriteTicks)`）：

1. 解析 home：`CodexLocalCatalog.LocalPath` + `DatabaseHome`（与目录读取共用，含顶层字面量 `sqlite_home`），拒绝 UNC／网络盘。
2. 只读打开 `state_5.sqlite`（`sqlite3_open_v2` flags=1，busy_timeout 750），取目标 id 的 `rollout_path`；再只读打开 `thread_history_1.sqlite`，取每个线程 `rollout_ordinal` 最大的一行，以及 `thread_history_projection_state.next_rollout_byte_offset`。
3. `ProjectionCaughtUp = offset >= FileInfo.Length`；任何缺表／缺列／缺文件／未知状态都不产出该线程（上层视为不可用）。

`CodexObserver` 的判定（每秒一轮，全部为本地文件 stat／SQLite 读）：

| 条件 | 动作 |
| --- | --- |
| 首次看到该聊天（`RolloutTicks==0`） | 只建基线，**不跟随** |
| rollout 写时间前进 | 开 30 秒活动窗口（`ActivityUntilMs`） |
| 最新轮次 `inProgress` 且投影已追平、且未被 live 判过 | 开通该聊天 follower |
| 仍在活动窗口内 | 保持 follower |
| 收到真实 `idle` 快照 | 立即置 `LiveIdleSeen`，下一轮退订并清窗口 |
| 跟随超过 90 秒（`FollowMaxMs`）未收 idle | 重新判定，不无限跟随 |
| 本机无该聊天任何记录 | 每次连接纪元只探测一次（有界 90 秒） |
| 投影落后／结构未知／缺文件 | 返回不可用（`local-file-projection-behind` 等），**不订阅补齐** |
| 流已连上但暂时描述不了该聊天 | 用本机记录维持已确认状态，继续有界跟随，不降级为未知 |

状态机面：`CodexState.Apply` 新增 `probe/turnProjected {threadId, ordinal, turn:{id,status}}`，与 `turn/completed`／`probe/currentTurnStopped` 共用终态分支（completed→ReadyForReview、failed→Failed、interrupted→Interrupted），并把“绑定后新出现的终态轮次”作为唯一入口，避免旧轮次重放。绑定基线 `BaselineOrdinal` 在绑定时取当时最新 `rollout_ordinal`，小于等于基线的轮次永不移列。

## 7. 已否路线与原因（不要重复尝试）

| 路线 | 结论 | 原因／证据 |
| --- | --- | --- |
| 纯文件替代（完全不用订阅） | 否决 | `thread_history_1`／rollout／logs 都没有“待批准集合”与“批准后真正恢复”的可信信号；logs 无 thread id，rollout 无 exec_approval_request 事件 |
| 定时订退（周期取快照再退订） | 否决 | 每次 following 清除闲置起点、退订重新计时，反复订退反而持续重置（`idle-contract-replay.json`） |
| 降低轮询频率 | 否决 | 不改变 follower 语义，只改变频率 |
| 目录辅助服务（app-server thread/list，即使 `useStateDbOnly=true`） | 否决 | 冷启动仍触发 chatgpt.com／github.com／api.github.com 外联（`cache-verification.json` 的 catalog_blocked_requests） |
| V8 Inspector 调试端口 | 不可用 | REA `list_electron_targets(9339)` 返回 `endpoint_unreachable`；开启端口需要重启／改启动参数，越过边界 |
| 外部进程内存直读 | 未验证，未合入 | 只能读 64 字节 PE 头；V8 堆对象定位、GC 与并发一致性、版本失效均未解决；不能按字符串猜状态 |
| ipc 内部 getter（getConversation／getThreadExecutionState） | 不可达 | 这些是内部上下文对象上的 getter，外部管道无调用路径；协议无对应请求方法 |

## 8. 必须保持的准确状态不变量（回归红线）

1. 实际开始执行才进入进行中（`turn/started` 或真实执行变化）。
2. 提问、批准等待、整轮完成／失败／停止进入待操作；多阶段等待优先（`AwaitingUserAction` 只增不减）。
3. 回答或批准本身不算恢复；只有 `IsExecutionChange` 为真才回进行中并清红点。
4. 断线、未知、缺帧不清除已确认等待（`probe/disconnected` → Phase Unknown，不动 requests）。
5. 新绑定不继承旧绑定保护；同编号名称更新保留有效保护（binding generation／`SourceEpoch`／`LastSequence`）。
6. 旧终态隔离：绑定前的终态轮次不得移动新卡片（`BaselineOrdinal`）；`turn/completed` 必须匹配 `binding.TurnId`。
7. 红点、编辑合并、保存、解绑重绑、归档恢复保持原有行为。

## 9. 仍存在的副作用（精确条件）

| 场景 | 时长 | 对闲置释放的影响 |
| --- | --- | --- |
| 轮次运行中 | 整轮 | 无（该聊天本就是 active／keepLoaded，不计闲置） |
| 轮次结束、收到真实 idle 快照 | 立即退订 | 无（与无看板同刻开始计时） |
| 轮次结束但流未及时给 idle，且 rollout 刚写入 | ≤30 秒（`ActivityWindowMs`） | 闲置起点推后同样时长 |
| 本机尚无该聊天记录（刚建／未落库） | 每次连接 ≤90 秒（`FollowMaxMs`），收到 idle 立即结束 | 同上 |
| 看板进程崩溃／管道重置 | Codex 侧 5 秒待重连 follower 后自动清除 | 可忽略 |

进一步缩减的可能（未做，需先验证）：把 30 秒窗口改为“只在投影未追平且字节仍在增长时跟随”；把无记录探测改为“仅当 state_5 出现该聊天但 history 尚无轮次时探测一次”。

## 10. 验证方法与复现命令

全部隔离，不碰正式数据；输出均在 `test-output/` 下且目标目录必须不存在。

```
dotnet build tools/CodexObserverChecks/CodexObserverChecks.csproj
dotnet build src/Taskboard.Desktop/Taskboard.Desktop.csproj -c Release

# 文件投影专项 26 项（合成 CODEX_HOME，代理 0 请求，夹具哈希不变）
python tools/Verify-FileProjection.py test-output/fp-check

# 目录只读 24 项 / 打包程序集 26 项
python tools/Verify-LocalCatalog.py test-output/lc-check
python tools/Verify-LocalCatalog.py test-output/pkg-check 'release/即时任务看板-0.9.28/Taskboard.dll'

# 观察器协议 73、重连 15、状态投影 25、核心 334
dotnet run --project tools/CodexObserverChecks --no-build -- --data-dir test-output/oc-check
dotnet run --project tools/CodexObserverChecks --no-build -- --data-dir test-output/rc-check --reconnect
dotnet run --project tools/CodexObserverChecks --no-build -- --data-dir test-output/unit-check --local-unit
dotnet run --project tests/Taskboard.Checks --no-build -- --data-dir test-output/core-check

# 包内验证（隔离 --data-dir）
"release/即时任务看板-0.9.28/即时任务看板.exe" --data-dir test-output/pkg-start --startup-check
"release/即时任务看板-0.9.28/即时任务看板.exe" --data-dir test-output/pkg-init --ai-init-verify
"release/即时任务看板-0.9.28/即时任务看板.exe" --data-dir test-output/pkg-ui --codex-reconnect-verify

# 真实本机只读探针（只读结构，不读正文、不联网、不重启 Codex）
# TASKBOARD_ACTIVITY_PROBE_IDS=<逗号分隔聊天id> dotnet run --project tools/CodexObserverChecks --no-build -- --data-dir test-output/probe --activity-probe
```

判定要点：专项报告里 `chatQueries=0`、`turnQueries=0`、`fixtureHashesUnchanged=true`、`proxyRequests=0`、`auxiliaryProcesses=1`；真实探针 `networkUsed=false`、`chatTextRead=false`。

注意：`Build-Portable.ps1` 是无 BOM 的 UTF-8，Windows PowerShell 5.1 会按 ANSI 解析中文报语法错误；本机用“UTF-8 临时副本加 BOM”执行，不改原脚本。`Verify-LocalCatalog.py` 的打包参数要相对仓库根传 `release/...`，且输出目录须预先不存在。

## 11. 风险与失效点

- 只读打开 WAL 库依赖 `-wal/-shm` 可读（本机实测可读，能读到最新已提交轮次）；若未来权限或布局变化导致打不开，表现为“状态待确认”，不会退回订阅。
- 每秒 1 次打开两个 SQLite（`state_5` 3.3MB、`thread_history_1` 135MB）并 stat rollout；只读、无写入、无锁升级，但属于持续本地 I/O。若要降低，可改为 2–3 秒或仅在有绑定卡片时轮询。
- 版本失效：`thread_turns`／`projection_state` 列名变化 → 明确失败（已在 `Require` 中实现），需要重新适配，不能猜。
- `inProgress` 滞留：靠 rollout 写入活动与 90 秒上限收敛，不会形成永久 follower。
- 未取得系统级流量计量；代理感知 0 请求 ≠ 全量 TCP/UDP 为 0；看板自身 0 字节不能排除间接流量。

## 12. 下一步路线菜单（按可行性排序）

1. **保持现路线 + 收紧窗口**（低风险）：按 §9 末尾两条改造，用现有专项检查回归。
2. **向上游申请只读接口**（最优但依赖外部）：例如一个不登记 follower、只回当前轮次与挂起请求集合的 `thread-status/read`；一旦存在，可把订阅彻底去掉。验收门：不改变闲置计时、不需要 owner、有版本协商。
3. **受控内存读取**（高风险，本轮不进日常包）：必须先解决 ①对象定位（`getCachedConversations`／`getConversation` 的实例地址）②GC 与并发一致性（读时校验时间戳／版本字段）③版本失效（Chromium 版本变化即安全失效）④隐私（只取结构、不落盘正文）。验收门：在合成聊天上复现轮次与完整请求集合，并在真实桌面用固定脚本对同一时刻的 UI 状态做交叉比对。
4. **官方/公开 API**：若 Codex 提供事件流（webhook／stdio 协议），优先采用；需区分“本地包能力”与“需要新版 Codex 的能力”。

## 13. 交接清单

改动文件：`src/Taskboard.Core/CodexState.cs`（`probe/turnProjected`）、`src/Taskboard.Desktop/CodexObserver.cs`（跟随策略／快照来源／能力文案）、`src/Taskboard.Desktop/CodexFileActivity.cs`（新增）、`src/Taskboard.Desktop/CodexLocalCatalog.cs`（`LocalPath`／`DatabaseHome` 提为 `internal static`）、`src/Taskboard.Desktop/Taskboard.Desktop.csproj`（0.9.28）。

验证入口：`tools/CodexObserverChecks/{Program,ActivityChecks,LocalCatalogChecks}.cs`、`tools/Verify-FileProjection.py`。

包与回执：`release/即时任务看板-0.9.28`、`release/instant-taskboard-0.9.28-win-x64.zip`（SHA256 `14E34F33AE1B85B4C4D3612DA25727E86FAED94711E18D85E74AEC894288B8B4`）、`docs/BUILD-INFO-0.9.28.json`、`docs/VERIFICATION-0.9.28.json`、`docs/ACCEPTANCE-0.9.28.md`。

Git 状态：根仓库 HEAD 仍是旧 master，现代源码大量未跟踪；**不得把旧 HEAD 当基线、不得 `git add -A`、不得整目录覆盖**。未上传 GitHub、未自动发布。

<!-- Public copy: private chat identifiers replaced with fixture identifiers. Original evidence retained locally. -->
