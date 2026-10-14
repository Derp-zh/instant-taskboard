# 即时任务看板

Windows 桌面任务看板，通过悬浮球快速展开多个独立列表窗口，管理灵感、任务阶段、提醒和暂存编辑。

当前版本：0.8.1。需求入口见 [文档索引](docs/INDEX.md)，使用方法见 [使用说明](docs/USER-GUIDE.md)。

## 功能

- 多列表独立窗口，可调整名称、数量、显隐及整组大小；有任务的列表禁止删除。
- 灵感快速录入，新建任务自动暂存，阶段详情、AI 与优先级选项可编辑。
- 任务拖动跟随鼠标，虚线预览落点，支持跨列移动、排序和最近 20 步撤销。
- 暗色实时毛玻璃、圆形悬浮球、搜索、待操作预览和设置分区。
- 每天、每周与一次提醒；配置备份和独立数据目录。
- Codex 现有聊天关联与只读历史监听入口。

动画支持 120Hz，实际显示流畅度受屏幕刷新率、Windows 合成器及硬件影响。Codex 桌面实时桥接仍待确认，飞书实际同步尚未接入。通知监听模块已通过隔离检查，真实应用捕获与系统通知音替换仍需专项验收。完整边界见 [实施记录](docs/IMPLEMENTATION.md)。

## 运行与构建

便携包解压后运行 `即时任务看板.exe`，请保留整个目录。升级前退出旧程序并保留原来的数据目录与启动路径配置。

开发构建需要 Windows、.NET 8 SDK，以及可访问 NuGet 的网络。

```powershell
dotnet build src/Taskboard.Desktop/Taskboard.Desktop.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Portable.ps1 -Output release/portable
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

此仓库只保存当前源码、测试工具和整理后的需求文档。历史取证原文、旧索引快照、真实聊天编号、原始 Codex 探测报告与本机路径不在公开范围；文档中指向这些历史附件的链接仅说明本地资料来源。验证文件中的路径已匿名化，运行包 SHA256 保持原值。
