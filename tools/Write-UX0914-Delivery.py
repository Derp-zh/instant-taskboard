import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = 'release/即时任务看板-0.9.14-final'

def read(path): return (ROOT / path).read_text(encoding='utf-8-sig')
def write(path, value): (ROOT / path).write_text(value.rstrip() + '\n', encoding='utf-8')
def sha(path): return hashlib.sha256((ROOT / path).read_bytes()).hexdigest().upper()

reports = []
for kind, filename, expected in [
    ('ux099', 'ux099-report.json', 30), ('ux0914', 'ux0914-report.json', 30),
    ('ux0913', 'ux0913-report.json', 31), ('ux0911', 'ux0911-report.json', 37),
    ('ux0912', 'ux0912-report.json', 23), ('ux0910', 'ux0910-report.json', 15),
    ('stage', 'stage-report.json', 31), ('integration', 'integration-report.json', 17),
    ('ux0913-render', 'ux0913-render-report.json', 16),
    ('ux0913-render', 'ux0914-closing-report.json', 6), ('ux0912-render', 'render-report.json', 54),
]:
    path = f'test-output/ux0914-final2-{kind}/{filename}'
    result = json.loads(read(path))
    if result['passed'] != expected or any((ROOT / path).parent.joinpath(name).exists() for name in ['startup-error.txt', 'errors.log']):
        raise RuntimeError('最终检查不完整：' + path)
    reports.append(dict(Kind=kind, Report=path, Passed=expected, SHA256=sha(path)))
core_path = 'test-output/ux0914-final-core/report.json'
if json.loads(read(core_path))['passed'] != 171 or sha(RUNTIME + '/Taskboard.Core.dll') != sha('test-output/ux0914-core-runner/Taskboard.Core.dll'):
    raise RuntimeError('最终发布Core与规则检查不一致')
reports.append(dict(Kind='core', Report=core_path, Passed=171, SHA256=sha(core_path)))
performance_path = 'test-output/ux0914-final2-performance/performance-report.json'
if json.loads(read(performance_path))['passed'] is not True:
    raise RuntimeError('响应预算未通过')

desktop_path = 'test-output/ux0914-desktop-dev2/ux0914-desktop-report.json'
desktop = json.loads(read(desktop_path))
if desktop['passed'] != 26 or not desktop['mouseMoved'] or not desktop['cursorRestored']:
    raise RuntimeError('真实鼠标检查未完成')
desktop_source_path = 'test-output/ux0914-desktop-tested-source.json'
desktop_source = json.loads(read(desktop_source_path))
critical_names = {'BoardController.cs', 'BoardController.OutsideClicks.cs', 'BallSurface.cs', 'BallSurface.EdgeHide.cs', 'ColumnSurface.cs', 'ColumnSurface.Inline.cs', 'EditorSurface.cs', 'SurfaceWindow.cs', 'Ui.cs', 'LiveBackdrop.cs'}
critical = [x for x in desktop_source['Sources'] if Path(x['Path']).name in critical_names]
if len(critical) != 10 or any(sha(x['Path']) != x['SHA256'] for x in critical):
    raise RuntimeError('真实鼠标所覆盖的命中、保存、菜单源码发生变化，必须重新验证')
