# 即时任务看板

Windows 桌面任务看板，通过悬浮球快速展开多个独立列表窗口，管理灵感、任务阶段、提醒和暂存编辑。

当前交付版本：0.9.1。紧凑卡片、快捷新建、平台多选、窗口外点与扑克牌草稿切换已完成隔离和实机验收。需求入口见 [文档索引](docs/INDEX.md)，使用方法见 [使用说明](docs/USER-GUIDE.md)。

## 下载运行包

当前Windows x64完整便携包：[即时任务看板0.9.1](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.1)。下载instant-taskboard-0.9.1-win-x64.zip，完整解压；从旧版托盘退出程序后，打开解压目录中的“即时任务看板.exe”。无需另装.NET，请保留整个目录。Release同时提供SHA256校验文件。

## 功能

- 多列表独立窗口，可调整名称、数量、显隐及整组大小，整组缩放时最大范围居中；有任务的列表禁止删除。
- 灵感快速录入，新建任务自动暂存，阶段详情、平台与优先级选项可编辑。
- 任务拖动跟随鼠标，虚线预览落点，支持跨列移动、排序和最近 20 步撤销与恢复。
- 已完成、暂搁置任务可归档，保存最近20条完整任务，并可取消归档。
- 紧凑列表标题区、青色任务标题及细竖条、白色阶段详情；灵感快捷录入与正式卡片共用布局。
- 编辑/设置连续两次外点关闭并暂存；草稿200毫秒扑克牌切换、创建顺序编号、整组X关闭和左下删除入口。
- 暗色实时毛玻璃、圆形悬浮球、搜索、待操作预览和设置分区。
- 新建、编辑和设置窗口的100毫秒中央画卷开合；通知监听可使用内置Alarm04或逐应用自选音频。
- 每天、每周与一次提醒；配置备份和独立的数据、配置目录，默认保存在系统用户目录。
- 自启登记使用仍存在的最新程序版本，升级后修复启动入口。
- Codex 现有聊天关联与只读历史监听入口。

动画支持 120Hz，实际显示流畅度受屏幕刷新率、Windows 合成器及硬件影响。Codex 桌面实时桥接仍待确认，飞书实际同步尚未接入。通知监听模块已通过隔离检查，真实应用捕获与系统通知音替换仍需专项验收。完整边界见 [实施记录](docs/IMPLEMENTATION.md)。

## 运行与构建

便携包解压后运行 `即时任务看板.exe`，请保留整个目录。升级前退出旧程序并保留原来的数据目录与启动路径配置。默认任务与配置分别位于 `%LOCALAPPDATA%/InstantTaskboard/data` 和 `config`，可在设置中更改；首次启动会检查旧便携数据，原件保留。

开发构建需要 Windows、.NET 8 SDK、PowerShell 7，以及可访问 NuGet 的网络。

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/Build-Portable.ps1 -Output release/portable
```

规则检查使用新的隔离目录：

```powershell
dotnet run --project tests/Taskboard.Checks -- --data-dir test-output/readme-core-check
python -m unittest discover -s tests/codex_probe -v
```

桌面交互检查会移动鼠标，运行前请协调当前操作。不要使用正式数据目录进行测试。

## 项目目录

| 目录 | 内容 |
| --- | --- |
| `src/Taskboard.Core` | 任务、列表、提醒、撤销与配置规则 |
| `src/Taskboard.Desktop` | 桌面窗口、输入、动画与接入模块 |
| `tests` | 隔离规则与 Codex 模块检查 |
| `tools` | 构建、打包、验收与 Codex 探索工具 |
| `docs` | PRD、任务清单、设计记录与验证摘要 |

正式任务数据、旧数据保留区、测试输出、本机配置和构建产物不应上传到代码仓库。第三方许可见 [第三方声明](docs/THIRD-PARTY-NOTICES.txt)。

## 公开仓库资料范围

本次更新为0.9.1源码及整理后的需求/验收文档。正式任务、用户配置、认证文件、原始聊天编号、历史取证附件和运行输出未发布。文档中的本机路径和会话编号已匿名化；指向历史本地运行包、旧取证及test-output的链接记录证据来源，不表示这些历史产物已经上传；当前0.9.1运行包见上方Release下载入口。验证摘要的DLL与压缩包哈希保留为原本机交付证据，不是本公开副本新构建的字节哈希。

默认通知音Alarm04.wav来自Windows系统，不随公开源码发布。请在Windows上使用tools/Build-Portable.ps1构建：脚本会从本机Windows媒体目录取得音频并内置到本地程序，音频文件由.gitignore排除。程序功能与本机0.9.1一致，公开副本仅增加音频准备步骤并脱敏文档。0.9.1完整运行包已独立发布到GitHub Release，附件哈希对应原本机BUILD-INFO-0.9.1.json。
