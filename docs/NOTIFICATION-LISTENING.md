# Windows通知监听与逐应用音频

日期：2026-10-14。版本：0.8.1监听试用模块。

## 用户确认的需求

设置左侧新增“监听”，多个应用各自设置一个音频。开启某应用替换后，自动关闭其Windows通知提示音，保留通知横幅和该应用自身声音。支持WAV、MP3、M4A等常见音频。用户明确“参考文档仅作参考，以我的需求为主”，参考项目仅额外播放音频的做法不是本轮要求。

本模块不读取Codex聊天正文、不连接AI服务，也不根据通知自动移动任务。

## 使用

1. 设置→监听→刷新应用列表→添加应用。
2. 选择准确的Windows通知发送方；必要时填写横幅中的准确显示名。
3. 选择音频并试听，开启该应用替换和总开关。
4. 让应用发一条实际通知，检查近期捕获和声音；其他应用可继续独立添加。
5. 关闭总开关、关闭规则、删除规则或正常退出时尝试恢复原通知音。收起看板继续监听。

音频保存原路径，不复制文件；配置备份含规则和路径，不携带音频。移动音频后需要重新选择。

## 实现与边界

`NotificationCapture`提供Windows通知读取与辅助功能双通道，避免便携版获得权限但不触发事件时失去监听。Windows读取先记录历史通知，事件加两秒轮询。辅助功能只观察Shell通知宿主小窗口，按DPI过滤，遍历放在单个后台队列。来源不足的短通知不猜测；已知应用两行通知可识别。支持独立容器和重复应用标记的合并通知拆分。

`NotificationSoundRules`优先按准确Windows发送方标识匹配；辅助功能退回精确显示名，忽略大小写和末尾.exe，不模糊包含。同名有歧义时不播放。自身通知过滤，同内容五秒去重、内存记录有上限。队列最多16条等待，依次播放；队列满显示结果，单条超过两分钟停止。文件不存在或解码失败时不关闭原音；播放失败尝试恢复原音。

`WindowsNotificationSoundStore`只修改当前用户准确发送方的`SoundFile`字符串：`HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\<AppUserModelId>`。写空字符串关闭原通知音，不修改Enabled、ShowBanner、全局声音、程序音量或音频会话，不猜测或创建发送方。该注册表方式属于Windows实现细节，**不是微软公开承诺的按应用静音接口**；设置写入不等于实际声音已消除，必须在目标版本实测。

`NotificationSoundLease`修改前原子保存原值及值是否存在。恢复时，原来没有值就删除本模块写入的值，原来静音则继续静音。外部已改成其他值时保留外部更改。恢复失败保留记录、提示并阻止正常退出，允许重试；异常退出后下次启动先恢复再应用规则。当前用户本机恢复记录位于`%LOCALAPPDATA%\InstantTaskboard\notification-sound-restore.json`，不进入任务或配置备份；互斥锁避免多个看板同时管理声音。

辅助功能依赖可见横幅；勿扰、锁屏、横幅关闭、来源不明、系统界面变更可能漏捕获。Windows权限撤销且无其他通道时停止替换并尝试恢复原音。应用自行播放的声音保持，因此可能与自定义音同时响起。MediaPlayer支持的其他编码取决于本机解码器。

参考：[微软通知监听说明](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener)、[MediaPlayer](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.mediaplayer?view=windowsdesktop-8.0)、[作者直接使用SoundFile的通知脚本源码](https://github.com/imabdk/Toast-Notification-Script/blob/master/Remediate-ToastNotification.ps1)。注册表发送方路径已以本机只读枚举补充核实。

## 验证

Release构建零警告、零错误；原87项数据规则回归通过。43项监听隔离检查覆盖准确匹配、歧义、合并通知、去重、旧数据、配置备份、原值先持久保存、正常恢复、模拟重启、外部更改、失败重试、设置入口、多应用及保存。使用Windows编码器生成实际MP3/M4A，与WAV一起实际解码并零音量播放完成；损坏MP3及不存在的M4A被拒绝。隐藏控件树渲染检查界面，未移动鼠标、未操作正式任务数据。

证据：`test-output/listening-package-02/listening-report.json`、`listening-settings.png`、`listening-rules.png`；最终包另执行同一隔离验证，见`VERIFICATION-0.8.1.json`。

恢复测试使用替身存储，**未修改真实应用的Windows通知设置**，不能等同于真实注册表或听觉验收。至少两个真实应用不同音频、原提示音消失、横幅和应用自身声音保留、关闭恢复、异常重启、勿扰及睡眠场景仍待桌面验收。当前交付可试用实现，不能称为实机项目全部通过。
