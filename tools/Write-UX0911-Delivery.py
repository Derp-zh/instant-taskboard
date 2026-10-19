"""Write the local 0.9.11 delivery only after checking final-package reports."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def write(path, content):
    (ROOT / path).write_text(content.rstrip() + "\n", encoding="utf-8")


def sha(path):
    return hashlib.sha256((ROOT / path).read_bytes()).hexdigest().upper()


def prepend(path, title, section):
    current = (ROOT / path).read_text(encoding="utf-8-sig")
    boundary = current.index("\n", current.index("# "))
    write(path, title + "\n\n" + section.strip() + "\n\n" + current[boundary:].lstrip())


reports = []
for kind, filename, expected in [
    ("core", "report.json", 171), ("ux0911", "ux0911-report.json", 37),
    ("ux099", "ux099-report.json", 30), ("ux0910", "ux0910-report.json", 15),
    ("stage", "stage-report.json", 31), ("integration", "integration-report.json", 17),
]:
    path = f"test-output/ux0911-final-{kind}/{filename}"
    data = json.loads((ROOT / path).read_text(encoding="utf-8-sig"))
    if data["passed"] != expected:
        raise ValueError(f"检查不完整：{path}")
    for error in ["startup-error.txt", "errors.log"]:
        if (ROOT / path).parent.joinpath(error).exists():
            raise ValueError(f"最终检查含异常：{path}/{error}")
    reports.append(dict(Kind=kind, Report=path, Passed=expected, SHA256=sha(path)))

runtime = "release/即时任务看板-0.9.11-final"
if sha(runtime + "/Taskboard.Core.dll") != sha("test-output/ux0911-exact-core-runner/Taskboard.Core.dll"):
    raise ValueError("规则检查未使用最终发布Core")
layout = json.loads((ROOT / "test-output/ux0911-final-ux0911/ux0911-report.json").read_text(encoding="utf-8"))
verification = dict(
    Version="0.9.11", Date="2026-10-18", LocalOnly=True,
    Entry=runtime + "/即时任务看板.exe", Passed=sum(x["Passed"] for x in reports), Reports=reports,
    CoreTestUsesPublishedCore=True,
    RuntimeHashes=[dict(Path=name, SHA256=sha(runtime + "/" + name)) for name in ["Taskboard.dll", "Taskboard.Core.dll", "即时任务看板.exe"]],
    EdgeAutoHide=dict(Default=False, IdleSeconds=4, ObservedDelayMs=layout["autoHideDelayMs"], ExposedWidthFraction=.25,
                     Restore="鼠标移入露出部分恢复完整球；单击打开面板", BackgroundReminderVerified=True, NativeClippingVerified=True),
    ButtonSize=dict(CancelWidth=layout["cancelWidth"], SaveWidth=layout["saveWidth"], Height=40),
    Screenshots=[f"test-output/ux0911-final-ux0911/{name}.png" for name in ["edit-buttons", "single-stage", "edge-left", "edge-right"]],
    MouseMoved=False, KeyboardInjected=False, ProductionDataChanged=False,
    StartupConfigurationChanged=False, UserProcessesStopped=False,
    CodebaseMemory=dict(Connected=True, Project="Instant-taskboard", Root="D:/Derp/Documents/0renwumianban",
                        FullPersistentRefreshSucceeded=True, ExactPathsWithNoRecordedIssue=7,
                        FreshnessSignal="metadata_changed even after refresh; direct source and package source hashes used as supplement",
                        Details="docs/INDEX-STATUS.md"),
    Boundaries=["测试窗口均在屏幕外；鼠标移入用路由事件验证，无真实鼠标或键盘输入。",
                "原生裁形与150%DPI取整已检查，未做多屏混合DPI、拔插显示器或真实桌面悬停实机验收。",
                "后台提醒通过实际CheckReminders路径验证，Codex现有接入边界保持，未声称新接通外部AI。",
                "普通本地修改未上传GitHub，上一公开0.9.5；正式数据、自启及旧包保留。"],
)
write("docs/VERIFICATION-0.9.11.json", json.dumps(verification, ensure_ascii=False, indent=2))

prd = (ROOT / "docs/PRD.md").read_text(encoding="utf-8-sig")
prd = prd[prd.index("## 1 "):]
write("docs/PRD.md", "# 即时任务看板电脑端PRD\n\n版本：0.9.11。更新日期：2026-10-18。第35节为本轮按钮、悬浮球边缘隐藏和单阶段卡片修订；冲突时本节优先，其余已确认规则保持。\n\n" + prd.rstrip() + "\n\n" + """
## 35 2026-10-18 按钮尺寸与悬浮球边缘隐藏（0.9.11）

