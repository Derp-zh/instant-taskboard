"""Write public, content-free evidence from explicitly selected isolated reports."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'docs'

def read(path):
    return json.loads((ROOT / path).read_text(encoding='utf-8-sig'))

def prepend(path, marker, text):
    target = ROOT / path
    old = target.read_text(encoding='utf-8-sig')
    if marker in old:
        raise RuntimeError('Delivery already written: ' + path)
    first, rest = old.split('\n', 1)
    target.write_text(first + '\n\n' + text.strip() + '\n\n' + rest.lstrip(), encoding='utf-8')

reports = {
    'core': ('test-output/codex-core-0916-reconnect-final3/report.json', 205),
    'turnTracker': ('test-output/codex-turn-tracker-0916-final/report.json', 39),
    'observerFixture': ('test-output/codex-observer-0916-reconnect-final/report.json', 18),
    'desktopBridge': ('test-output/codex-desktop-bridge-0916-final/report.json', 25),
    'existingUI': ('test-output/codex0916-ux-final/ux0915-report.json', 41),
    'originalDesktop': ('test-output/codex0916-original-desktop-final5/codex-lifecycle-report.json', 184),
}
public = {}
for label, (path, count) in reports.items():
    value = read(path)
    actual = value.get('count') if isinstance(value.get('passed'), bool) else value.get('passed')
    if actual != count:
        raise RuntimeError('Unexpected check count: ' + path)
    public[label] = {'assertionsPassed': count, 'localReport': path, 'checks': value['checks']}

lifecycle = read(reports['originalDesktop'][0])
public['originalDesktop'].update({
    'originalDesktop': True, 'syntheticFixture': False,
    'actualCardsVerified': True, 'actualPersistenceVerified': True,
    'checkpoints': [
        {'label': s['label'], 'elapsedMs': s['ms'], 'source': s['source'],
         'identityVerified': s['identityVerified'], 'liveThreads': s['liveThreads'],
         'rows': [x for x in s['rows'] if x['alias'] in ('A', 'B', 'multi')]}
        for s in lifecycle['observations']
        if s['label'] not in ('poll', 'hide', 'edit', 'reminder', 'saveEdit', 'disconnect', 'unbind', 'bind', 'finish')
    ]
})
sources = [
    'src/Taskboard.Core/CodexState.cs', 'src/Taskboard.Core/BoardEngine.cs',
    'src/Taskboard.Desktop/CodexObserver.cs', 'src/Taskboard.Desktop/CodexDesktopBridge.cs',
    'src/Taskboard.Desktop/CodexTurnTracker.cs', 'src/Taskboard.Desktop/CodexChecks.cs',
    'src/Taskboard.Desktop/CodexDesktopLifecycleChecks.cs', 'src/Taskboard.Desktop/Program.cs',
    'src/Taskboard.Desktop/Taskboard.Desktop.csproj',
    'tests/Taskboard.Checks/CodexRecoveryRules.cs',
]
sha = lambda path: hashlib.sha256((ROOT / path).read_bytes()).hexdigest().upper()
verification = {
    'version': '0.9.16', 'date': '2026-10-19', 'fullIntegrationPassed': False,
    'runtime': 'release/即时任务看板-0.9.16-final',
    'originalDesktopPackage': 'OpenAI.Codex_26.1013.4894.0_x64__2p2nqsd0c76g0',
    'codexCliVersion': '0.163.0-alpha.10.2', 'nativeReadSchemaVersion': 1,
    'reports': public,
    'directoryDiagnostic': {
        'exitCode': 3, 'directoryPassed': True, 'sourceIdentityVerified': True,
        'fullIntegrationPassed': False,
        'localReport': 'test-output/codex-directory-0916-final-wait/codex-report.json',
    },
    'limitations': {
        'asyncUserInput': {
            'supported': False, 'visibleQuestionAttempted': True,
            'observedMetadata': {'type': 'active', 'activeFlags': []},
            'reason': '原桌面可见异步提问未在只读接口中暴露等待状态；不使用聊天正文或窗口文字猜测。',
        },
        'synchronousUserInput': {
            'originalDesktopVerified': False,
            'reason': '测试聊天为Default模式，同步request_user_input仅Plan模式可用；未完成Plan模式原桌面验收。',
        },
        'approvalWaitingAndAnswerRecovery': {
            'originalDesktopVerified': False, 'realPermissionRequestAttempted': True,
            'reason': '自建聊天确实发起require_escalated权限请求；自动审批环境未形成可人工暂停的等待批准状态，未观察到waitingOnApproval，不能算批准恢复验收。',
        },
        'sharedTerminalTransport': '观察器夹具通过，未做真实终端全生命周期验收。',
        'compatibility': '桥接依赖已核实桌面协议；远端host、未加载会话、未知schema、身份不符均不可作为实时来源。',
        'screenTesting': '最终包验证WPF卡片控件、实际编辑保存及持久化；没有重复进行真实鼠标或闪烁像素采样，不用旧版数量冒充。',
        'startupOrdering': '已加入未发现桌面时重试及历史降级后重新发现；未通过关闭用户原桌面来做启动顺序实测。',
    },
    'sourceHashes': {p: sha(p) for p in sources},
    'runtimeHashes': {p: sha('release/即时任务看板-0.9.16-final/' + p) for p in ('即时任务看板.exe', 'Taskboard.dll', 'Taskboard.Core.dll')},
    'privacy': {'productionDataChanged': False, 'conversationContentsIncluded': False, 'threadIdentifiersIncluded': False, 'rawScreenshotsIncluded': False},
    'codebaseMemory': {'project': 'Instant-taskboard', 'rootVerified': True, 'refreshed': True,
                       'indexedAtUtc': '2026-10-19T07:25:09Z', 'sourceFreshnessSignal': 'metadata_changed',
                       'supplement': '已直接核实本轮源码并记录源码与运行DLL哈希；PowerShell部分解析不完整。'},
}
(DOCS / 'VERIFICATION-0.9.16.json').write_text(json.dumps(verification, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

(DOCS / 'ACCEPTANCE-0.9.16.md').write_text('''# 0.9.16 Codex接入修复与验收

日期：2026-10-19。依据[0.9.14问题交接](CODEX-0914-HANDOFF.md)在0.9.15基础修复。保留已有PRD及界面交互；本次获用户明确授权进行原桌面测试与GitHub发布。

## 已修复

- 默认优先发现原Codex桌面的本机只读工具通道；核实服务端Windows包身份、同用户与同桌面会话，再逐个核实绑定thread、local host、schema及状态。连接成功不自动给所有会话授予实时能力。观察器只允许tools/list和固定read_thread，不创建、回答、批准或重跑聊天。
- 原桌面不要求用户重启、改全局配置或改用看板自有CLI。通道不可用仍保留历史只读降级；未加载、错误及未知协议不推断结束。先开看板后开桌面增加定期重试，但未关闭用户桌面来验收启动顺序。
- 每个绑定身份/代次维护准确当前轮次，跨连接保留；只对已观察轮次的明确终态补查结束，旧历史、仅idle、未知及重复结束均不冒充新结束。共享服务的轮次分页只取notLoaded元数据，读中遇到新事件则整批旧快照作废。
- 请求处理不等于恢复。可信实际恢复且整卡回进行中时才同时清时间红点；编辑保存合并最新后台状态。另修复原桌面实测发现的重连多阶段缺口：已确认等待/待检查的保护跨断线保留，其他阶段运行不得覆盖；全部相关阶段确认恢复后再解除。新建绑定、改绑及归档恢复不继承旧保护。
- 目录诊断拆分目录、通道身份、逐会话、生命周期及实际移列层次。--codex-verify只做只读目录/通道诊断；部分成功退出3，完整接入未验收始终明确为false，不再目录非空便passed:true。

## 实测范围

| 范围 | 本轮最终结果 |
| --- | --- |
| 核心规则及正式存储 | 205项通过，含新增重连等待保护与保存重读 |
| 轮次对账合成夹具 | 39项通过；不是原桌面验收 |
| 完整观察器WebSocket夹具 | 18项通过；含提问/批准/请求处理/恢复、补查与竞态；不是原桌面验收 |
| 桥接解析及原桌面只读目标核实 | 25项通过；不是提问/批准全流程验收 |
| 原有界面控件规则 | 最终包41项通过；未重复鼠标和闪烁像素采样 |
| 原桌面A/B与实际隔离看板 | 最终包184条断言通过；实际卡片控件所在列、编辑保存和存储重读均核实 |

原桌面A/B为Codex应用中可见的自建聊天，不是临时CLI会话。最终隔离目录为test-output/codex0916-original-desktop-final5。实际覆盖：绑定旧结束记录不误移，A/B交错执行与准确结束，进行中/待操作双向移动，多阶段等待优先，其他四列/自定义列/人工暂停保护，时间提醒、实际恢复清点、旧编辑保存合并、只关闭看板自己的只读管道后的重连、运行后再绑定、解绑重绑及旧结束不重放。机器摘要保留各检查点的卡片状态，参见[验证JSON](VERIFICATION-0.9.16.json)。

## 仍未支持或未验收

- **异步提问仍不能可靠自动移列**：原桌面显示问题时，read_thread及紧凑等待快照仍给出active且activeFlags为空，缺少可靠待回答信号。未将窗口文字或聊天正文猜测接入产品。完整接入验收为false。
- 同步request_user_input在本次Default测试聊天不可用，Plan模式原桌面提问及回答恢复未验收；有相应结构化信号时的规则仅由合成夹具验证。
- 原桌面确实发起了require_escalated权限请求，但当前自动审批环境未形成可人工暂停的等待批准状态，也未观察到waitingOnApproval。因此权限等待移列及批准后恢复未验收，不能用夹具结果替代。
- 真实终端共享接入、远端host、其他Codex版本、卸载/重装、混合DPI与热插拔未验收。左侧吸附和飞书实际同步保持待实现。

早期运行超时、会话因闲置未加载、编辑操作先清提醒导致的测试顺序失败均保留在私有test-output；候选包重连多阶段问题已复现并修复，不把候选包120条或0.9.15历史数量计入最终184条。正式任务、自启、权限配置及用户原有聊天没有改动；测试驱动只向自建A/B发送明确测试请求，产品观察器继续只读。

## 复核方式

运行核心检查、tools/CodexTurnTrackerChecks、tools/CodexObserverChecks及tools/CodexDesktopBridgeChecks，各工具README写明范围。所有看板检查必须显式使用新--data-dir。原桌面可见测试需先获用户授权，再以--codex-lifecycle-verify和本地lifecycle-plan.json指定两个自建聊天；tools/CodexLifecycleDriver.py是独立测试驱动，允许发送到显式A/B，不能当作产品观察器写入入口。真实编号、测试聊天正文和屏幕截图不发布。

协议核实参考[官方app-server资料](https://learn.chatgpt.com/docs/app-server)及本机0.163.0-alpha.10.2 schema。原桌面桥接经本机安装包的只读证据与实际通道核实，属于版本相关能力，并非承诺稳定的公开API。
''', encoding='utf-8')

prepend('docs/HANDOFF.md', '## 当前交付：0.9.16', '''
## 当前交付：0.9.16（2026-10-19）

本轮按CODEX-0914-HANDOFF修复原桌面只读接入、准确当前轮次补查、诊断分层、实际恢复清提醒及重连多阶段等待保护；保留0.9.15界面和触发范围。入口[本轮验收](ACCEPTANCE-0.9.16.md)、[逐层证据](VERIFICATION-0.9.16.json)，PRD40 U170至U176/A188至A194、TASKS28 T201至T208。0.9.14交接仍为历史审计，不改写成修复回执。

最终0.9.16包原桌面A/B实际卡片/存储184条断言、核心205、对账39、观察器夹具18、桥接25及界面41通过。**完整接入未通过验收**：异步问题在原接口没有等待信号，仍不能可靠移待操作；同步Plan提问未验收，自动审批环境未验收等待批准/批准恢复。禁止用目录、CLI临时聊天或夹具冒充这些通过。

本次用户明确要求更新GitHub，必须同步源码/文档与完整ZIP、SHA256，并回读远端及下载附件。最终本地入口release/即时任务看板-0.9.16-final/即时任务看板.exe。发布记录见GITHUB-RELEASES及GITHUB-RELEASE-0.9.16.json。正式数据、自启和权限配置不修改。测试A/B仅为本次自建聊天。

时间红点采用2026-10-19最新规则：可信实际恢复、整卡回进行中时同时清除；请求resolved、idle、未知或另有运行阶段等待时不清。AwaitingUserAction仅由已确认实时状态维护，跨断线保护多阶段等待；新绑定/改绑/归档恢复清除，不能由历史Phase自行建立。索引Instant-taskboard已full持久化刷新，C#metadata_changed继续以直接源码及最终DLL哈希补证。

## 以下为0.9.15及更早的历史接手记录
''')
prepend('docs/INDEX.md', '## 0.9.16当前入口', '''
## 0.9.16当前入口（2026-10-19）

- [验收与未支持范围](ACCEPTANCE-0.9.16.md)、[逐层实测摘要](VERIFICATION-0.9.16.json)、[完整包来源](BUILD-INFO-0.9.16.json)。原桌面执行、结束、双向移列及重连等待保护已实测；异步提问仍缺信号，同步Plan提问和权限等待/批准恢复未验收，完整接入不是通过。
- PRD第40节U170至U176/A188至A194，TASKS第28节T201至T208、IMPLEMENTATION的0.9.16节。保留第39节已确认界面交互；时间红点以本轮最新规则覆盖旧D12/R86。
- 用户本轮明确授权发布源码、完整便携ZIP与SHA256：[GitHub 0.9.16](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.16)，远端回执见GITHUB-RELEASE-0.9.16.json。下列0.9.15及旧版状态均为历史。
''')
prepend('docs/TASKS.md', '## 28 0.9.16', '''
## 28 0.9.16原Codex桌面接入修复（2026-10-19）

| 任务 | 当前结果 | 验收 |
| --- | --- | --- |
| T201 | 原桌面只读工具通道、包/SID/session核实与逐会话能力；最终原A/B核实通过 | A188 |
| T202 | 历史只读降级与未知保护；启动顺序定期发现已实现，未关闭用户桌面实测 | A188 |
| T203 | 绑定代次当前轮次、准确终态补查、幂等、旧事件及读中竞态隔离；对账39/观察器18通过 | A189 |
| T204 | 诊断分层、部分诊断退出3；最终包实际退出3，完整接入未验收为false | A190 |
| T205 | 真实恢复清时间红点、编辑合并与重连等待保护；核心205与原桌面实际检查点通过 | A191、A192 |
| T206 | 自建原桌面A/B与隔离WPF卡片/存储184断言；原界面41断言；正文/编号/截图不发布 | A193 |
| T207 | 异步提问待信号补齐；同步Plan提问、人工权限等待及批准恢复仍未验收，不得标全通过 | A193 |
| T208 | 用户明确要求源码/文档及0.9.16完整ZIP/SHA同步GitHub并回读核验 | A194 |
''')
with (DOCS / 'PRD.md').open('a', encoding='utf-8') as target:
    target.write('''

## 40 原Codex桌面接入修复与验收范围（0.9.16，2026-10-19）

按本轮CODEX-0914-HANDOFF及用户最新要求实施；保留第39节及既有界面交互。用户本轮批准按推荐规则处理并授权原桌面操作和GitHub发布，仅本次有效。

| 需求 / 验收 | 规则与效果 |
| --- | --- |
| U170 / A188 | 优先用户原Codex桌面，只读核实服务端身份与逐绑定实时能力；历史降级、未知/未加载/非本机host不冒充实时，不创建或接管用户聊天 |
| U171 / A189 | 只对当前绑定已观察准确轮次的明确终态补查；仅idle、旧历史、重复终态及旧绑定/连接事件不得误移。保留代次、epoch、序号与编辑合并 |
| U172 / A190 | 目录、通道、逐会话、生命周期和实际卡片/保存分层报告；--codex-verify部分诊断退出3，目录成功不得宣称完整接入通过 |
| U173 / A191 | 覆盖旧D12/R86及第22节冲突项：Codex可信实际恢复、整卡从待操作回进行中时，同时清时间红点。仅回答/批准请求处理、idle或未知不算恢复；其他运行阶段仍待操作时优先等待且保留红点 |
| U174 / A192 | 已确认等待/结果待检查保护跨断线保留；其他阶段执行不得借重连未知状态覆盖。真实恢复才解除，新绑定/改绑/归档恢复不继承旧保护。其他四列、自定义列及未运行阶段不自动移动 |
| U175 / A193 | 必须隔离看板与原桌面可见自建聊天实际验证，区分实测、夹具和未验收。异步提问当前无信号仍未支持，Plan提问及权限等待/批准恢复未验收，不凭文本猜测，不将工作流悄悄改为托管CLI |
| U176 / A194 | 本轮用户明确要求直接上传GitHub；同步源码/文档、完整便携ZIP及SHA256，核对远端版本和附件完整性；不改正式任务、自启或全局权限配置 |

准确结果与范围见ACCEPTANCE-0.9.16及VERIFICATION-0.9.16.json。第39节界面、左侧吸附T184与飞书实际接入边界保持。
''')
prepend('docs/IMPLEMENTATION.md', '## 0.9.16 原桌面只读接入', '''
## 0.9.16 原桌面只读接入、轮次对账与分层诊断（2026-10-19）

依据CODEX-0914-HANDOFF，PRD40/TASKS28。CodexDesktopBridge以固定tools/list/read_thread只读调用接入已安装原桌面的命名管道；验证包身份、SID及Windows会话，逐返回核对schema1、准确thread/local host和状态，限定超时/帧大小/分页，丢弃正文。未知或未加载逐会话降级，不给全部绑定统一Authoritative。CodexObserver优先原桌面，保留共享服务和历史只读退路及定期重新发现，目录另行延迟读取不改变实时权威性。

CodexTurnTracker按完整CodexWatch身份维护当前轮次，跨epoch保留、解绑删除；新活动轮次和准确终态转标准Core消息，再转同次状态快照。只读前保存Revision，通知到达使整批旧结果作废；旧连接回调、绑定代次与排队事件继续隔离。独立历史与仅idle不合成结束。共享服务thread/turns/list用desc/notLoaded，最多8页、重复游标终止。

Core仅在新可信状态时聚合移列；resolved/idle不借缓存Running清点。实际恢复并回进行中清时间红点，旧草稿合并保留后台变化。原桌面实测发现重连将B待检查清Unknown、A仍运行时多阶段错误回进行中，新增AwaitingUserAction只由已确认状态设立，跨断线/存储保留等待优先，真正恢复解除；新绑定和归档恢复清除。历史Phase不得自行授予保护。新增5条区分回归后核心205项通过，最终原桌面重连检查点确认多阶段仍待操作。

CodexChecks不再输出目录即passed:true；schema2明确分层，退出3代表目录/通道部分诊断，完整接入仍false。显式隔离生命周期模式与外部测试驱动分离，观察器无写工具；最终包原A/B184条实际卡片/编辑保存/持久化断言、界面41、核心205、对账39、观察器18、桥接25通过。来源及检查点见VERIFICATION-0.9.16；失败候选记录不删除，不计最终数量。

异步问题UI可见但原接口activeFlags为空，仍不能可靠移列；Default不能触发同步Plan提问，自动审批下未观察到人工权限等待，不能宣称提问/批准恢复原桌面通过。未安装插件、重启用户桌面、改变全局权限或切换托管CLI。0.9.15动画、命中、卡片已删除的Codex状态行均保持。最终包未重复真实鼠标/闪烁帧检查；正式数据和自启不操作。本轮用户要求同步GitHub源码及完整运行包。
''')
prepend('docs/USER-GUIDE.md', '# 0.9.16 Codex接入修订', '''
# 0.9.16 Codex接入修订

- 新版保留0.9.15现有界面、文字、按钮范围和真实控件开合。
- 默认尝试读取正在运行的原Codex桌面绑定聊天；已实测执行、正常结束及进行中/待操作双向移列，实际恢复回进行中也会清时间提醒红点。重连仍保留其他阶段已确认的待处理优先。
- **异步提问当前仍缺等待信号，不能保证自动进入待操作。** 同步Plan提问及权限等待/批准恢复未完成原桌面验收；完整接入不是全通过。具体见ACCEPTANCE-0.9.16。
- 未加载聊天、桌面通道不可用或版本不支持时保持状态待确认，不用历史完成记录猜测移列。只读目录检查成功不代表完整接入成功。
- 本轮按用户要求发布[GitHub 0.9.16完整便携包](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.16)及SHA256。升级前从旧版托盘退出，完整解压后运行“即时任务看板.exe”，保留原任务与配置目录。

以下为旧版本修订记录，冲突以本节及PRD40为准。
''')
print('Wrote content-free 0.9.16 delivery evidence and current entry points')
