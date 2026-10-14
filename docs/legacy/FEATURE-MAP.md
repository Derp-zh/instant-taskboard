# 新需求任务与旧功能对应表

2026-10-14补充：T47至T51对应当前PRD U27至U31。卡片只压缩内部纵向留白；搜索检查真实文字视口与光标；圆角形状内可聚焦、形状外不命中；菜单第二次点击关闭。跨列/排序使用缓存跟随图像与目标虚线占位，松手才修改模型。旧程序的仅改变鼠标拖放提示不足以满足新要求，当前实现和证据查../IMPLEMENTATION.md及../VERIFICATION-0.7.0.json。

日期：2026-10-12。任务编号依据 [任务清单](../TASKS.md)，思路章节依据 [重写思路](../REWRITE-IDEAS.md)。证据E编号见 [源码摘录](evidence/SOURCE-EXCERPTS.md)。

“已有核心”表示旧版有同类机制，但仍需从零重写；“部分相似”表示只借鉴局部；“未实现”表示不能宣称旧版可直接提供。下表覆盖全部28项电脑端任务和8项平台后续任务。

## 1 电脑端任务匹配

| 新任务 | 旧实现边界 | 相似来源和证据 | 重写思路 |
| --- | --- | --- | --- |
| T01 悬浮球托盘 | 已有核心；不在任务栏显示 | App.OnStartup、BallWindow、ColumnWindow；E01 | L01 |
| T02 同开同收右键 | 部分相似；旧版右键菜单存在，但球左键可拖、编辑不整组登记 | App.ToggleBoard、BoardWindow.Hide、BallWindow菜单；E02、E03 | L01、L03 |
| T03 自启首次快捷方式 | 未实现；便携启动脚本不等于自启 | App启动流程没有此配置；旧使用说明明确无自启 | L01；D09 |
| T04 六窗布局毛玻璃 | 已有核心；列表名称和默认参数不同 | Workspace.Defaults、ColumnWindow、Native.Glass；E04、E05、E22 | L02 |
| T05 下拉0.1秒 | 部分相似；旧版是缩放回弹110ms加错开 | WindowMotion.Show/Hide/Tick；E09、E10 | L03 |
| T06 自由移动 | 部分相似；鼠标位移可借鉴，旧版邻列挤压需舍弃 | WindowMover.Attach、Workspace.MoveInOrder；E08 | L02 |
| T07 两种拉伸入口 | 部分相似；只有常驻右下角入口，无菜单四边模式 | ColumnWindow.SetPanel、Native.KeepBelowBall；E04 | L02 |
| T08 任务草稿模型 | 部分相似；任务与稳定编号有，持久新建草稿无 | Models.TaskItem、Logic.Clone；E11、E13 | L04、L05 |
| T09 六列与迁移 | 部分相似；稳定分组编号和Move有，新名字/新结构迁移无 | Group.Defaults、Logic.Move；E12 | L04；保留旧数据不自动迁移 |
| T10 新建编辑提交 | 部分相似；保存副本有，无新字段排布与隐藏恢复 | TaskEditor构造和Save；E13、E14 | L05 |
| T11 持久草稿 | 未实现；旧draft仅存于编辑窗口内存 | TaskEditor.draft；E13、E14 | L05 |
| T12 多窗口堆叠 | 未实现；旧ShowDialog阻塞，不提供独立草稿堆叠 | BoardWindow.EditTask、Ui.Dialog | L05 |
| T13 详情阶段 | 部分相似；阶段列表、改名和主指针存在，但旧阶段是AI会话 | Models.Stage、Logic.Associate、TaskEditor.RenderStages；E11、E13 | L06 |
| T14 四阶段卡片 | 部分相似；标题圆角胶囊有，旧副阶段不是前一后两 | BoardWindow.Card/StageLine、Ui.Pill；E11、E26 | L06、L08 |
| T15 灵感列内编辑 | 未实现；旧灵感新建仍打开TaskEditor | BoardWindow.Render添加任务入口；E16及方法索引 | L07 |
| T16 点击拖放排序 | 已有核心；旧版仅标题编辑，点击/拖动互斥需统一 | BoardWindow.Card、Logic.Move；E12、E15 | L07 |
| T17 撤销 | 部分相似；只支持一次删除且20秒 | BoardWindow.DeleteTask/UndoDelete | L07；D06 |
| T18 AI优先级颜色 | 部分相似；固定平台和通用胶囊有，选项表/优先级/多选颜色无 | StageEditor平台下拉、Theme、Ui.Pill；E25、E26 | L08 |
| T19 搜索定位 | 部分相似；旧全文筛选和输入合并有，无扩展结果和红闪 | BoardWindow.Render/ShowSearch；E16、E17 | L09 |
| T20 定时提醒 | 已有核心；每天每周一次和补查存在，UI与草稿条件需改 | Logic.Tick、App.Remind、TaskEditor；E18、E02、E14 | L10 |
| T21 球悬停预览 | 未实现；旧球只有角标与提示 | BallWindow构造和Refresh | L09 |
| T22 设置 | 部分相似；基础设置存在，无连接卡片和尺寸开关 | BoardWindow.Settings、App.SetBallVisible | L11、L02 |
| T23 连接验证 | 未实现；旧版只有能力说明和打开链接/应用 | BoardWindow.Capabilities、StageEditor.OpenStage；E27 | L11 |
| T24 保存备份 | 已有核心；原子替换和损坏恢复有，校验需补齐 | Store.Read/Save、构造；E20、E21 | L12 |
| T25 后台字段合并 | 部分相似；仅提醒合并，AI与详情尚无统一提交规则 | TaskEditor.Save；E14 | L05、L10 |
| T26 增量刷新释放 | 部分相似；无变化复用有，有变化全量重建且重复监听 | BoardWindow.Render、WindowMover.Attach；E08、E16、E29 | L13 |
| T27 验收 | 部分相似；旧20断言和桌面靶窗经验有，新流程未测 | SelfTest.Run/UiProbe、DesktopCheck；E30、旧自测结果 | L13 |
| T28 便携交付 | 部分相似；相邻data文件夹和运行包有，多份包存在差异 | App数据目录、旧运行包身份记录 | L12、L13 |