用户指定版本0.9.11，补充确认收起且鼠标离开4秒后隐藏、鼠标移上恢复完整球再单击展开。普通本地修改不上传GitHub。

| 编号 / 验收 | 已确认规则 |
| --- | --- |
| U139 / A157 | 编辑任务右上角“取消保存”与右下角“保存任务”宽高一致，仅调整尺寸，保留文字、外观和放弃编辑含义。新建右上X保持 |
| U140 / A158 | 主设置增加悬浮球边缘自动隐藏开关，默认关闭；旧配置缺字段也关闭，保存、重启和配置备份保留。开启后整组面板收起、鼠标离开球4秒，向当前屏幕较近的左或右边缘隐藏，桌面仅露出球宽的1/4；上下位置保持在屏幕内。展开、右键拖动、菜单或预览使用期间不隐藏 |
| U141 / A159 | 鼠标移入露出部分即恢复完整球（位于该侧边缘内），不自行打开面板；单击按原规则展开。隐藏时有待操作任务则在露出部分显示小红点，不显示原数量气泡；无待操作则无红点，完整显示时沿用原样。关闭开关立即恢复完整球。隐藏仅改变球位置及裁形，不停止提醒、监听或后台自动转入待操作等操作；保存球位置不写边缘外坐标，手动隐藏、托盘恢复、默认布局保持可用 |
| U142 / A160 | 多列表独立窗口中的单阶段任务卡片不显示阶段状态图标，原会话关联文本保留。多阶段图标和编辑窗口阶段操作保持；灵感库既有会话名称显示例外不扩展 |
""")

prepend("docs/TASKS.md", "# 电脑端任务面板工作清单", """
## 23 0.9.11悬浮球边缘隐藏与卡片（2026-10-18）

| 任务 | 工作 | 验收 |
| --- | --- | --- |
| T165 | 编辑取消保存按钮跟随保存任务实际宽高，原操作保留 | A157 |
| T166 | 默认关闭的主设置开关，4秒闲置、左右边缘1/4及移入恢复 | A158、A159 |
| T167 | 隐藏红点、原生边界裁切、安全位置、后台提醒及配置保存 | A159 |
| T168 | 单阶段列表隐藏状态图标、保留关联文本及多阶段操作 | A160 |
| T169 | 最终包301项隔离检查、便携ZIP/SHA256、文档和指定名称索引 | A157至A160 |

以上完成。新功能37项、规则171项、界面回归30/15项、阶段31项、迁移17项，共301项；窗口屏幕外，无鼠标键盘注入。实际桌面悬停和多屏混合DPI保留验收边界，普通本地修改未上传。
""")

prepend("docs/IMPLEMENTATION.md", "# 新版电脑端实施记录", """
## 0.9.11边缘隐藏与单阶段卡片（2026-10-18）

对应PRD35 U139至U142/A157至A160、TASKS23 T165至T169。取消保存按钮宽度绑定保存任务的实际宽度，二者高40；当前实际宽均74。仅尺寸改变，新建X和放弃工作副本路径保持。

Preferences增加可选BallEdgeAutoHide，默认false，旧文件无需迁移；主设置开关即时生效并沿用配置保存/备份。BallSurface.EdgeHide使用100ms DispatcherTimer观测鼠标及窗口使用状态；整组收起、鼠标离开4秒后移到当前屏幕较近的左/右边缘，只露出宽1/4。鼠标移入恢复该侧边缘内完整球，不打开面板；展开、右键拖动、右键菜单及待操作预览期间推迟隐藏。隐藏用独立6单位红点替代数量气泡，后台数据刷新不重置闲置计时、不唤出球。

Native.ClipWindow对藏球设置矩形区域，仅保留露出条带，避免隐藏部分显示在邻屏；还原时撤掉区域，圆形合成继续沿用。按设备DPI取整，本机150%下半像素坐标的误差不超过一物理像素。保存球位置使用完整球的边缘内坐标；关闭开关、手动隐藏或恢复布局清理临时状态，拖动和托盘入口保持。其余窗口裁形不变，后台提醒/监听计时器不随隐藏停止。

TaskCard.Heading仅在任务实际有多阶段时添加阶段状态按钮，保留单阶段会话名与下方关联状态文本；编辑器阶段控件不改。灵感库既有名称显示规则保持。

最终便携包执行37项本轮、30项0.9.9回归、15项0.9.10回归、31项阶段、17项迁移；规则程序装载最终Core DLL执行171项，共301项。检查真实4秒DispatcherTimer、左右原生窗口区域、鼠标移入路由事件、开关写盘/备份、关闭开关/菜单/布局恢复，以及藏球期间实际CheckReminders转待操作并刷新红点。全部任务窗口屏幕外、无鼠标或键盘注入，截图已查看；真实桌面悬停和多屏混合DPI/热插拔未实测，不冒充已通过。正式任务、自启和用户进程未操作；仅清理本轮提前启动的隔离检查进程。

