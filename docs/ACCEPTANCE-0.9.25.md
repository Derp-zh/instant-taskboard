# 0.9.25 本机状态与流量修复验收

日期：2026-10-23。本地开发，未发布；正式数据/配置/自启/preserved-data、旧运行包及Codex安装文件未改，未退出或重启Codex，没有鼠标、剪贴板或消息/批准操作。

## 原因与实现

源码证实原观察器原桌面分支每轮逐绑定调用read_thread，轮间300毫秒；同一聊天多阶段还会重复读取。用户给出的484次/约29秒及20KB/s对120KB/s是用户提供的观察，未将其冒充本轮独立抓包或解密结论。

0.9.25普通路径只连接固定本机命名管道codex-ipc，经同SID/Session/登记包身份核验，订阅已有内存状态。已核实订阅入口不hydrate/readThread、不改变聊天owner；只改变本机follower集合并发送缓存snapshot/patches，解绑/退出取消订阅。已加载状态可能保留更久，这是实际本机副作用。正常产品路径没有聊天查询回退，没有CDP或专用启动前置条件。CLI目录查询仅在用户打开选择器/检查目录时使用本地useStateDbOnly元数据。

状态只保留ID/类型/轮次/状态等结构，严格按原Codex异步答复信封提取questionItemId并立即丢弃正文；不是自然语言猜测。版本11快照和严格连续revision增量校验，未知协议、远程host、缺帧/来源变化不当作空请求或恢复。

## 本轮结果

- 原包26.1016.8622.0：初步35秒只读探针收到1个快照及9个增量，无聊天查询、无观察端口/重开。精确安装包片段和脱敏证据在output/codex-traffic-20261023。
- 产品Observer普通默认启动：原桌面来源核验、准确当前聊天/轮次、本机事件进入隔离任务并回进行中、准确关联落盘/回读，以及从落盘重建连接9项通过；后台聊天查询0、轮次查询0，见test-output/codex-local-live-final/local-live.json。没有把这些操作当完整问答/批准往返或可见卡片鼠标验收。
- 本机投影隔离检查25项通过：异步问题、未接受回答、回答不恢复、等待期间执行不盖过等待、批准后新执行、Plan/输入响应补等待、终态、未知协议、缺帧重同步、canonical/legacy补问题、动态输入和准确命令启动。见test-output/codex-local-unit-final/local-unit.json。
- 核心规则334项、隔离观察器协议73项、初始化/设置离屏44项通过；它们分别覆盖多阶段等待/重连、旧终态、解绑重绑/归档恢复、红点、保存合并、首次提示/稍后/重开与原配置。证据分别为test-output/codex-local-core-a/report.json、codex-local-legacy-final/report.json、codex-local-package-ai/ai-initialization-report.json。准确轮次跟踪另50项合成检查见test-output/codex-local-turns-a/report.json。
- 新完整便携包普通入口实际启动检查退出0，ProductVersion为0.9.25；本包初始化离屏44项退出0。真实安装发现/本地目录/本机身份握手报告Ready且未选择聊天，见test-output/codex-local-environment-a/ai-initialization-environment.json；不将握手当具体聊天验证。

## 保留边界

未对正在使用的原桌面制造新问题、批准或失败，未耗尽额度；完整原桌面生命周期仍待专项验收。未加载/无owner的聊天不主动查询加载；非本机host不支持。复杂分页/legacy冲突、跨窗口owner转换、同名会话/混合DPI等未全面实测。无准确轮次的全局MCP请求不猜归属。全量历史源不能冒充实时源。

本轮没有进行原20/120KB/s条件下的整机公网字节差分，不能声称Codex整体绝对零流量；已验证新状态通道为本机管道、后台聊天查询为0。换包后需自行退出旧看板，旧进程继续运行会保留旧查询。压缩包逐文件与SHA256核对见BUILD-INFO-0.9.25.json；构建及完整性核验不替代产品验收。
