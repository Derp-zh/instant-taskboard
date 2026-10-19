# 即时任务看板

当前版本0.9.16：修复原Codex桌面只读接入、准确当前轮次的结束补查、分层诊断、实际恢复清时间红点，以及重连后的多阶段等待优先。保留0.9.15的卡片文字循环、深蓝黑保存文字、真实控件开合及原有操作范围。

原桌面A/B实际执行、结束、双向移列、重连与编辑保存已实测。**异步提问当前仍缺等待信号；同步Plan提问及权限等待/批准恢复尚未完成原桌面验收，完整接入不是全通过。** 见[验收与限制](docs/ACCEPTANCE-0.9.16.md)、[逐层证据](docs/VERIFICATION-0.9.16.json)和[文档入口](docs/INDEX.md)。

## 下载运行包

[即时任务看板0.9.16完整便携包](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.16)。下载instant-taskboard-0.9.16-win-x64.zip与SHA256校验文件，完整解压；从旧版本托盘退出后，运行解压目录中的“即时任务看板.exe”。无需另装.NET，请保留整个目录。默认任务和配置保存在系统用户目录，升级时保留原目录及路径配置。

## 功能

- 六个独立列表：灵感库、完善中、进行中、待操作、暂搁置、已完成；支持调整、拖动、归档及撤销。
- 阶段独立运行及会话关联，多运行阶段待处理优先；仅内置进行中与待操作自动流转，其他分类保留人工操作。
- 共享新建暂存池、自动保存、灵感快捷录入、搜索、提醒、备份、独立数据和配置目录。
- 桌面边缘右侧吸附与自动隐藏、悬浮球及托盘菜单、快捷键展开。左侧吸附待后续。
- 列表、新建/编辑/设置和球开合使用真实控件裁切，目标100毫秒；繁忙时可能掉帧。暂存抽牌约266.67毫秒，原外点/快速重开/操作范围保持。

飞书实际同步尚未接入；通知监听的真实应用捕获与系统通知音替换仍有专项验收边界。完整规则见[PRD](docs/PRD.md)和[使用说明](docs/USER-GUIDE.md)。

## 构建与验证

开发构建需要Windows、.NET 8 SDK、PowerShell 7及可访问NuGet的网络。Windows默认音频在构建时从本机系统目录准备，不随公开源码提供。

```powershell
pwsh -NoProfile -File tools/Build-Portable.ps1 -Output release/portable
dotnet run --project tests/Taskboard.Checks -- --data-dir test-output/new-core-run
dotnet run --project tools/CodexTurnTrackerChecks -c Release -- test-output/new-turn-run/report.json
dotnet run --project tools/CodexObserverChecks -c Release -- --data-dir test-output/new-observer-run
```

所有验证使用新的隔离目录。桌面交互会移动鼠标时先协调用户；原桌面聊天测试必须只操作明确授权的自建聊天。目录诊断--codex-verify部分完成退出3，并不表示完整接入通过。验证工具范围见各README及本轮验收说明。

正式任务、旧数据保留区、测试输出、本机配置及构建产物不上传源码仓库。第三方许可见[声明](docs/THIRD-PARTY-NOTICES.txt)。
