# 0.9.14 原包观察器审计

这是独立受控复现工具，不修改产品源码，不读取正式任务，不操作原 Codex 桌面聊天，也不注入鼠标键盘。完整结论见 [修复交接](../../docs/CODEX-0914-HANDOFF.md)。

在仓库根目录运行，要求 Windows、.NET 8 SDK，以及 `release/即时任务看板-0.9.14-final` 原包。项目引用原包的 JSON 运行库，以兼容原程序集的依赖版本；不随产品打包。

```powershell
dotnet run --project tools/CodexReleaseAudit/CodexReleaseAudit.csproj -- --assembly 'release/即时任务看板-0.9.14-final/Taskboard.dll' --data-dir 'test-output/codex-0914-audit-new-run'
```

每次使用 `test-output` 内不存在的新目录；工具拒绝复用输出目录。输出 `audit-report.json` 和 `fixture-requests.jsonl`。日志仅含合成 thread/turn 和状态，不含真实聊天内容。

工具加载原 Taskboard.dll 和 Taskboard.Core.dll，使用原观察器、RPC、状态映射。其子进程作为合成 app-server：先运行，再等待回答，再恢复运行，最后只报告 idle；若观察器请求轮次列表，则可返回合成 completed 轮次。工具订阅原观察器 Event 回调后把事件交给原 Core，不测试桌面 BoardController、卡片渲染或正式持久化。

当前原包预期结果：识别等待及恢复，但完成后仍停在 Running，且轮次补查为0。**退出码0表示成功复现这个缺口，不表示 Codex 接入可用。** `syntheticFixture:true` 和 `desktopLifecycleVerified:false` 必须保留。它也不能证明原桌面可提供权威实时信号；权威标志仅反映原观察器在夹具连接上的推断。

修复后此工具的预期可能改变；应新增正反回归而非继续把“复现缺口”当通过标准。真实桌面会话及实际看板移列须另验收。
