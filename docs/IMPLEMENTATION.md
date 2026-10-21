# 当前实施入口

0.9.22源码对应桌面项目Version 0.9.22，公开ZIP采用已隔离验证的自包含win-x64构建。

- 阶段X及原保存链：`EditorSurface.cs`、Core `BoardEngine.cs`/`Validation.cs`。
- 六内置列保护与旧配置：Core `BoardColumns.cs`/`Models.cs`。
- 设置分组/保存：`SettingsSurface.cs`/`SettingsLayout.cs`/`SettingsHotkey.cs`，保留原控件和事件。
- 球提示交接：`BallSurface.EdgeHide.cs`，数字预布局，统一帧回调交接opacity；原位移/命中不改。
- 初始化：`CodexInstallationDiscovery.cs`、`CodexInitialization.cs`、`BoardController.AiInitialization.cs`、`SettingsAiInitialization.cs`；准确兼容profile检查沿用观察桥，不猜新对象。
- 检查：Core `StageDeletionRules.cs`；Desktop `Ux0922Checks.cs`/`AiInitializationChecks.cs`；实际覆盖见[验收](ACCEPTANCE-0.9.22-PUBLIC.md)。

共享层消费者和保持范围见[影响表](architecture/SHARED-CODE.md)。源码构建使用`tools/Build-Portable.ps1`；该脚本保留Windows系统提示音构建回填，系统音频不随源码提供。
