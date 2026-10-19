import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = 'release/即时任务看板-0.9.13-final'

def read(path):
    return (ROOT / path).read_text(encoding='utf-8-sig')

def write(path, value):
    (ROOT / path).write_text(value.rstrip() + '\n', encoding='utf-8')

def sha(path):
    return hashlib.sha256((ROOT / path).read_bytes()).hexdigest().upper()

reports = []
for kind, filename, expected in [
    ('core', 'report.json', 171), ('ux0913', 'ux0913-report.json', 31),
    ('ux0913-render', 'ux0913-render-report.json', 16), ('ux0912-render', 'render-report.json', 54),
    ('ux099', 'ux099-report.json', 30), ('ux0911', 'ux0911-report.json', 37),
    ('ux0912', 'ux0912-report.json', 23), ('ux0910', 'ux0910-report.json', 15),
    ('stage', 'stage-report.json', 31), ('integration', 'integration-report.json', 17),
]:
    path = f'test-output/ux0913-final-{kind}/{filename}'
    result = json.loads(read(path))
    if result['passed'] != expected:
        raise RuntimeError('检查数量不符：' + path)
    folder = (ROOT / path).parent
    if any((folder / name).exists() for name in ['startup-error.txt', 'errors.log']):
        raise RuntimeError('存在错误回执：' + path)
    reports.append(dict(Kind=kind, Report=path, Passed=expected, SHA256=sha(path)))

performance_path = 'test-output/ux0913-final-performance/performance-report.json'
performance = json.loads(read(performance_path))
if performance['passed'] is not True:
    raise RuntimeError('响应预算未通过')
if sha(RUNTIME + '/Taskboard.Core.dll') != sha('test-output/ux0913-core-runner/Taskboard.Core.dll'):
    raise RuntimeError('规则测试未使用最终发布Core')
render = json.loads(read('test-output/ux0913-final-ux0913-render/ux0913-render-report.json'))
columns = json.loads(read('test-output/ux0913-final-ux0912-render/render-report.json'))
verification = dict(
    Version='0.9.13', Date='2026-10-19', LocalOnly=True, Entry=RUNTIME + '/即时任务看板.exe',
    Passed=sum(item['Passed'] for item in reports), Reports=reports,
    Performance=dict(Report=performance_path, SHA256=sha(performance_path), Passed=True),
    CoreTestUsesPublishedCore=True,
    RuntimeHashes=[dict(Path=name, SHA256=sha(RUNTIME + '/' + name)) for name in ['Taskboard.dll', 'Taskboard.Core.dll', '即时任务看板.exe']],
    NativeHitTests=dict(NewTaskHorizontalFractions=[.1, .25, .5, .75, .9], DockedBallRightRectangle=True),
    PixelSampling=dict(Popups=render['measurements'], ColumnCycles=3, Columns=6,
                       MaxColumnLateDifference=max(max(cycle['difference']) for cycle in columns['cycles'])),
    MouseMoved=False, KeyboardInjected=False, ProductionDataChanged=False, StartupConfigurationChanged=False,
    UserProcessesStopped=False, ExistingExplicitOffSettingPreserved=True,
    CodebaseMemory=dict(Project='Instant-taskboard', Connected=True, Persisted=True, Details='docs/INDEX-STATUS.md', Freshness='metadata_changed; explicit source and SHA256 supplement'),
    Boundaries=[
        '真实屏幕采样仅测试窗口区域；未移动鼠标或注入键盘。原生命中通过WindowFromPoint核对，业务点击通过相同入口和路由事件验证。',
        '未稳定复现用户第一次真实外点的全部刷新闪烁；加入共用激活重绘保护及移除焦点驱动的折叠后，编辑、新建、设置的模拟第一外点与激活消息采样无背景亮度跳变，仍需真实鼠标双击复核。',
        '本轮只做右侧吸附，左侧形状/命中与左右切换T184待实现。新配置默认开启，已有保存false保持。',
        '未实测多屏混合DPI、热插拔、所有显卡或繁忙负载；列表真实裁切依赖UI线程，不保证120FPS。',
        'Codex、飞书和通知替换原接入边界保持。未改正式数据、自启或用户进程；未上传GitHub。'
    ]
)
write('docs/VERIFICATION-0.9.13.json', json.dumps(verification, ensure_ascii=False, indent=2))
write('docs/ACCEPTANCE-0.9.13.md', '''# 0.9.13本地验收

2026-10-19。PRD37 U149至U156/A167至A174、TASKS25 T176至T183。入口release/即时任务看板-0.9.13-final/即时任务看板.exe，ZIP/SHA256和显式来源见BUILD-INFO-0.9.13-local。

425项隔离检查及响应预算通过：本轮交互31、屏幕/原生命中16、三轮六列像素54、规则171、既有交互/藏球/菜单/提示/阶段/迁移30/37/23/15/31/17。规则程序加载最终发布Core，运行文件哈希及逐项报告见VERIFICATION-0.9.13.json。

会话右对齐6至10字符、未绑定与单阶段保留；新建虚线框横向10%、25%、50%、75%、90%点的WindowFromPoint实际命中内容子窗口，空白不穿透。已关联名称作为搜索预显示，选定立即收起；箭头可展开/收起，阶段内只收下拉、阶段外收整块。实际原生提示框位置固定在工作区水平中央和62%高度，重新定位亦保持。

右侧贴边形状、填充/描边/毛玻璃、右上矩形原生点击范围通过，截图已查看；只可上下拖动、关闭恢复自由圆球。收起/弹出均100ms且有中间位置，最终1/4原生条带、小红点上移10、同待操作边框色、弹出时渐隐、后台提醒转入待操作、可见坐标持久化与开关/菜单中止回归通过。新配置默认开启，已有保存的关闭配置保留。左侧吸附、左右切换明确列入T184待实现。

第一次真实外部双击闪烁未在本轮模拟路径稳定复现，不能声称所有原因均已查明。共用窗口拦截系统非客户区激活重绘，移除焦点引起的关联区折叠；编辑、新建和设置三类窗口模拟第一外点及激活变化后，没有隐藏、重播或额外交接，采样背景亮度稳定。真实鼠标双击仍需用户环境复核。

均使用隔离数据，没有移动鼠标或注入按键；只显示并采样测试窗口。混合DPI多屏、热插拔及全部负载保留边界；不保证所有硬件120FPS。正式任务、自启、用户进程和旧包未操作，普通本地修改未上传GitHub。Codebase Memory已刷新持久化，metadata_changed限制按源码及包哈希补证，见INDEX-STATUS。
''')
previous = json.loads(read('docs/BUILD-INFO-0.9.12-local.json'))
scope = {item['Path'] for item in previous['Sources']}
scope.update([
    'src/Taskboard.Desktop/HintScreenPin.cs', 'src/Taskboard.Desktop/Ux0913Checks.cs',
    'src/Taskboard.Desktop/Ux0913RenderChecks.cs', 'src/Taskboard.Desktop/BallSurface.EdgeHide.cs',
    'src/Taskboard.Desktop/StageConversationPicker.cs', 'tools/Write-UX0913-Delivery.py',
    'docs/VERIFICATION-0.9.13.json', 'docs/ACCEPTANCE-0.9.13.md'
])
write('test-output/ux0913-source-scope.json', json.dumps(sorted(scope), ensure_ascii=False, indent=2))
print(json.dumps(dict(Version='0.9.13', Passed=verification['Passed'], Sources=len(scope)), ensure_ascii=False))
