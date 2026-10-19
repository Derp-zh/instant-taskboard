from pathlib import Path

root = Path(__file__).resolve().parent.parent
def read(path): return (root / path).read_text(encoding="utf-8-sig")
def write(path, text): (root / path).write_text(text, encoding="utf-8", newline="\n")
def prepend_after_title(path, text):
    original = read(path)
    title, body = original.split("\n", 1)
    write(path, title + "\n\n" + text.strip() + "\n" + body)

summary = "0.9.4已选定暂存切换1.35倍（约266.67ms）；其他动效仍100ms，快速重开同样完整播放并重新触发白条。编号恢复40×40圆形，白边亮度保留，X仍48×40。切换前2/3点击忽略，最后1/3最多登记一次后续切换。外部鼠标监听与界面分离，点击其他应用整组收起及同窗口同输入恢复继续保留，看板内部窗口外单击保持、双击关闭悬浮窗口。多白条未重现，按用户补充不做专项改动。普通修改未上传GitHub，公开版本仍0.9.1。"
entry = "release/即时任务看板-0.9.4-standard/即时任务看板.exe"
prepend_after_title("docs/INDEX.md", "2026-10-15最新本地交付：" + summary + " 当前入口：PRD第28节U86至U90/A104至A108，TASKS第16节T109至T114，[验收](ACCEPTANCE-0.9.4.md)、[验证摘要](VERIFICATION-0.9.4.json)、[完整运行包清单](BUILD-INFO-0.9.4-standard.json)。以下为历史交付来源。")
handoff = read("docs/HANDOFF.md")
start = handoff.index("## 当前状态")
end = handoff.index("## 阅读顺序")
handoff = handoff[:start] + """## 当前状态

- 最新本地交付：""" + summary + """
- 本地常用入口：`""" + entry + """`。先从托盘退出旧版，再打开新版；保留完整目录。既有正式数据不迁移、不新建另一份；旧比较包保留。
- 本轮规则见PRD第28节及TASKS第16节；既有共享暂存、仅名称/详情暂存、内部新建先选状态、悬浮透明度10%、提醒仅勾选圆、圆角补口和新阶段移左继续保留。设置单击例外已删除。
- 点击其他应用直接整组收起并保留会话，再展开恢复；内部双击仅关闭悬浮窗口。不要退回“有悬浮窗口不收起”或焦点回流后延迟收起的旧规则。
- 新窗口测试均用新隔离数据；鼠标测试已与用户协调，不关闭用户运行版本、不改正式自启/任务数据。今后新的鼠标测试仍需协调。
- Codebase Memory优先项目taskboard，先index_status再get_architecture；本轮连接恢复后的实际刷新回执见INDEX-STATUS，路径覆盖仍需核验。
- 已公开发布0.9.1，普通本地修改不发布；接到明确GitHub更新请求时，必须源码和完整ZIP/SHA256 Release同时更新。

""" + handoff[end:]
handoff = handoff.replace("3. 验收或交付时读取 [0.9.3 验收记录](ACCEPTANCE-0.9.3.md)、[验证摘要](VERIFICATION-0.9.3.json)、[原速清单](BUILD-INFO-0.9.3-normal.json) 与 [快版清单](BUILD-INFO-0.9.3-fast135.json)。普通修改不必重读全部历史证据。", "3. 验收或交付时读取 [0.9.4验收](ACCEPTANCE-0.9.4.md)、[验证摘要](VERIFICATION-0.9.4.json)和[常用包清单](BUILD-INFO-0.9.4-standard.json)。普通修改不必重读全部历史证据。")
write("docs/HANDOFF.md", handoff)
readme = read("README.md").splitlines()
for i, line in enumerate(readme):
    if line.startswith("当前本地版本："):
        readme[i] = "当前本地版本：" + summary + " 需求入口见[文档索引](docs/INDEX.md)，运行与升级见[使用说明](docs/USER-GUIDE.md)。"
