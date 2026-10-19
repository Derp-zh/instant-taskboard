# GitHub 0.9.16更新与回读

已按用户本轮要求同步源码、文档和[0.9.16正式Release](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.16)；非草稿、非预发布，回读确认为latest。

源码标签v0.9.16对应提交`0a8e923e64983c515a89b24dfb503b6c65f1a53c`。包含此前本地确认的0.9.6至0.9.15界面修订及本轮Codex修复。公开源副本独立构建成功；108个运行源码文件与受测来源按内容一致，81个暂存Git字节一致，其余仅Git行尾规范化差异。发布运行包使用最终原桌面实测的本地包，不以另外构建的运行文件替代。Windows音频不放入公开源码，构建时从本机Windows准备。

- 附件instant-taskboard-0.9.16-win-x64.zip：79,802,779字节、476文件，包含.NET组件。
- SHA256：`E24B6929D925ECCD9B9F8F56E85B5FFB99CCA76BACF021EE140A3AFCA08B5CC7`。
- ZIP与SHA256均实际下载；服务端digest、文件大小、下载摘要与本地一致，下载ZIP全部476项逐文件校验通过。
- 正式任务、配置、保留数据、聊天正文、真实聊天编号、原始截图、凭据、索引数据库和厂商解包模块不上传。
- 最终原桌面自建A/B与隔离看板184条断言通过。异步提问仍缺信号，Plan提问及权限等待/批准恢复未验收；完整Codex接入不是通过。

机器回执见[GITHUB-RELEASE-0.9.16.json](GITHUB-RELEASE-0.9.16.json)，范围见[验收](ACCEPTANCE-0.9.16.md)。发布回执另提交至main，版本标签保持指向上面的实际源码提交。主工作区旧工程Git状态保留，实际上传从.lark-tmp/github-update-091副本进行，不恢复旧源码。