## 2 平台后续任务匹配

| 新任务 | 旧实现边界 | 来源与新版处理 |
| --- | --- | --- |
| C01 Codex能力核实 | 未实现真实CLI信号读取 | 旧仅提供手填会话和平台说明；需单独核实，见L11 |
| C02 手动准确关联 | 部分相似 | Logic.Associate、StageEditor；可借鉴平台加会话唯一匹配，不将详情阶段作为会话，E11、E27 |
| C03 执行事件映射 | 部分相似，只有规则未接入 | ApplyEvent直接调用者只有SelfTest.Run，E19及ai-event-callers；去重/旧事件过滤可借鉴，详情阶段不自动切换 |
| C04 接入验收 | 未实现 | 旧自测事件规则不证明连到Codex；另验收真实会话与断开恢复 |
| F01 飞书字段规则 | 未实现 | 重新规划任务编号、字段映射和冲突 |
| F02 双向同步 | 未实现 | JSON本地保存不是同步；另实现手机交换 |
| F03 同步验收 | 未实现 | 需要双端冲突、断网恢复和删除传播用例 |
| O01 其他AI | 未实现自动连接 | 旧固定平台选项/打开应用只是分类和跳转入口 |

## 3 写法查找索引

| 要查的思路 | 旧符号 | 对应证据 |
| --- | --- | --- |
| 收起不退出 | App.ToggleBoard、BoardWindow.Hide、App.ExitApp | E01、E02 |
| 独立窗口无覆盖空隙 | ColumnWindow、Workspace.Register | E04、E05 |
| 默认工作区与位置找回 | Workspace.Defaults/Clamp/RestoreDefaults | E05、E06、E07 |
| 鼠标位移与取消 | WindowMover.Attach | E08 |
| 动效方向反转 | WindowMotion.Show/Hide/Tick | E09、E10 |
| 滤除旧事件 | Logic.ApplyEvent | E19 |
| 周期只补一次 | Logic.Tick | E18 |
| 保存前用副本 | TaskEditor构造、Save | E13、E14 |
| 拖动前后插入修正 | BoardWindow.Card、Logic.Move | E12、E15 |
| 原子替换和备份 | Store.Save、构造 | E20、E21 |
| 系统毛玻璃加降级 | Native.Glass、ColumnWindow.SourceInitialized | E04、E22、E23 |
| 不让全量刷新变卡 | BoardWindow.Render的限制 | E16、E28、E29；L13给改写方向 |

完整符号定位见 [历史方法索引](evidence/method-index.json)。旧文件名不代表当前工程仍保留这些文件。

## 5 0.6第三轮修订参考边界

T43至T46以当前源码SurfaceWindow、LiveBackdrop、BallSurface、DraftStackSurface、MotionChecks为入口。旧Native.Glass与WindowMotion仅说明历史思路，不能证明圆形裁剪、局部实时背景或无闪烁已完成。新版移除背景截图及移动后隐藏重显，采用同一顶层窗口内的系统合成与透明输入表面；截图和原生事件作为验收依据，不能只检查窗口区域句柄。
