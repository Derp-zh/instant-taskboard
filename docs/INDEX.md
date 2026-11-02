# 项目文档索引

公开仍0.9.23；本地0.9.30整理Codex接入并重排设置，未发布。保留离线目录和准确实时处理，撤出不可靠文件执行／未知轮次补报；严格被动目标未完成。更新：2026-11-02。

| 入口 | 用途 | 读取时机 |
| --- | --- | --- |
| [HANDOFF](HANDOFF.md) | 当前运行、注意事项、能力缺口 | 新会话 |
| [PRD](PRD.md) | 现行需求范围和模块导航 | 确认需求依据 |
| [现行模块目录](current/README.md) | M01至M12、Codex、通知、共享背景动画输入的有效条目及验收 | 只选本轮相关页 |
| [当前状态](current/STATUS.md) | 实际交付/实测/未验收、当前包及发布记录 | 判断能力、准备交付 |
| [TASKS](TASKS.md) | 当前任务、缺口、模块依赖 | 安排实施 |
| [共享代码影响表](architecture/SHARED-CODE.md) | 当前代码职责、关键链路、消费者及回归选择 | 涉及共享代码 |
| [IMPLEMENTATION](IMPLEMENTATION.md) | 当前源码定位及本次整理记录 | 定点接手/实施记录 |
| [维护流程](DOCS-MAINTENANCE.md) | 原位更新新规则、历史按需、索引和摘要同步 | 每次规则/交付更新 |
| [INDEX-STATUS](INDEX-STATUS.md) | 实际索引连接/刷新/摘要同步结果 | 核实索引 |

现行规则以`docs/current/`为权威；STATUS只说明是否交付/验收，不把未实测能力写成通过。配置项或目录存在不等于实际接入。最新用户明确指令若尚未落文档，先同步对应现行条目，建议项仍需确认。

## 仅在需要时读取

- [0.9.30接入取舍与设置整理](ACCEPTANCE-0.9.30.md)、[机器结果](VERIFICATION-0.9.30.json)、[使用说明](USER-GUIDE-0.9.30.md)、[版本说明](RELEASE-NOTES-0.9.30.md)：本轮隔离验证、设置七页与边界；原桌面完整生命周期与严格无副作用仍未证明。

- [0.9.28独立评审](REVIEW-CODEX-0.9.28-20261024.md)：7个合成反例及1个通过对照、包／源码清单核对、下一轮修复与验收门；不能把30／90秒上限或完整准确性写成已保证。

- [0.9.28活动轮次订阅与本机文件投影](ACCEPTANCE-0.9.28.md)、[机器结果](VERIFICATION-0.9.28.json)、[包清单](BUILD-INFO-0.9.28.json)、[AI交接方案](AI-HANDOFF-CODEX-PASSIVE-0.9.28.md)：协议无免订阅读方法、闲置释放参数、已否路线与后续可选路线、真实本机只读探针；仍未达零订阅。

- [2026-10-24无订阅读取接续调查](ACCEPTANCE-CODEX-PASSIVE-20261024.md)：现行0.9.27正式运行核对、定向路由／发布revision反例、native V8锚点及本轮隔离复验；产品未改，缓存保留副作用未修复。

- [0.9.27目录离线修复与被动监测研究](ACCEPTANCE-0.9.27.md)：本地数据库／WAL、辅助冷启动外联来源移除、订阅保留副作用与原始内存读取边界；不是完整零流量交付。

- [0.9.26自动连接与目录恢复](ACCEPTANCE-0.9.26.md)：启动顺序/断线模拟、空目录与reader重建、两列表底部提示；当前Codex不重启，不将模拟当实机通过。

- [0.9.25本机状态与流量修复](ACCEPTANCE-0.9.25.md)：已有内存快照/增量，无后台聊天查询、无需端口或重开；保留整机流量及生命周期未验范围。

- [拖动绑定X01](current/X01-拖动绑定探索.md)：已并入0.9.24普通主开发启动，[本地入口与效果](DRAG-BINDING-MAIN-20261023.md)、[本版验收](ACCEPTANCE-0.9.24.md)；r2[历史说明](DRAG-BINDING-PREVIEW-GUIDE.md)保留原样。
- [拖动绑定并入主开发版提示词](DRAG-BINDING-INTEGRATION-PROMPT.md)、[实施交接](DRAG-BINDING-INTEGRATION.md)及[定向文件清单](DRAG-BINDING-INTEGRATION-MANIFEST.json)：2026-10-23交接后已执行定向集成，按用户修订预设0.9.24；本地完成与公开发布分别记录。
- [0.9.22本轮验收](ACCEPTANCE-0.9.22-ux.md)：阶段X删除与保护、六内置列表禁删、设置排版/保存、球红点数字同帧交接；开发隔离结果与[发布后真实鼠标补验收](ACCEPTANCE-0.9.22-mouse.md)分别记录，后者含19项检查、39帧和实际提示文字。
- [0.9.23验收](ACCEPTANCE-0.9.23.md)、[机器摘要](VERIFICATION-0.9.23.json)、[发布回执](GITHUB-RELEASE-0.9.23.json)：先连接再自检、保留旧配置、升级路径修复、只读测试及新观察适配；实际目录/原桌面通道通过，完整观察与生命周期待验收。初次初始化的历史检查见[0.9.21](ACCEPTANCE-0.9.21-ai-init.md)。
- [DSH接入探索C02](current/C02-DSH接入探索.md)：2026-10-23复核目录/空闲状态与来源身份、补查受控HTTP和官方被动API；状态缺口、待选插件/API路线与后续原桌面验收，未接入产品。
- [0.9.20宽度修正版验收](ACCEPTANCE-0.9.20-widthfix.md)、[证据](VERIFICATION-0.9.20-widthfix.json)、[包](BUILD-INFO-0.9.20-widthfix.json)、[已有GitHub回执](GITHUB-RELEASE-0.9.20.json)。
- [项目功能介绍](../README.md)与[英文版](../README_EN.md)已同步0.9.23；源码8d3d23e及Release下载逐文件核对，详见[回执](GITHUB-RELEASE-0.9.23.json)。
- [Codex观察使用与兼容](CODEX-PENDING-BRIDGE.md)、[0.9.19实际验收](ACCEPTANCE-0.9.19.md)及[分层证据](VERIFICATION-0.9.19.json)。
- [通知监听研究](NOTIFICATION-LISTENING.md)、[飞书探索](FEISHU-SYNC-RESEARCH.md)：技术参考不扩充已授权功能。
- [整理前文档/来源编号](history/20261021-before-current/README.md)、[旧程序资料](legacy/INDEX.md)：仅追溯，不默认读取。旧包和版本化证据维持原时点，不复用其“当前/本轮”描述。

Codebase Memory名`Instant-taskboard`，根目录和覆盖每次实际检查；文档检索优先限定current，代码按依赖定位。持久图`.codebase-memory/graph.db.zst`不是聊天记忆，缺口用源码核实。