Codebase Memory实际已连接：Instant-taskboard初查不存在，核对旧taskboard正确根目录及ready后，architecture/search_graph/trace_path/snippet定位，精确覆盖提示metadata_changed，因此按用户指定名称full持久化重建；初次5706节点/16204边，8个PowerShell部分解析、0不可用。最终刷新及覆盖结果见INDEX-STATUS。未遍历所有源码、历史聊天或保留数据。便携ZIP逐文件核验及源码/运行哈希见BUILD-INFO-0.9.11-local；普通修改未上传GitHub。
""")

handoff = (ROOT / "docs/HANDOFF.md").read_text(encoding="utf-8-sig")
handoff = handoff[handoff.index("## 阅读顺序"):]
handoff = handoff.replace("项目名：`taskboard`", "项目名：`Instant-taskboard`（原`taskboard`）").replace("名称 `taskboard`", "名称 `Instant-taskboard`")
handoff = handoff.replace("[0.9.10验收](ACCEPTANCE-0.9.10.md)、[验证摘要](VERIFICATION-0.9.10.json)和[本地包清单](BUILD-INFO-0.9.10-local.json)", "[0.9.11验收](ACCEPTANCE-0.9.11.md)、[验证摘要](VERIFICATION-0.9.11.json)和[本地包清单](BUILD-INFO-0.9.11-local.json)")
handoff = handoff.replace("- 0.9.10接手曾重连并full持久化刷新；交付复查再次Transport closed。最终图新鲜度未确认，见INDEX-STATUS，按指定源码补证。", "- 0.9.11实际恢复连接并按用户指定名称full持久化重建；最终刷新状态及精确覆盖见INDEX-STATUS。历史断连记录不代表当前连接。")
write("docs/HANDOFF.md", """# 新会话接手说明

更新时间：2026-10-18。最新用户指令及PRD35优先。

## 当前状态

- 本地0.9.11，入口`release/即时任务看板-0.9.11-final/即时任务看板.exe`；完整ZIP/SHA256及来源哈希见BUILD-INFO-0.9.11-local。PRD35 U139至U142/A157至A160、TASKS23 T165至T169；上一公开0.9.5，普通修改未上传，旧包保留。
- 编辑“取消保存”与保存任务实际等宽等高，当前74×40，仅尺寸改变。单阶段列表卡片隐藏阶段状态图标，关联文本保留；多阶段与编辑器操作保持。
- 主设置“悬浮球在桌面边缘自动隐藏”默认关闭。用户确认整组收起且鼠标离开4秒后隐藏到较近的左/右边缘，仅露1/4宽；移入恢复边缘内完整球，单击展开。藏球有待操作时显示露出部分红点，完整球数量显示保持，后台提醒和自动流转继续。
- 最终301项隔离检查通过：171规则、37本轮、30/15界面回归、31阶段、17迁移；规则使用最终包Core DLL。真实4秒计时及原生区域、后台提醒路径通过；窗口均屏幕外，移入用路由事件，无鼠标或键盘注入。实际桌面悬停、多屏混合DPI/热插拔未实测。
- Codebase Memory已连接，用户指定名Instant-taskboard初次不存在，核对旧taskboard根目录与覆盖后按指定名full持久化重建。最终刷新与覆盖回执见INDEX-STATUS，不沿用旧断连结论。
- 0.9.10提示/名称留白、0.9.9其余交互、0.9.8阶段暂留和归档、2/3门槛及1.35倍暂存切换保持。正式数据、自启配置和用户进程未操作，未沿用历史鼠标许可。

""" + handoff)

index = (ROOT / "docs/INDEX.md").read_text(encoding="utf-8-sig")
index = index.replace("项目名：`taskboard`", "项目名：`Instant-taskboard`（原`taskboard`）")
headline = "2026-10-18当前本地0.9.11：取消保存与保存任务等尺寸；默认关闭的悬浮球边缘自动隐藏，收起离开4秒露1/4、移入恢复，隐藏红点及后台运行；单阶段卡片去状态图标、保留关联。入口PRD35 U139至U142/A157至A160、TASKS23 T165至T169、IMPLEMENTATION0.9.11，[验收](ACCEPTANCE-0.9.11.md)、[验证摘要](VERIFICATION-0.9.11.json)、[完整包](BUILD-INFO-0.9.11-local.json)。最终包301项隔离检查通过，窗口屏幕外、无鼠标键盘注入。Codebase Memory实际已连接并使用用户指定名Instant-taskboard，最终回执见INDEX-STATUS。普通修改未上传，上一公开0.9.5。下列旧版本状态均为历史。"
index = index.replace("# 项目文档索引\n", "# 项目文档索引\n\n" + headline + "\n", 1)
write("docs/INDEX.md", index)

readme = (ROOT / "README.md").read_text(encoding="utf-8-sig")
readme = readme[readme.index("Windows桌面任务看板"):]
readme = readme.replace("本轮0.9.10仅本地交付", "本轮0.9.11仅本地交付")
write("README.md", """# 即时任务看板

