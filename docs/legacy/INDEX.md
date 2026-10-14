# 旧程序拆解资料索引

日期：2026-10-12。旧程序的实现资料作为历史参考保存，新的产品要求以 [PRD](../PRD.md) 为准。

## 1 重写入口

| 资料 | 用途 |
| --- | --- |
| [重写思路](../REWRITE-IDEAS.md) | 按模块说明旧写法、可借鉴逻辑、新版改法和必须舍弃的部分 |
| [新旧功能对应](FEATURE-MAP.md) | 覆盖T01至T28及全部平台后续任务，区分已有核心、部分相似、未实现 |
| [旧程序拆解](ANALYSIS.md) | 架构、主要调用链、已知问题、证据边界 |
| [REA静态核对](REA-FINDINGS.md) | 各旧运行包身份、有限成员比较、未知与错误边界 |
| [清理记录](CLEANUP.md) | 删除范围、核对结果、Git历史及用户数据保留 |

## 2 已保存的历史证据

| 证据 | 用途 |
| --- | --- |
| [30组源码摘录](evidence/SOURCE-EXCERPTS.md) | E01至E30局部写法，供无需恢复整个旧程序时查阅 |
| [源码来源清单](evidence/source-inventory.json) | 20文件、2453行的路径、SHA256、行数及Git提交 |
| [历史架构导出](evidence/architecture.json) | 删除前图结构和模块联系 |
| [历史方法索引](evidence/method-index.json) | 181个方法的旧符号、文件和行号 |
| [AI事件调用链](evidence/ai-event-callers.json) | 实际直接调用者只有自测，旧监听未接入 |
| [压缩包清单](evidence/package-inventory.json) | 待删除初版ZIP的身份与9个成员，无data数据成员 |
| [用户数据保留清单](evidence/preserved-data-manifest.json) | 已复制数据的路径、大小与SHA256，不在文档里展开任务内容 |
| [清理目标清单](evidence/removal-manifest.json) | 确认删除的路径、类别、大小、哈希及执行结果 |
| [20261012规则自测](evidence/20261012规则自测.txt) | 前次源码构建的20项规则自测，非新版验收 |
| [旧版使用说明](旧版使用说明.md) | 删除前旧版能力描述，仅历史参考 |
| [旧版验证记录](旧版验证记录.md) | 旧桌面与性能记录，含尚未验证范围 |

REA的各身份/成员文件由REA-FINDINGS统一索引。若存在旧桌面测试原文，则保存在evidence目录；测试中跳过项仍按跳过处理。

## 3 索引与恢复边界

`index-snapshot/graph.db.zst`与`artifact.json`是删除前Codebase Memory索引快照。它记录旧节点关系和绝对路径，不包含一个可运行旧程序，已删除源码的实时片段不能依赖此快照获取。不要把它覆盖到当前`.codebase-memory`目录。

当前Codebase Memory项目`taskboard`会在清理后重新索引文档；旧类/方法应查本目录的历史导出，或在Git历史读取。Git历史保留，但没有新提交、重置或清除历史操作。程序删除只针对当前工作区。
