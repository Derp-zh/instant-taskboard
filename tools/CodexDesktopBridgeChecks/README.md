# 原桌面只读桥接检查

`dotnet run --project tools/CodexDesktopBridgeChecks -c Release -- test-output/codex-desktop-bridge-checks/report.json`运行合成解析、身份匹配和允许操作检查。加`--discover`可验证本机已安装原桌面的进程包身份、同桌面会话、同用户SID，并检查其公开`read_thread`工具。受工具沙箱限制时，发现测试需要在已授权的本机执行环境运行。

默认及`--discover`不读取聊天。可再加`--targets <隔离测试目标JSON>`，仅只读核实显式指定测试聊天的当前状态及轮次元数据，并测试无效目标不会破坏其他目标；目标文件格式为`[{"alias":"A","threadId":"测试聊天编号"}]`。报告只保存别名、状态和轮次哈希，不保存正文或真实编号。工具不会创建、发消息、回答、批准或修改聊天。

发现和元数据读取通过不能代替原桌面生命周期与看板实际移列验收。产品只能调用`tools/list`及固定`read_thread`，不提供任意工具调用入口。桥接以本机已安装版本协议为准，未知版本/非本机host/未加载状态/格式不符保留不可用，不把历史目录等同实时连接。