write("README.md", "\n".join(readme) + "\n")
guide = read("docs/USER-GUIDE.md").splitlines()
for i, line in enumerate(guide):
    if line.startswith("新版电脑端0.9.3"):
        guide[i] = "新版电脑端0.9.4，本地修订，2026-10-15。" + summary + " 悬浮窗口透明度默认10%，多列表20%、球40%，三者独立。适用Windows 10 2004及以上x64。"
    elif line.startswith("本轮提供原速360毫秒"):
        guide[i] = "用户已选择1.35倍作为常用暂存切换速度。先从托盘退出旧版，再启动`" + entry + "`，保留整个运行目录。新版沿用同一正常保存路径，不建立新的正式数据；常用启动登记和快捷方式规则恢复，隔离测试不改正式登记。旧0.9.3比较包保留。"
    elif line.startswith("悬浮窗口从中央向两侧揭开"):
        guide[i] = "悬浮窗口从中央向两侧揭开，关闭时向中央收拢；每次展开都重新触发贯穿窗口全高的中央白条。完整及快速反向开合均100毫秒，反向衔接当前画面；球和列表完整开合也仍100毫秒。暂存切换采用1.35倍、约266.67毫秒的纯横向抽牌，抽出后升到前层再盖回，不重播画卷。动画前2/3的点击忽略，最后1/3最多登记一次后续切换，结束后执行；提前完成的双击不会连续抽两张。"
    else:
        guide[i] = line.replace("后台编号采用与前台X相同的48×40亮色白边胶囊", "后台编号恢复40×40圆形，保留亮色白边；前台X保持48×40")
write("docs/USER-GUIDE.md", "\n".join(guide) + "\n")
prepend_after_title("docs/IMPLEMENTATION.md", """## 0.9.4常用速度与重复开合（2026-10-15）

PRD第28节为最新用户确认。DraftSwitchSpeed默认1.35，只有DraftSwitchMotion引用加速值；列表、球和画卷仍100ms。已选为常用版，因此取消上一版比较包跳过版本登记/快捷方式的条件，隔离启动仍跳过正式登记。

快速反向白条缺失的可触发路径：RevealScroll沿用旧进度，并将时间乘剩余距离；收起尚在准备时重新展开，from可能仍为1，从而跳过泛光并只运行1ms。现在画卷固定100ms，裁口延续当前位置，泛光按本次方向独立重新起算；初始打开和最终收起的白芯保持至首尾帧，防止100ms短动效合成端点漏掉25ms光效。不改变球与列的时间线。

OutsideClickObserver原在界面线程注册WH_MOUSE_LL。系统会静默移除超时低级钩子，界面重绘/布局耗时期间存在检测失效路径；依据[微软LowLevelMouseProc说明](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)。改为独立消息线程，仅记录物理位置、原始按下时间和工作区代次并立即返回；有序Channel工作队列负责原生命中，再投递界面处理。原生命中也与钩子分离，避免WindowFromPoint消息等待影响监听。保留真实点击才收起、内部单双击、菜单/原生对话框和代次过滤；没有重启或轮询强行收起。真实测试将隔离界面故意阻塞1700ms，阻塞期间及恢复后的外部点击均有效，恢复同一窗口和输入。

编号只改40×40圆形，底色白边不变；X仍48×40。暂存Switch开始合成时间由LiveBackdrop直接登记，准备期和动画前2/3不接受后续切换；最后1/3只接受一个pendingSwitch，不覆盖。WPF点击和动画中原生命中均使用原始按下时间，提前事件晚到也忽略；交接完成后才执行登记的切换，新动作清掉被中断的旧登记。

用户允许未重现的多白条项跳过。专项隔离三张暂存仅看到一个贯穿宿主的泛光层，不作未经验证的多图层修补。原采样记录保留，短闪可能漏采的失败不伪装成通过。采样器改为从一次原生画面读取六行统计光条，PNG写入移至采样结束，避免保存图片阻塞25ms闪光采样；重复整组恢复及快速重开另设实机验收，旧固定等待改为等待真正开合结束。

最终证据以VERIFICATION-0.9.4.json和ACCEPTANCE-0.9.4.md为准，完整便携包见BUILD-INFO-0.9.4-standard.json。未操作正式任务数据、用户进程或GitHub。
""")
print("0.9.4当前入口及已确认规则已写入")
