# GitHub运行包发布记录

2026-11-03已发布[即时任务看板0.9.30](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.30)，标签`v0.9.30`，源码提交`f554da9944fc24a53a41a03eaf455e781f822699`，确认为Latest。中英文首页、源码／相关检查、现行文档及完整Windows x64 ZIP／SHA256已同步；公开源码独立编译通过，远端源码归档410项及构建清单161项一致。重新下载ZIP的479个成员逐项哈希／长度一致，ZIP为77,205,581字节，SHA256 `E52D95A515A3A850D8481F96555C621E89DD41F362DD9D6A4F57516C76F832D8`。使用已验收原包，不操作正式数据或当前Codex；严格无副作用及完整原桌面生命周期仍未验收。见[发布回执](GITHUB-RELEASE-0.9.30.json)。以下记录保留各自历史时点。

2026-10-21已发布[即时任务看板0.9.20宽度修正版](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.20)，标签`v0.9.20`，源码提交`b7255e78559fcf29ce6ca0d3a727bd38d2819669`，已确认为Latest。同步更新源码/文档及完整485文件Windows x64便携ZIP与SHA256。最终ZIP大小79,842,129字节，SHA256：`F2D9C67ABF2E4753DC6F5C93EB20A16BB8BD887665AF6048148623E9DAC88B2F`；修正运行包说明中的失效链接后重新打包并替换了Release附件，两个附件均从公开Release重新下载，与GitHub服务端摘要及文件大小一致。双语README按qiangshou技能重构并通过配对校验。源码暂无LICENSE，README已如实注明授权条款未声明。

[版本说明](RELEASE-NOTES-0.9.20.md)、[机器回执](GITHUB-RELEASE-0.9.20.json)、[验收摘要](ACCEPTANCE-0.9.20-widthfix.md)。以下为历史发布记录。


2026-10-19已发布[即时任务看板0.9.16](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.16)，同步源码/文档及完整476文件ZIP、SHA256。源码标签提交`0a8e923e64983c515a89b24dfb503b6c65f1a53c`；两个附件实际下载及逐文件校验通过，确认为latest。原桌面执行/结束/双向移列已实测；异步提问仍缺信号，计划模式提问及权限等待/批准恢复未验收，不宣称完整接入通过。

[发布说明](GITHUB-UPDATE-0.9.16.md)、[机器回执](GITHUB-RELEASE-0.9.16.json)、[验收](ACCEPTANCE-0.9.16.md)。

以下为历史发布记录。

2026-10-15已发布[即时任务看板0.9.5](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.5)，当前最新版；源码标签v0.9.5对应提交`f034bc6b61b8019e370b321c23ff8e0c3d334a4b`。本次同时更新源码/文档及完整运行包，包含0.9.2至0.9.5累计修订，编辑窗口最终错位32/32。

- 附件：instant-taskboard-0.9.5-win-x64.zip，79,637,762字节，共476文件及.NET运行组件。
- 校验附件：instant-taskboard-0.9.5-win-x64.zip.sha256。
- ZIP SHA256：5AD4809019DDFACC75B2C23C9BD27CB769426EC3FD9832AD665EB933C0820131。
- 服务端附件大小及SHA256已核对；实际下载ZIP和SHA256文件均比对一致。正式发布、非草稿、非预发布，并确认为latest。
- 附件沿用已验收的本机完整ZIP，仅改公开附件名称，字节未变；公开源码另行构建、122规则与13模块检查通过。

详情见[GITHUB-UPDATE-0.9.5](GITHUB-UPDATE-0.9.5.md)，机器回执见[GITHUB-RELEASE-0.9.5.json](GITHUB-RELEASE-0.9.5.json)。

以下为历史发布记录。

2026-10-15已发布[即时任务看板0.9.1](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.1)，对应已验证的原本机完整便携交付文件，Release源码目标为b3b135bf523d7c44f90a9fc496cf869bcd773d61。

- 附件：instant-taskboard-0.9.1-win-x64.zip，79,600,310字节，内含476个文件与.NET运行组件。
- 校验附件：instant-taskboard-0.9.1-win-x64.zip.sha256。
- ZIP SHA256：0A754CA2FF3BB5E845093115F8B01F46281FE00865EBA031FDA7ED2A5B258BC9。
- GitHub服务端附件大小及SHA256已回读核对，实际下载ZIP与SHA256文件也已比对一致。正式发布为最新版，不是草稿或预发布。

源码、文档与公开运行包均不包含正式任务或用户配置。运行包保留内置默认音；公开源码构建从本机Windows准备音频。更新前请从旧版本托盘退出，完整解压新版本后运行“即时任务看板.exe”，保留整个目录。

用户已明确GitHub更新必须包含运行包；后续收到GitHub上传或更新请求时，源码/文档提交和对应Release发布均纳入交付，不能仅提交源码。该规则不自动授权普通本地修改向GitHub发布。
