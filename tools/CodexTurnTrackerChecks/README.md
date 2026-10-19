# 准确轮次终态对账夹具

运行：`dotnet run --project tools/CodexTurnTrackerChecks -c Release -- test-output/codex-turn-tracker-0916/report.json`。

直接编译产品 `CodexTurnTracker.cs`，使用完全合成的会话及轮次元数据，覆盖缺少完成事件的补查、历史基线、轮次幂等、重连、重新绑定、A/B隔离和分页故障。不会启动Codex、读取聊天正文、操作屏幕或正式看板数据。此结果不能代替原Codex桌面可见聊天的真实接入和移列验收。

每个绑定身份及代次创建一个tracker，跨连接epoch保留；解绑/改绑后丢弃。观察器仍须隔离旧连接回调及绑定代次。在只读请求之前保存`Revision`，若期间有通知，则丢弃这一轮旧快照并重试。先转发`Reconcile`合成消息，再转发同批次状态快照，避免覆盖正在等待回答/批准的状态。只有经过逐会话验证的实时来源可传`authoritative:true`。

协议依本机 `codex-cli 0.163.0-alpha.10.2 app-server generate-json-schema --experimental` 核实：`thread/turns/list`使用`sortDirection:desc`、`itemsView:notLoaded`、`cursor`和`nextCursor`。只保留`id/status`；首次读取仅最新一页，精确当前ID补查最多八页。页上限、重复游标、缺失目标或未知状态均不推断完成。
