# 观察器合成回归

0.9.19-preview补充：包含可选本机请求观察桥的固定字段投影、首帧原子等待、同数量请求ID替换后的实际存储、多阶段跨重连、空pending但执行未确认、人工列与编辑合并。另以本工具进程实际持有的TCP监听器模拟页面CDP，检查监听PID、已加载模块哈希、固定方法、异常保护及首次对象查询复用。这个模拟器不执行原应用代码或脚本查询，不连接原桌面。

链接当前产品 `CodexObserver`、`CodexRpc`、`CodexTurnTracker`、`CodexDesktopBridge` 与 Core，在回环 WebSocket 上驱动完整观察器循环。测试不操作原桌面聊天、不启动真实轮次、不读取正式看板数据、不移动鼠标。

```powershell
dotnet run --project tools/CodexObserverChecks/CodexObserverChecks.csproj -- --data-dir test-output/codex-observer-new-run
```

输出目录必须位于仓库 `test-output` 下且尚不存在。Windows 环境需要允许当前进程创建回环 HTTP/WebSocket 监听器；工具沙箱不支持该监听时，应在获准的本机环境运行，不把沙箱错误当产品故障。

报告 `report.json` 分别记录通过项、调用数量、当前链接源码 SHA256，以及固定边界 `syntheticFixture: true`、`desktopLifecycleVerified: false`。覆盖旧历史、提问/请求处理/实际恢复、缺完成通知的精确轮次补查、交错会话、单会话不可用、解绑重绑、重连、轮询与原生通知竞态、历史非权威、只读方法及参数。测试通过不代表原 Codex 桌面生命周期或看板界面移列通过，原桌面验收必须另行记录。
