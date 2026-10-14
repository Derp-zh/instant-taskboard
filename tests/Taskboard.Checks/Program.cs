using System.Diagnostics;
using System.Text.Json;
using Taskboard.Core;

try
{
if (args.Length != 2 || args[0] != "--data-dir") throw new ArgumentException("测试必须指定 --data-dir 隔离目录");
var root = Path.GetFullPath(args[1]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new ArgumentException("请使用新的空测试目录");
Directory.CreateDirectory(root);
var checks = new List<string>();
void Assert(bool value, string name) { if (!value) throw new Exception("失败：" + name); checks.Add(name); }
void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { checks.Add(name); return; } catch (JsonException) { checks.Add(name); return; } throw new Exception("未拒绝：" + name); }
var engine = new BoardEngine(new BoardState());
var blank = engine.NewSession(Lane.Running); engine.StageSession(blank);
Assert(engine.State.Sessions.Count == 0 && engine.State.Tasks.Count == 0, "空内容不形成草稿或任务");
blank.Working.Stages[0].Name = "改名"; engine.AddStage(blank); engine.StageSession(blank);
Assert(engine.State.Sessions.Count == 0, "仅改阶段名和新增空阶段不暂存");
blank.Working.AiIds.Add(engine.State.AiOptions[0].Id); engine.StageSession(blank);
Assert(engine.State.Sessions.Count == 0, "仅选AI不暂存");
blank.Working.Stages[0].Text = "只有详情\n第二行"; engine.StageSession(blank);
Assert(engine.State.Sessions.Count == 1 && engine.State.Tasks.Count == 0 && !engine.Search("详情").Any(), "非空草稿暂存但不参与正式列表与搜索");
var first = engine.Submit(blank);
Assert(first.DisplayTitle == "只有详情" && engine.State.Tasks.Count == 1 && engine.State.Sessions.Count == 0, "无名称可以正式保存并以详情首行展示");
var initialId = first.Id;
var session = engine.Edit(first.Id); session.Working.Title = "未提交";
engine.StageSession(session);
Assert(engine.State.Tasks[0].Title == "", "编辑副本不修改正式任务");
Assert(ReferenceEquals(session, engine.Edit(first.Id)), "同一任务仅一份编辑会话");
engine.Discard(session);
Assert(engine.State.Tasks[0].Title == "" && engine.State.Sessions.Count == 0, "X放弃已建任务未提交修改");
session = engine.Edit(first.Id);
for (var i = 0; i < 5; i++) engine.AddStage(session);
session.Working.Stages[1].Name = "已改名";
session.Working.CurrentStageId = session.Working.Stages[2].Id;
Assert(session.Working.Stages[^1].Number == 6, "阶段改名不影响编号递增");
Assert(session.Working.CurrentStageId == session.Working.Stages[2].Id, "新增阶段不切换当前阶段");
session.Working.Stages[5].Text = "隐藏阶段检索词"; first = engine.Submit(session);
Assert(first.Id == initialId && BoardEngine.CardStages(first).Select(x => x.Number).SequenceEqual(new[] { 1, 2, 3, 4 }), "稳定编号及前一当前后两阶段摘要");
Assert(engine.Search("隐藏阶段检索词").Single().Id == first.Id, "搜索覆盖非当前阶段正文");
engine.Move(first.Id, Lane.Inspiration, null);
Assert(BoardEngine.CardStages(first).Single().Number == 0 && first.Stages.Count == 7, "灵感只展示阶段0且保留全部阶段");
engine.Move(first.Id, Lane.Refining, null); engine.Undo(); engine.Undo();
Assert(engine.State.Tasks[0].Lane == Lane.Running, "跨列移动可连续撤销");
first = engine.State.Tasks[0]; session = engine.Edit(first.Id);
session.Working.Title = "人工修改";
first.PendingReasons.Add("后台结果"); first.Reminder.LastOccurrence = DateTime.Today;
first.Reminder.NextLocal = DateTime.Today.AddDays(3); first.Lane = Lane.Action;
first = engine.Submit(session);
Assert(first.Title == "人工修改" && first.PendingReasons.Contains("后台结果") && first.Reminder.NextLocal == DateTime.Today.AddDays(3) && first.Lane == Lane.Action, "提交标题保留后台提醒及列表变化");
engine.Undo(); first = engine.State.Tasks[0];
Assert(first.Title == "" && first.PendingReasons.Contains("后台结果") && first.Lane == Lane.Action, "字段撤销保留后台更新");
var secondSession = engine.NewSession(Lane.Action); secondSession.Working.Title = "第二任务"; var second = engine.Submit(secondSession);
var thirdSession = engine.NewSession(Lane.Action); thirdSession.Working.Title = "第三任务"; var third = engine.Submit(thirdSession);
engine.Move(first.Id, Lane.Action, null);
Assert(engine.State.Tasks.Where(x => x.Lane == Lane.Action).Select(x => x.Id).SequenceEqual(new[] { second.Id, third.Id, first.Id }), "同列向后移动不发生偏移");
engine.Undo();
Assert(engine.State.Tasks.Where(x => x.Lane == Lane.Action).First().Id == first.Id, "撤销排序恢复原位置");
engine.Delete(second.Id); Assert(!engine.State.Tasks.Any(x => x.Id == second.Id), "删除任务"); engine.Undo();
Assert(engine.State.Tasks[1].Id == second.Id, "撤销删除恢复内容与顺序");
var optionId = engine.State.AiOptions[0].Id; var priorityId = engine.State.PriorityOptions[0].Id;
first = engine.State.Tasks[0]; first.AiIds = [optionId]; first.PriorityId = priorityId;
session = engine.Edit(first.Id); session.Working.Title = "修改选项前打开的编辑";
engine.DeleteOption(optionId, true); engine.DeleteOption(priorityId, false);
Assert(first.AiIds.Count == 0 && first.PriorityId is null && session.Working.AiIds.Count == 0 && session.Original.PriorityId is null, "删除选项清空任务及编辑引用");
engine.Submit(session); engine.Undo(); Validation.Check(engine.State); checks.Add("删除选项后保存撤销不恢复无效引用");
first = engine.State.Tasks[0];
first.Reminder = new Reminder { Enabled = true, Repeat = Repeat.Daily, Time = TimeSpan.FromHours(9), NextLocal = new DateTime(2026, 10, 1, 9, 0, 0) };
first.Lane = Lane.Paused;
var now = new DateTime(2026, 10, 13, 10, 0, 0);
var due = ReminderClock.Tick(engine.State, now);
Assert(due.Count == 1 && first.Lane == Lane.Action && first.Reminder.NextLocal == new DateTime(2026, 10, 14, 9, 0, 0), "睡眠错过多个周期只补一次且移入待操作");
Assert(ReminderClock.Tick(engine.State, now).Count == 0, "同一次到期不重复提醒");
first.Lane = Lane.Done; first.Reminder.NextLocal = now.AddDays(-1);
Assert(ReminderClock.Tick(engine.State, now).Count == 0, "已完成任务停止提醒");
first.Lane = Lane.Running; first.Reminder.Repeat = Repeat.Once;
Assert(ReminderClock.Tick(engine.State, now).Count == 1 && !first.Reminder.Enabled, "一次性提醒只执行一次");
var weekly = new Reminder { Repeat = Repeat.Weekly, Weekday = DayOfWeek.Monday, Time = TimeSpan.FromHours(9) };
Assert(ReminderClock.Next(weekly, new DateTime(2026, 10, 12, 9, 0, 0)) == new DateTime(2026, 10, 19, 9, 0, 0), "每周提醒严格推进到未来");
session = engine.Edit(first.Id); session.Working.Title = "退出后恢复的未提交修改"; engine.StageSession(session);
var draft = engine.NewSession(Lane.Refining); draft.Working.Title = "持久新建草稿"; draft.Visible = false; engine.StageSession(draft);
var store = new AtomicStore(Path.Combine(root, "persistence")); await store.SaveAsync(Copies.Of(engine.State));
var reloaded = store.Load();
Assert(reloaded.Sessions.Any(x => x.TaskId == first.Id && x.Working.Title == "退出后恢复的未提交修改") && reloaded.Tasks[0].Title != "退出后恢复的未提交修改", "未提交编辑跨进程恢复但不变成正式修改");
Assert(reloaded.Sessions.Any(x => x.TaskId is null && x.Working.Title == "持久新建草稿"), "关闭新建草稿跨进程保留");
await store.SaveAsync(Copies.Of(engine.State));
File.WriteAllText(store.MainPath, "{bad"); reloaded = store.Load();
Assert(reloaded.Tasks.Count == 3 && store.RecoveryMessage is not null && Directory.GetFiles(store.DirectoryPath, "*.damaged-*").Length == 1, "损坏主文件从有效备份恢复且保留原件");
File.WriteAllText(store.MainPath, "{}"); File.WriteAllText(store.BackupPath, "[]");
var badPrimary = File.ReadAllText(store.MainPath); Reject(() => store.Load(), "主文件备份都坏时拒绝启动");
Assert(File.ReadAllText(store.MainPath) == badPrimary, "坏数据不会被空看板覆盖");
var bad = Copies.Of(engine.State); bad.Tasks[0].Stages = [null!]; Reject(() => Validation.Check(bad), "拒绝空阶段元素");
bad = Copies.Of(engine.State); bad.Tasks[0].CurrentStageId = Guid.NewGuid(); Reject(() => Validation.Check(bad), "拒绝无效当前阶段");
bad = Copies.Of(engine.State); bad.Tasks.Add(Copies.Of(bad.Tasks[0])); Reject(() => Validation.Check(bad), "拒绝重复任务编号");
bad = Copies.Of(engine.State); bad.Tasks[0].AiIds = [Guid.NewGuid()]; Reject(() => Validation.Check(bad), "拒绝未知选项引用");
bad = Copies.Of(engine.State); bad.Preferences.Positions["bad"] = new WindowPosition { Width = double.NaN, Height = 100 }; Reject(() => Validation.Check(bad), "拒绝非法窗口尺寸");
bad = Copies.Of(engine.State); bad.AiOptions[0].Color = "not-color"; Reject(() => Validation.Check(bad), "拒绝非法颜色");
bad = Copies.Of(engine.State); bad.Sessions.Add(new EditSession()); Reject(() => Validation.Check(bad), "拒绝空持久草稿");
var failStore = new AtomicStore(Path.Combine(root, "failure"));
await failStore.SaveAsync(Copies.Of(engine.State)); var untouched = File.ReadAllBytes(failStore.MainPath);
using (var locked = new FileStream(failStore.MainPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    try { await failStore.SaveAsync(Copies.Of(engine.State)); throw new Exception("未识别写入失败"); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { checks.Add("被占用的主文件使写入明确失败"); }
}
Assert(File.ReadAllBytes(failStore.MainPath).SequenceEqual(untouched), "写入失败不破坏旧文件");
var limitEngine = new BoardEngine(new BoardState()); var limitDraft = limitEngine.NewSession(Lane.Refining); limitDraft.Working.Title = "0"; var limitTask = limitEngine.Submit(limitDraft);
for (var i = 1; i <= 25; i++) { var edit = limitEngine.Edit(limitTask.Id); edit.Working.Title = i.ToString(); limitEngine.Submit(edit); }
for (var i = 0; i < 20; i++) limitEngine.Undo();
Assert(!limitEngine.CanUndo && limitEngine.State.Tasks[0].Title == "5", "撤销仅保留本次运行最近20次提交");
var noUndo = limitEngine.Edit(limitTask.Id); noUndo.Working.Title = "灵感自动输入"; limitEngine.Submit(noUndo, false);
Assert(!limitEngine.CanUndo, "灵感自动输入保存不计入字段撤销步数");
var configState = Copies.Of(engine.State); configState.Preferences.ConnectionConfigs["codex"] = "保留的接入配置"; configState.Preferences.ConnectionConfigs["飞书"] = "待专项接入";
var configuration = ConfigurationBackup.Capture(configState, Path.Combine(root, "configured-data"));
var configurationFile = Path.Combine(root, "configuration.json"); configuration.Write(configurationFile);
using (var document = JsonDocument.Parse(File.ReadAllBytes(configurationFile)))
    Assert(!document.RootElement.TryGetProperty("Tasks", out _) && !document.RootElement.TryGetProperty("Sessions", out _) && !File.ReadAllText(configurationFile).Contains("退出后恢复的未提交修改"), "配置备份不含任务、阶段内容或草稿");
var restoredConfiguration = ConfigurationBackup.Read(configurationFile);
Assert(restoredConfiguration.Preferences.ConnectionConfigs["codex"] == "保留的接入配置" && restoredConfiguration.Preferences.GlassTransparency == 20, "平台配置及默认20%透明度可往返恢复");
var tasksBeforeConfig = JsonSerializer.Serialize(configState.Tasks); var sessionsBeforeConfig = JsonSerializer.Serialize(configState.Sessions);
restoredConfiguration.AiOptions.Clear(); restoredConfiguration.PriorityOptions.Clear();
var referenced = configState.AiOptions[0].Id; configState.Tasks[0].AiIds.Add(referenced);
tasksBeforeConfig = JsonSerializer.Serialize(configState.Tasks);
var configured = restoredConfiguration.Apply(configState);
Assert(JsonSerializer.Serialize(configured.Tasks) == tasksBeforeConfig && JsonSerializer.Serialize(configured.Sessions) == sessionsBeforeConfig, "恢复配置不更改任务、提醒、草稿或未提交输入");
Assert(configured.AiOptions.Any(x => x.Id == referenced), "恢复配置保留现有任务所引用选项");
var oldBackupFile = Path.Combine(root, "old-full-data.json"); File.WriteAllText(oldBackupFile, JsonSerializer.Serialize(configState));
Reject(() => ConfigurationBackup.Read(oldBackupFile), "配置导入拒绝旧任务全量备份");
var unexpectedConfig = File.ReadAllText(configurationFile).TrimEnd(); File.WriteAllText(Path.Combine(root, "unexpected-config.json"), unexpectedConfig[..^1] + ",\"Tasks\":[]}");
Reject(() => ConfigurationBackup.Read(Path.Combine(root, "unexpected-config.json")), "配置包夹带任务字段时拒绝导入");
var invalidTransparency = Copies.Of(engine.State); invalidTransparency.Preferences.GlassTransparency = 101;
Reject(() => Validation.Check(invalidTransparency), "拒绝超范围透明度");
invalidTransparency.Preferences.GlassTransparency = double.NaN;
Reject(() => Validation.Check(invalidTransparency), "拒绝非有限透明度");
var legacyPreferences = JsonSerializer.Serialize(engine.State).Replace("\"GlassTransparency\":20,", "");
Assert(JsonSerializer.Deserialize<BoardState>(legacyPreferences)!.Preferences.GlassTransparency == 20, "旧版本缺少透明度字段时兼容默认20%");
var oldBallPreferences = JsonSerializer.Serialize(engine.State).Replace("\"BallTransparency\":40,", "");
Assert(JsonSerializer.Deserialize<BoardState>(oldBallPreferences)!.Preferences.BallTransparency == 40, "旧数据缺少球透明度时独立采用40%，普通窗口仍为20%");
var invalidBall = Copies.Of(engine.State); invalidBall.Preferences.BallTransparency = double.PositiveInfinity;
Reject(() => Validation.Check(invalidBall), "拒绝无限大的球透明度，防止合成画面失效");
var ballConfiguration = ConfigurationBackup.Capture(engine.State, root); ballConfiguration.Preferences.BallTransparency = 55;
Assert(ballConfiguration.Apply(engine.State).Preferences.BallTransparency == 55 && engine.State.Preferences.BallTransparency == 40, "球透明度随配置备份恢复，源数据保持不变");
await IntegrationRules.Run(root, Assert);
var draftClock = new BoardEngine(new BoardState()); var clockSession = draftClock.NewSession(Lane.Action);
Assert(clockSession.CreatedUtc is { Kind: DateTimeKind.Utc }, "新建草稿记录独立UTC创建时间");
clockSession.Working.Title = "创建时间保存样本"; clockSession.CreatedUtc = new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc); draftClock.StageSession(clockSession);
var clockStore = new AtomicStore(Path.Combine(root, "draft-clock")); await clockStore.SaveAsync(draftClock.State);
Assert(clockStore.Load().Sessions.Single().CreationTime == clockSession.CreatedUtc, "草稿创建时间跨保存和重启保留");
clockSession.CreatedUtc = null;
Assert(clockSession.CreationTime == clockSession.Original.UpdatedUtc, "旧草稿缺少创建字段时使用原始时间，切换不改创建先后");
var perf = new BoardEngine(new BoardState());
var lists = new BoardEngine(new BoardState());
var inspirationColumn = lists.State.Preferences.Columns.First(x => x.BuiltinLane == Lane.Inspiration);
inspirationColumn.Visible = false; inspirationColumn.Name = "随手记录";
Assert(lists.State.Preferences.Columns.Count(x => x.Visible && !x.Deleted) == 5 && inspirationColumn.Contains(new WorkItem { Lane=Lane.Inspiration }),"灵感隐藏不计数，改名保持角色");
foreach (var protectedColumn in lists.State.Preferences.Columns.Where(x => x.Protected))
{ try { BoardColumns.Delete(lists.State,protectedColumn); throw new Exception("保护列表被删除"); } catch (InvalidOperationException) { checks.Add("保护列表禁止删除："+protectedColumn.Name); } }
BoardColumns.SetVisibleCount(lists.State.Preferences,7);
Assert(lists.State.Preferences.Columns.Count(x=>x.Visible && !x.Deleted)==7,"数量增加可恢复隐藏列并新增自定义列");
var custom = lists.State.Preferences.Columns.Last(); var customSession = lists.NewSession(Lane.Refining); custom.Assign(customSession.Working); custom.Assign(customSession.Original);
customSession.Working.Title="自定义列任务"; var customTask = lists.Submit(customSession);
Assert(custom.Contains(customTask) && !lists.State.Preferences.Columns.First(x=>x.BuiltinLane==Lane.Refining).Contains(customTask),"自定义列任务不混入完善中");
try { BoardColumns.Delete(lists.State,custom); throw new Exception("非空列被删除"); } catch (InvalidOperationException) { checks.Add("正式任务阻止删除列表"); }
var configWithLists = ConfigurationBackup.Capture(lists.State,root);
configWithLists.Preferences.Columns.Remove(custom); var appliedLists = configWithLists.Apply(lists.State);
Assert(appliedLists.Preferences.Columns.Any(x=>x.Id==custom.Id && !x.Deleted) && JsonSerializer.Serialize(appliedLists.Tasks)==JsonSerializer.Serialize(lists.State.Tasks),"配置导入保留现有任务引用的列表且任务不变");
lists.Delete(customTask.Id); BoardColumns.Delete(lists.State,custom); lists.Undo();
Assert(!custom.Deleted && custom.Visible && custom.Contains(lists.State.Tasks.Single()),"空列表删除后撤销任务会恢复列表身份");
lists.Move(customTask.Id,Lane.Running,null); lists.Undo(); Assert(custom.Contains(lists.State.Tasks.Single()),"自定义列移动撤销恢复准确列表");
var draftColumn = lists.State.Preferences.Columns.First(x=>x.BuiltinLane==Lane.Paused); var columnDraft=lists.NewSession(Lane.Paused); columnDraft.Working.Title="非空草稿"; lists.StageSession(columnDraft);
try { BoardColumns.Delete(lists.State,draftColumn); throw new Exception("草稿列被删除"); } catch (InvalidOperationException) { checks.Add("非空草稿阻止删除列表"); }
BoardColumns.SetVisibleCount(lists.State.Preferences,0); Validation.Check(lists.State);
Assert(lists.State.Preferences.Columns.All(x=>!x.Visible) && lists.State.Tasks.Count==1,"所有列可隐藏而不丢任务");
var noColumnsJson = JsonSerializer.SerializeToNode(new BoardState())!; noColumnsJson["Preferences"]!.AsObject().Remove("Columns");
Assert(noColumnsJson.Deserialize<BoardState>()!.Preferences.Columns.Count==6,"旧数据无列配置时兼容六列");
var codexTask = new WorkItem { Title="关联检查",Lane=Lane.Running,Codex=new CodexBinding { ThreadId="thread-1" },TimerUnread=true };
long eventSequence=0;
JsonElement Message(string method,object value) => JsonSerializer.SerializeToElement(new { method,@params=value });
bool Apply(string method,object value,bool live=true,int generation=1,string epoch="source-1") => CodexState.Apply(codexTask,Message(method,value),++eventSequence,generation,epoch,live);
Assert(!Apply("turn/started",new {threadId="thread-1",turn=new {id="turn-1"}},false) && codexTask.Codex.Phase==CodexPhase.Unknown,"历史来源不能宣称运行或自动流转");
Apply("turn/started",new {threadId="thread-1",turn=new {id="turn-1"}});
Assert(codexTask.Codex.Phase==CodexPhase.Running && codexTask.Lane==Lane.Running,"可信开始事件确认运行");
Apply("thread/status/changed",new {threadId="thread-1",status=new {type="active",activeFlags=new[]{"waitingOnUserInput"}}});
Assert(codexTask.Lane==Lane.Action && codexTask.Codex.Phase==CodexPhase.WaitingForInput,"等待回答进入待操作");
Apply("thread/status/changed",new {threadId="thread-1",status=new {type="active",activeFlags=new[]{"waitingOnApproval"}}});
Assert(codexTask.Codex.Phase==CodexPhase.WaitingForApproval,"权限等待独立显示");
Apply("serverRequest/resolved",new {threadId="thread-1",requestId=7}); Assert(codexTask.Lane==Lane.Action,"单项请求消失不冒充恢复执行");
Apply("thread/status/changed",new {threadId="thread-1",status=new {type="active",activeFlags=Array.Empty<string>()}});
Assert(codexTask.Lane==Lane.Running && codexTask.TimerUnread,"可信恢复回进行中且时间红点保留");
Assert(!Apply("turn/completed",new {threadId="other",turn=new {id="turn-1",status="completed"}}),"别的会话事件不能污染关联");
Assert(!Apply("turn/completed",new {threadId="thread-1",turn=new {id="older-turn",status="completed"}}),"旧轮次结束不能覆盖当前运行");
Assert(!Apply("turn/completed",new {threadId="thread-1",turn=new {id="turn-1",status="completed"}},generation:2),"旧关联代次不能修改状态");
Apply("turn/completed",new {threadId="thread-1",turn=new {id="turn-1",status="completed"}});
Assert(codexTask.Codex.Phase==CodexPhase.ReadyForReview && codexTask.Lane==Lane.Action,"正常结束进入待操作但不完成任务");
Apply("turn/completed",new {threadId="thread-1",turn=new {id="turn-1",status="failed"}}); Assert(codexTask.Codex.Phase==CodexPhase.Failed,"失败单独记录");
Apply("turn/completed",new {threadId="thread-1",turn=new {id="turn-1",status="interrupted"}}); Assert(codexTask.Codex.Phase==CodexPhase.Interrupted,"中断单独记录");
Apply("probe/disconnected",new {threadId="thread-1"}); Assert(codexTask.Codex.Phase==CodexPhase.Unknown && codexTask.Lane==Lane.Action,"断线待确认且不误报结束");
codexTask.Lane=Lane.Done; Apply("turn/started",new {threadId="thread-1",turn=new {id="turn-2"}}); Assert(codexTask.Lane==Lane.Done,"AI事件不拉回已完成");
codexTask.Lane=Lane.Action; codexTask.ColumnId=custom.Id; Apply("turn/started",new {threadId="thread-1",turn=new {id="turn-3"}}); Assert(codexTask.Lane==Lane.Action && codexTask.ColumnId==custom.Id,"自定义列不参与AI自动流转");
Assert(!CodexState.Apply(codexTask,Message("probe/disconnected",new {threadId="thread-1"}),0,1,"source-1",true),"乱序信号拒绝");
var bindingOriginal=Copies.Of(codexTask); var bindingEdited=Copies.Of(codexTask); bindingEdited.Title="新标题";
codexTask.Codex.Phase=CodexPhase.WaitingForApproval;
Assert(BoardEngine.Merge(bindingOriginal,bindingEdited,codexTask).Codex!.Phase==CodexPhase.WaitingForApproval,"标题提交保留监听最新状态");
bindingEdited.Codex=new CodexBinding {ThreadId="thread-2"}; var rebound=BoardEngine.Merge(bindingOriginal,bindingEdited,codexTask);
Assert(rebound.Codex!.Generation==2 && rebound.Codex.Phase==CodexPhase.Unknown && rebound.Codex.PreviousThreads.Contains("thread-1"),"改绑提升代次并保留旧关联历史");
for (var i = 0; i < 300; i++)
{
    var s = perf.NewSession((Lane)(i % 6)); s.Working.Title = "任务" + i;
    for (var j = 0; j < 4; j++) { if (j > 0) perf.AddStage(s); s.Working.Stages[j].Text = "性能任务详情 " + i + " 第" + j + "阶段"; }
    perf.Submit(s);
}
for (var i = 0; i < 10; i++) { var s = perf.NewSession(Lane.Refining); s.Working.Title = "草稿" + i; perf.StageSession(s); }
var watch = Stopwatch.StartNew(); for (var i = 0; i < 100; i++) _ = perf.Search("详情").ToArray(); watch.Stop();
var searchMs = watch.Elapsed.TotalMilliseconds / 100;
Assert(searchMs < 200, "300任务全部阶段搜索反馈低于200ms");
watch.Restart(); var perfStore = new AtomicStore(Path.Combine(root, "performance")); await perfStore.SaveAsync(Copies.Of(perf.State)); watch.Stop(); var saveMs = watch.Elapsed.TotalMilliseconds;
watch.Restart(); _ = perfStore.Load(); watch.Stop(); var loadMs = watch.Elapsed.TotalMilliseconds;
var result = new { passed = checks.Count, checks, performance = new { tasks = 300, stagesPerTask = 4, drafts = 10, searchMs, saveMs, loadMs } };
File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { result.passed, result.performance }));
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    Environment.ExitCode = 1;
    if (args.Length == 2 && args[0] == "--data-dir")
    {
        Directory.CreateDirectory(args[1]);
        File.WriteAllText(Path.Combine(args[1], "failure.txt"), e.ToString());
    }
}