当前本地0.9.11：编辑取消保存与保存任务等尺寸；主设置新增默认关闭的悬浮球边缘自动隐藏，收起且鼠标离开4秒后露出1/4，移入恢复，隐藏时待操作用小红点提示，后台继续运行；单阶段列表卡片去除状态图标并保留关联文本。

入口`release/即时任务看板-0.9.11-final/即时任务看板.exe`。先从旧版托盘退出，保留完整目录及原数据路径。最终包301项隔离检查通过，无鼠标键盘注入；真实桌面悬停和多屏混合DPI尚未实测。普通修改未上传GitHub，上一公开0.9.5。

见[本轮验收](docs/ACCEPTANCE-0.9.11.md)、[验证摘要](docs/VERIFICATION-0.9.11.json)和[使用说明](docs/USER-GUIDE.md)。

""" + readme)

write("docs/ACCEPTANCE-0.9.11.md", """# 0.9.11本地验收

2026-10-18。PRD35 U139至U142/A157至A160、TASKS23 T165至T169。入口`release/即时任务看板-0.9.11-final/即时任务看板.exe`；完整便携ZIP/SHA256及显式相关来源哈希见BUILD-INFO-0.9.11-local。

编辑取消保存与保存任务实际均74×40，外观、文字及取消保存含义保持，新建X不改。单阶段任务卡片无状态图标，会话名称、正文与下方关联文本保留；多阶段图标和编辑器操作保持。截图已查看。

自动隐藏主设置默认关闭，旧配置无字段默认关闭，开关保存及配置备份恢复通过。实际DispatcherTimer在约4秒后将球移到较近左右边缘，露出1/4；本机150%DPI坐标取整及原生区域宽度通过。左右红点均在露出侧，恢复后原数量气泡保持。关闭开关立即恢复；展开、菜单、拖动与预览使用期间不隐藏，安全位置及恢复默认布局通过。隐藏时实际CheckReminders路径仍将任务转入待操作，并同步点亮红点。

最终包301项：171规则、37本轮、30项0.9.9回归、15项0.9.10回归（无本轮真实提示显示检查）、31阶段、17迁移。所有任务窗口屏幕外；移入恢复使用路由事件，并非真实移动鼠标。原生裁形避免邻屏露出已检查，但多屏混合DPI、拔插显示器、真实悬停和开合观感未实机验收。正式数据、自启配置、用户进程和旧包未操作，未沿用历史鼠标许可。Codex及飞书原专项边界保持。

Codebase Memory已连接，先检查指定名不存在，核对旧索引根目录及覆盖，再按用户指定Instant-taskboard重建。最终刷新及覆盖见INDEX-STATUS。完整ZIP逐文件长度与SHA256核对；普通本地修改未上传GitHub。
""")

previous = json.loads((ROOT / "docs/BUILD-INFO-0.9.10-local.json").read_text(encoding="utf-8-sig"))
scope = {x["Path"] for x in previous["Sources"]}
scope.update([
    "src/Taskboard.Desktop/BallSurface.EdgeHide.cs", "src/Taskboard.Desktop/Ux0911Checks.cs",
    "src/Taskboard.Desktop/SettingsSurface.cs", "src/Taskboard.Desktop/SurfaceWindow.cs",
    "src/Taskboard.Desktop/BallSurface.cs", "src/Taskboard.Desktop/EditorSurface.cs",
    "src/Taskboard.Desktop/ColumnSurface.cs", "src/Taskboard.Core/Models.cs",
    "src/Taskboard.Desktop/BoardController.cs", "src/Taskboard.Desktop/Program.cs",
    "src/Taskboard.Desktop/Ux099Checks.cs", "tools/Write-UX0911-Delivery.py", "AGENTS.md",
    "docs/ACCEPTANCE-0.9.11.md", "docs/VERIFICATION-0.9.11.json",
    "docs/PRD.md", "docs/HANDOFF.md", "docs/INDEX.md", "docs/IMPLEMENTATION.md",
    "docs/TASKS.md", "docs/INDEX-STATUS.md", "docs/USER-GUIDE.md", "README.md",
])
write("test-output/ux0911-source-scope.json", json.dumps(sorted(scope), ensure_ascii=False, indent=2))
print(json.dumps(dict(Version="0.9.11", Passed=verification["Passed"], Sources=len(scope)), ensure_ascii=False))
