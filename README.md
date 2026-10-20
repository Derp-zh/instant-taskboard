<!-- README_SYNC: source=v0.9.20; updated=2026-10-21 -->
<p align="center">简体中文 · <a href="./README_EN.md">English</a></p>

# 即时任务看板

面向需要同时推进多件事的人：用六个独立列表和阶段卡片管理任务，把每个阶段的进度与对应会话放在一起。

如果它对你有用，可以点亮 Star，方便以后回来，也让有相同需求的人发现它。<br>
<a href="https://github.com/Derp-zh/instant-taskboard/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/Derp-zh/instant-taskboard?style=flat-square&label=Stars"></a>
<a href="https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.20"><img alt="Latest release" src="https://img.shields.io/github/v/release/Derp-zh/instant-taskboard?style=flat-square&label=release"></a>
<a href="https://github.com/Derp-zh/instant-taskboard/issues"><img alt="GitHub issues" src="https://img.shields.io/github/issues/Derp-zh/instant-taskboard?style=flat-square&label=issues"></a>
<a href="https://github.com/Derp-zh/instant-taskboard"><img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows%20x64-blue?style=flat-square"></a>

## 下载使用

下载 [0.9.20 Windows x64 完整便携包](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.20)及同页的 SHA256 文件，解压后运行 `即时任务看板.exe`。无需单独安装 .NET。升级前先从托盘退出旧版本；请保留完整解压目录。

默认任务与配置放在当前 Windows 用户目录下，可在设置中查看或更改。程序提供备份与恢复入口；升级不会要求你把任务上传到云端。

## 它适合怎么用

- **六个独立窗口**：灵感库、完善中、进行中、待操作、暂搁置、已完成，可按当前工作习惯摆放。
- **按阶段记录一件事**：一张任务卡可以有多个阶段，各阶段分别关联会话；正在运行的阶段可显示状态，待处理事项优先呈现。
- **把想法快速放进流程**：暂存池、灵感快捷录入、搜索、提醒、归档和撤销，帮助任务从记录走到完成。
- **让看板贴近桌面工作区**：列表窗口、悬浮球和托盘入口；窗口展开使用真实控件裁切，繁忙时动画可能掉帧。

## AI 会话观察边界

可选的 Codex 观察模式默认关闭。它通过本机观察入口读取有限的会话状态；不会替你回答问题或批准操作，也不读取聊天正文来猜任务状态。它依赖兼容版本的 Codex 和手动启动步骤，具体适用范围及已知未验收情形见[观察说明](docs/CODEX-PENDING-BRIDGE-PUBLIC.md)。这不是对所有 AI 应用的通用接入。

飞书云端同步尚未接入。任务内容默认存于本机；请按你的设备备份习惯保护数据。

## 从源码构建

需要 Windows、.NET 8 SDK、PowerShell 7 和 NuGet 网络连接。构建脚本会把 Windows 系统提供的默认提示音复制到运行目录；源码仓库不携带这份系统音频。

```powershell
pwsh -NoProfile -File tools/Build-Portable.ps1 -Output release/portable
```

完整需求和使用方式见[使用说明](docs/USER-GUIDE-0.9.20.md)、[需求文档](docs/PRD.md)与[任务清单](docs/TASKS.md)。第三方组件声明见[NOTICE](docs/THIRD-PARTY-NOTICES.txt)。

## 授权与反馈

当前仓库没有 `LICENSE` 文件，因此源码复用、修改和再分发的授权条款尚未声明；不要仅凭代码公开推定许可证。运行包中的第三方组件依各自声明适用。

遇到问题或希望讨论改进，可开一个 [Issue](https://github.com/Derp-zh/instant-taskboard/issues)。如果这个工具适合你的工作流，也欢迎在顶部点 Star 收藏。
