import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = 'release/即时任务看板-0.9.15-final'


def read(path):
    return (ROOT / path).read_text(encoding='utf-8-sig')


def write(path, value):
    (ROOT / path).write_text(value.rstrip() + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256((ROOT / path).read_bytes()).hexdigest().upper()


reports = []
desktop_path = 'test-output/ux0915-final11-ux0914-desktop/ux0914-desktop-report.json'
desktop = json.loads(read(desktop_path)) if (ROOT / desktop_path).is_file() else None
for kind, name, expected in [
    ('ux0915', 'ux0915-report.json', 41),
    ('ux099', 'ux099-report.json', 30),
    ('ux0910', 'ux0910-report.json', 15),
    ('ux0911', 'ux0911-report.json', 37),
    ('ux0912', 'ux0912-report.json', 23),
    ('ux0913', 'ux0913-report.json', 31),
    ('ux0914', 'ux0914-report.json', 30),
    ('stage', 'stage-report.json', 31),
    ('integration', 'integration-report.json', 17),
    ('ux0915-render', 'ux0915-render-report.json', 24),
    ('ux0913-render', 'ux0913-render-report.json', 16),
    ('ux0913-render', 'ux0914-closing-report.json', 6),
    ('ux0912-render', 'render-report.json', 54),
    ('scroll', 'scroll-report.json', None),
    ('ux0914-desktop', 'ux0914-desktop-report.json', 26),
]:
    if kind == 'ux0914-desktop' and desktop is None:
        continue
    prefix = 'ux0915-final11-solo2' if kind in {'scroll', 'ux0915-render', 'ux0913-render', 'ux0912-render'} else 'ux0915-final11'
    path = f'test-output/{prefix}-{kind}/{name}'
    result = json.loads(read(path))
    count = result['passed']
    if (expected is not None and count != expected) or (expected is None and count < 80):
        raise RuntimeError('检查不完整：' + path)
    if any((ROOT / path).parent.joinpath(n).exists() for n in ['startup-error.txt', 'errors.log']):
        raise RuntimeError('检查目录仍有错误：' + path)
    reports.append(dict(Kind=kind, Report=path, Passed=count, SHA256=sha(path)))

core_path = 'test-output/ux0915-final-core/report.json'
if json.loads(read(core_path))['passed'] != 171 or sha(RUNTIME + '/Taskboard.Core.dll') != sha('test-output/ux0915-core-runner/Taskboard.Core.dll'):
    raise RuntimeError('最终Core与规则检查来源不一致')
reports.append(dict(Kind='core', Report=core_path, Passed=171, SHA256=sha(core_path)))

extra = []
for kind, filename in [('performance', 'performance-report.json'), ('animation', 'animation-pixels.json')]:
    path = f'test-output/ux0915-final11-solo2-{kind}/{filename}'
    if json.loads(read(path))['passed'] is not True:
        raise RuntimeError('抽牌或响应检查未通过')
    extra.append(dict(Report=path, SHA256=sha(path), Passed=True))

render = json.loads(read('test-output/ux0915-final11-solo2-ux0915-render/ux0915-render-report.json'))
if len(render['measurements']) != 12 or any(m['baseline'] <= 5 or m['baseline'] >= 100 or m['delta'] >= 2 for m in render['measurements']):
    raise RuntimeError('真实窗口基线或展开结束稳定性未通过')
if desktop is not None and (not desktop['mouseMoved'] or not desktop['cursorRestored']):
    raise RuntimeError('鼠标恢复记录不完整')
scroll = json.loads(read('test-output/ux0915-final11-solo2-scroll/scroll-report.json'))
white = [m for m in scroll['evidence'] if 'openingWhiteCycles' in m]
if len(white) != 3:
    raise RuntimeError('三类窗口整组白条证据缺失')

ball = json.loads(read('test-output/ux0915-final11-solo2-animation/animation-pixels.json'))
if not ball['openingMonotonic'] or not ball['openingIntermediate'] or not ball['monotonic'] or not ball['closingMonotonic'] or ball['independent'] or ball['rootWidth'] != 370:
    raise RuntimeError('球真实双向开合或固定画布证据未通过')
shared_result = 'test-output/ux0915-final11-shared/shared-motion-report.json'
shared_error = 'test-output/ux0915-final11-shared/startup-error.txt'
if (ROOT / shared_result).is_file():
    shared_probe = dict(Passed=json.loads(read(shared_result))['passed'], Report=shared_result, SHA256=sha(shared_result))
elif (ROOT / shared_error).is_file():
    shared_probe = dict(Passed=False, Report=shared_error, SHA256=sha(shared_error), Error=read(shared_error), Previous0914Report='test-output/ux0915-previous-shared-baseline/startup-error.txt', Previous0914SHA256=sha('test-output/ux0915-previous-shared-baseline/startup-error.txt'))
else:
    raise RuntimeError('额外抽牌检查缺少实际运行结果')
shared_probe.update(
    OverlayObserved=True,
    OverlayScreenshot='test-output/ux0915-final11-shared/ball-expanded-screen.png',
    SecondProbeError='test-output/ux0915-final11-shared-clear/startup-error.txt',
    SecondProbeScreenshot='test-output/ux0915-final11-shared-clear/ball-expanded-screen.png',
    Scope='两次实际截图均为遮住球采样区域的ChatGPT桌面通知；该探测未走到共享草稿断言，不当作产品通过或已确认草稿回归。未关闭或移动用户窗口。')

total = sum(r['Passed'] for r in reports)
verification = dict(
    Version='0.9.15', Date='2026-10-19', LocalOnly=True,
    Entry=RUNTIME + '/即时任务看板.exe', Passed=total,
    Reports=reports, ExtraChecks=extra,
    RuntimeHashes=[dict(Path=n, SHA256=sha(RUNTIME + '/' + n)) for n in ['Taskboard.dll', 'Taskboard.Core.dll', '即时任务看板.exe']],
    SamePortableForUiChecks=True, SamePortableForDesktopAndUiChecks=desktop is not None, CoreUsesPublishedAssembly=True,
    DesktopChecks=dict(Status='passed' if desktop is not None else 'awaiting_user_mouse_coordination', Report=desktop_path if desktop is not None else None),
    NewRequirements=dict(SaveText='#102033', ConversationWidth='6-10 M at inherited font', MarqueeStartSeconds=.5, MarqueeEndSeconds=.3, PixelsPerSecond=24,
        DeletedCodexStatusLine=True, FloatingOpenClose='live controls and native aperture, no snapshot handoff', DurationTargetMs=100,
        UserAcceptedBusyUiDroppedFrames=True),
    RepeatedOpenSampling=dict(Cycles=12, NewTaskCycles=8, EditCycles=2, SettingsCycles=2,
        MaxLateDifference=max(m['delta'] for m in render['measurements']), ActualDarkWindowBaselines=[m['baseline'] for m in render['measurements']], BusyUiBlockMs=45),
    WhiteBarSampling=white,
    ListPresentation=dict(Report='test-output/ux0915-final11-solo2-ux0912-render/render-report.json',
        Rule='首次物理显现后所有采样保持2以内；首显延迟、实际完成和旧105ms原始差值均单列',
        EarlierTimingFailures=['test-output/ux0915-final9-solo2-ux0912-render/render-cycle-0.json', 'test-output/ux0915-final10-solo2-ux0912-render/render-cycle-0.json']),
    BallRealMotion=dict(Report='test-output/ux0915-final11-solo2-animation/animation-pixels.json', OpeningMonotonic=True, ClosingMonotonic=True, FixedCanvasWidth=370, SnapshotHandoffs=0),
    AdditionalSharedProbe=shared_probe,
    MouseMoved=desktop is not None, CursorRestored=bool(desktop and desktop['cursorRestored']), ProductionDataChanged=False, StartupConfigurationChanged=False, UserProcessesStopped=False,
    CodebaseMemory=dict(Project='Instant-taskboard', Connected=True, Persisted=True, Details='docs/INDEX-STATUS.md', Freshness='metadata_changed / XAML not_tracked; direct source and package hashes supplement'),
    Boundaries=[
        '新建/编辑/设置使用已确认的UI线程真实裁切；忙碌可延迟首帧/丢帧，白条短暂高亮不保证每轮采到。逐轮采样及漏采数保留，未承诺120FPS。',
        '最初旧检查假设首次异步返回早于40ms、每轮捕获白条峰值，及用×寻找已改名取消按钮，已按现有规则修订；40ms门槛和实际反向仍检查。早期失败目录保留。',
        '早期屏幕测试读到Smoke模式移至屏幕外的旧位置；最终测试在Show后移回仅测试宿主，按实际坐标与暗色基线验证。旧误取点记录不计通过。',
        '最终真实鼠标已通过，证据来自同一便携包。' if desktop is not None else '最终真实鼠标待当前协调回复；早期不同运行文件的鼠标结果不计本轮最终通过，未据此声称真实点击已验收。',
        '混合DPI/热插拔、所有显卡负载及用户环境偶发情况仍需复核；左侧吸附T184待实现，其他外部接入原边界保持。',
        '额外共享抽牌探测按实际Passed和错误记录单列，不计入本轮必需项数量；此前新旧版均曾50%透明度前置色差27，不擅改透明度或提高阈值。',
        '列表旧105ms后判定会将DWM晚于原生完成的首显判为闪烁；保留旧原始差值，新增首次物理显现/实际完成记录，检查各区域首次显现后有无回退，未承诺全部首显100ms内完成。球另保留阻塞55ms原始帧，正常展开和收起独立要求中间帧。',
        '普通本地修改未上传GitHub，不操作正式任务、自启、保留数据或用户进程。'
    ]
)
write('docs/VERIFICATION-0.9.15.json', json.dumps(verification, ensure_ascii=False, indent=2))
write('docs/ACCEPTANCE-0.9.15.md', f'''# 0.9.15本地验收

PRD39 U165至U169/A183至A187、TASKS27 T194至T200。本地入口release/即时任务看板-0.9.15-final/即时任务看板.exe；完整ZIP、SHA256及源码来源见BUILD-INFO-0.9.15-local。未上传GitHub。

本轮最终运行包共{total}项检查通过，包含41项新需求检查、24项重复展开实屏检查、171规则及关联布局/开合/保存/关闭回归；球双向实屏与响应预算另通过。{'最终真实鼠标26项已通过。' if desktop is not None else '最终真实鼠标待当前协调回复，未计入通过数量；此前不同运行文件的结果不冒充最终包验收。'}额外抽牌探测是否通过及实际错误见VERIFICATION的AdditionalSharedProbe，未擅改透明度规则或提高阈值。已完成控件与实屏检查使用同一最终便携包，规则程序加载发布Core，并核对哈希。

保存文字#102033；会话按实际继承字体6至10个M宽、靠右在图标左；阶段气泡边框和文字均受剩余空间约束，短名称自然宽；300/430/350三种列宽、短中长会话及实际循环偏移已测。首停0.5秒、24DIP/秒单向前移、末停0.3秒、复位循环。解释并删除Codex状态行，正文及关联名称/数据保持，Phase变化不再重建卡片。

按用户确认，悬浮开合全程真实控件，最后草稿收起后才释放控件，快速重开取消旧清理；首帧准备后100ms目标裁切；中央单白芯与泛光同进度，结束没有截图/控件交接。Root外非命中显示层及原UIA根不改变输入范围，球向左开合也取消截图交接：370内部真实画布固定，原生外部仍58至370且父轮廓裁切，右锚子窗口同步；菜单、红点及触发范围保持。草稿抽牌仍合成。8轮新建、2轮编辑、2轮设置展开结束真实背景采样最大亮度差{verification['RepeatedOpenSampling']['MaxLateDifference']}；含45ms界面阻塞。另保留第一外点、第二外点到保存/关闭、关联区展开、快速反向、整组恢复、实际保存/暂存与球动效采样。

忙碌真实UI可能延迟首帧并跳过短白条峰值，这是用户已接受的取舍，不宣称所有轮次均捕获峰值。三类窗口的整组开/关/快速重开逐轮采样及漏采数记录在VERIFICATION；普通首次/重复开合的单白条、中间帧及上下贯穿仍有实屏证据。旧独立合成时序假设及旧×按钮选择器已修订，失败记录保留，不以反复运行掩盖边界。早期编辑/设置测试受Smoke离屏放置影响，旧位置采样不计通过；最终Show后移回该测试宿主，并要求实际暗色窗口基线。

列表六列三轮采样区分首次物理显现延迟与显现后回退；DWM可能晚于原生动画完成呈现，旧105ms断言失败目录保留。最终每个区域首次显现后偏差须在2以内，首显时间、实际完成及旧原始差值全部记录，不将延迟显示宣称为100ms内完成。球的55ms阻塞探测保留原始帧并检查无回退，正常展开/收起分别检查中间帧；阻塞时可跳过中间帧。

{'真实鼠标覆盖六列空白菜单、标题/整组及单列缩放、文字外新建点击、卡片、修改后外部双击保存关闭、球矩形左右键/拖动及菜单位置；完成恢复鼠标和前台。' if desktop is not None else '最终真实鼠标尚未执行，需协调用户操作后覆盖菜单、拖动缩放、空白区新建、外部双击及球矩形；不能用内部事件检查冒充实际鼠标验收。'}列表alpha1、22×22整组范围、球矩形内部命中、悬浮滑块1.34倍和90ms快速滚轮等既定操作的隔离检查通过。正式数据、自启和用户进程未操作。

Codebase Memory已连接持久化刷新，C# metadata_changed和XAML not_tracked以直接源码/最终哈希补证；多屏混合DPI、热插拔、所有显卡负载未覆盖，不能声称用户环境偶发闪烁已全部复现。左侧吸附T184仍待后续。
''')

previous = json.loads(read('docs/BUILD-INFO-0.9.14-local.json'))
scope = {entry['Path'] for entry in previous['Sources']}
scope.update([
    'src/Taskboard.Desktop/CardHeading.cs', 'src/Taskboard.Desktop/SurfaceWindow.LiveScroll.cs', 'src/Taskboard.Desktop/SurfaceWindow.LiveBall.cs',
    'src/Taskboard.Desktop/Ux0915Checks.cs', 'src/Taskboard.Desktop/Ux0915RenderChecks.cs',
    'tools/Write-UX0915-Delivery.py', 'docs/VERIFICATION-0.9.15.json', 'docs/ACCEPTANCE-0.9.15.md',
])
for path in scope:
    if not (ROOT / path).is_file():
        raise RuntimeError('来源路径不存在：' + path)
write('test-output/ux0915-source-scope.json', json.dumps(sorted(scope), ensure_ascii=False, indent=2))
print(json.dumps(dict(Passed=total, SourcePaths=len(scope), SameFinalPortable=True)))