closing = json.loads(read('test-output/ux0914-final2-ux0913-render/ux0914-closing-report.json'))
columns = json.loads(read('test-output/ux0914-final2-ux0912-render/render-report.json'))
current = json.loads(read('test-output/ux0914-final2-ux0914/ux0914-report.json'))
verification = dict(
    Version='0.9.14', Date='2026-10-19', LocalOnly=True, Entry=RUNTIME + '/即时任务看板.exe',
    Passed=sum(x['Passed'] for x in reports) + 26, FinalPortablePassed=sum(x['Passed'] for x in reports), DesktopPassed=26, Reports=reports,
    DesktopVerification=dict(Report=desktop_path, SHA256=sha(desktop_path), TestedBuildVersion=desktop_source['BuildVersion'],
        TestedAssemblySHA256=desktop_source['AssemblySHA256'], SourceReceipt=desktop_source_path, SourceReceiptSHA256=sha(desktop_source_path),
        CriticalSourcesUnchanged=critical, SameBinaryAsPortable=False,
        Note='真实鼠标使用同功能开发构建，最终包另做461项隔离及实屏检查；命中、菜单、保存与模板10条源码相同。之后滚轮反向修订及检查程序调整不冒充已做真实鼠标滚轮验收。'),
    Performance=dict(Report=performance_path, SHA256=sha(performance_path), Passed=True),
    CoreTestUsesPublishedCore=True,
    RuntimeHashes=[dict(Path=name, SHA256=sha(RUNTIME + '/' + name)) for name in ['Taskboard.dll', 'Taskboard.Core.dll', '即时任务看板.exe']],
    ClosingPixelSampling=dict(Variants=['普通编辑修改后关闭', '关联区域展开后外部双击关闭'], MaxBeforeClosingDifference=max(x['max'] for x in closing['measurements']), IndependentSampler=True, Report='test-output/ux0914-final2-ux0913-render/ux0914-closing-report.json'),
    Scroll=dict(TargetMs=90, MeasuredIsolatedMs=current['scrollMs'], FloatingTrackWidth=8.04, FloatingVisibleThumbWidth=5.36, ColumnTrackWidth=6, ColumnVisibleThumbWidth=4,
        Note='隔离滚轮事件、实际中间偏移/反向/打断及虚拟化检查；未声称真实鼠标滚轮专项已完成'),
    ColumnPixelSampling=dict(Cycles=3, Columns=6, MaxLateDifference=max(max(x['difference']) for x in columns['cycles'])),
    MouseMoved=True, CursorRestored=True, KeyboardInjected=False, ProductionDataChanged=False, StartupConfigurationChanged=False, UserProcessesStopped=False,
    CodebaseMemory=dict(Project='Instant-taskboard', Connected=True, Persisted=True, Details='docs/INDEX-STATUS.md', Freshness='C# metadata_changed; XAML not_tracked; direct source and hashes supplement'),
    Boundaries=[
        '用户本轮明确同意后才移动鼠标；仅隔离进程窗口，目标属于其他进程即停止输入，完成恢复原鼠标和前台。正式任务、自启和用户进程未操作。',
        '实际26项覆盖六列空白菜单、标题/两种缩放、新建空白点击、修改后双击保存关闭、卡片、球矩形左右键/拖动和菜单位置。滚轮衔接使用隔离路由事件与实际偏移测量，不冒充真实鼠标滚轮检查。',
        '关闭前实屏采样覆盖第二外点后的保存等待至关闭开始；既定中央白条和裁切保留。不能保证所有显卡及负载无帧问题，不宣称完整复现用户所有历史闪烁。',
        '列表真实裁切和滚轮帧推进依赖UI线程，不保证120FPS；混合DPI多屏、热插拔、所有负载和外部接入原边界保持。',
        '左侧吸附T184仍待实现；本轮普通修改未上传GitHub。'
    ]
)
assert verification['Passed'] == 487 and verification['FinalPortablePassed'] == 461
write('docs/VERIFICATION-0.9.14.json', json.dumps(verification, ensure_ascii=False, indent=2))
write('docs/ACCEPTANCE-0.9.14.md', '''# 0.9.14本地验收

2026-10-19。PRD38 U157至U164/A175至A182、TASKS26 T185至T193，左侧T184待实现。入口release/即时任务看板-0.9.14-final/即时任务看板.exe，完整ZIP/SHA256与来源见BUILD-INFO-0.9.14-local。

本轮合计487项：最终便携包461项（本轮30、关闭前实屏6、原有交互/阶段/后台/迁移和显示回归、171规则），另26项已协调的真实鼠标交互；响应预算另通过。实际鼠标使用本轮同功能开发构建，最终便携包另做隔离回归及屏幕采样；10条命中、菜单、保存和模板关键源码SHA256与真实测试时完全相同，两个二进制的哈希分别记录，未冒充同一最终包的真实鼠标检查。

真实鼠标已检查六列空白处右键菜单、标题空白拖动、灵感库22×22最大范围缩放角部、开启后的单列右下缩放、新建按钮文字外点击、卡片打开、修改后第一次外点保持/第二次保存关闭、球新增矩形区域左右键及右键长按上下拖动、菜单在球旁留间距。完成恢复鼠标位置及前台，未注入键盘。最初测试因隔离配置默认关闭单列缩放而停下，开启后完整26项通过；不把初次中止记为通过。

列表Root的alpha0空白穿透是共同输入问题，恢复alpha1并保持常驻合成层；球内部Border右角归零，修复系统命中正常而内部按钮仍按旧圆角的问题。触发范围不能只凭WindowFromPoint验证，这次补充内部命中和实际动作。

双击保存原先全区IsEnabled=false触发主题按钮0.45、输入框0.5透明度，形成关闭前变暗。改为输入拦截并保留视觉，重复提交锁、键盘/输入法/粘贴保护保持。真实双击在受控保存等待时按钮和输入框维持正常亮度；最终包独立线程采样普通编辑及曾展开会话区域的修改后关闭，第二击到关闭开始前亮度无跳变。原关闭中央白条与裁切保持；未声称已复现并消除所有硬件和负载下的历史闪烁。

保存文字#FFB347；悬浮竖向轨道6→8.04、左右内缩1→1.34，可见滑块4→5.36，均1.34倍，列表6/1不变；水平条保持。暂存快照继承并处理自动尺寸。快速90ms滚轮有实际中间偏移、连续/首帧前反向抵消、直接操作取消、列表回收虚拟化及拖动边缘逐卡推进；本轮隔离实测时长见VERIFICATION。滚轮使用隔离事件和偏移检查，未冒充真实鼠标滚轮专项。

本轮461项检查均来自最终便携包，规则程序加载最终发布Core，三轮六列实屏采样和三类悬浮窗口第一外点回归通过。旧会话滚动检查原在固定延时后读旧引用，现针对当前可见控件检查重新展开时的偏移归零，保留原停顿和实际滚动检查；之前失败回执保留，不用重跑掩盖假设差异。

AGENTS.md已写入触发范围保护、影响清单、双层命中与实际动作、打开/第二外点/保存/关闭各阶段回归，以及必要连带改变先讨论规则。正式数据、自启、用户进程与旧包未改，多屏混合DPI、热插拔及全部负载留边界。Codebase Memory已刷新，metadata_changed/XAML not_tracked按直接源码和SHA256补证。普通本地修改未上传GitHub。
''')
previous = json.loads(read('docs/BUILD-INFO-0.9.13-local.json'))
scope = {x['Path'] for x in previous['Sources']}
scope.update(['AGENTS.md', 'README.md', 'docs/HANDOFF.md', 'docs/INDEX.md', 'docs/INDEX-STATUS.md', 'docs/PRD.md', 'docs/TASKS.md', 'docs/IMPLEMENTATION.md', 'docs/USER-GUIDE.md',
    'src/Taskboard.Desktop/QuickScroll.cs', 'src/Taskboard.Desktop/Ux0914Checks.cs', 'src/Taskboard.Desktop/Ux0914DesktopChecks.cs', 'src/Taskboard.Desktop/Ux0914ClosingChecks.cs',
    'src/Taskboard.Desktop/Theme.xaml', 'tools/Write-UX0914-Delivery.py', 'docs/VERIFICATION-0.9.14.json', 'docs/ACCEPTANCE-0.9.14.md'])
write('test-output/ux0914-source-scope.json', json.dumps(sorted(scope), ensure_ascii=False, indent=2))
print(json.dumps(dict(Version='0.9.14', Passed=verification['Passed'], FinalPortable=verification['FinalPortablePassed'], RealMouse=26, Sources=len(scope)), ensure_ascii=False))

