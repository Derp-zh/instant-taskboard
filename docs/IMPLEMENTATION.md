# 当前实施

0.9.23修改安装发现、CodexInitialization、BoardController.AiInitialization、SettingsAiInitialization及原Observer/DesktopBridge/PendingBridge。

先尝试只读连接，来源验证与协议检查独立于版本号。非打包进程核对系统登记最终路径、有效OpenAI签名及同用户/Session；旧服务地址保留，桌面连接与目录按实际可用性自动选择。商店升级同包同相对路径修复并备份，任意失效手动路径仍需选择。

内部观察仍匹配精确模块内容哈希后才使用固定投影，支持Hfn/Dot旧合同与imn/nct新合同。没有未知对象猜测、安装修改、自动重启或聊天写操作。只读测试不发布任务事件，检查期间改绑/配置变化拒绝旧结果。

影响消费者与保持范围见[共享影响](architecture/SHARED-CODE.md)，实际结果见[验收](ACCEPTANCE-0.9.23.md)。Codebase Memory实际检查/定点读取后已刷新；部分PowerShell解析缺口用源码补证，摘要写工具缺失不冒称同步。

独立构建便携包，不替换正式运行文件。用户本轮明确要求GitHub更新，源码、完整ZIP与SHA256同步，发布后重新下载并逐文件校验。
