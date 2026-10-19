using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Explicit visible verification mode. Codex remains read-only; a separate test
// driver controls only its own original-desktop test chats. No synthetic events.
internal static class CodexDesktopLifecycleChecks
{
    internal static async Task Run(BoardController controller)
    {
        if (!controller.Isolated || controller.Smoke || controller.Engine.State.Tasks.Count != 0)
            throw new InvalidOperationException("原桌面验证只允许空白隔离看板");
        var root = controller.Store.DirectoryPath;
        using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "lifecycle-plan.json")));
        var threadA = plan.RootElement.GetProperty("A").GetString()!;
        var threadB = plan.RootElement.GetProperty("B").GetString()!;
        if (!Guid.TryParse(threadA, out _) || !Guid.TryParse(threadB, out _) || threadA == threadB)
            throw new InvalidOperationException("需要两个明确的原桌面测试聊天");
        controller.SuppressCollapse = true;
        var tasks = new Dictionary<string, Guid>();
        var observations = new List<object>(); var checks = new List<string>();
        var clock = Stopwatch.StartNew(); var snapshotVersion = 0;
        WorkItem Item(string alias) => controller.Engine.State.Tasks.Single(x => x.Id == tasks[alias]);
        WorkItem Create(string alias, Lane lane, string thread)
        {
            var session = controller.Engine.NewSession(lane); session.StatusSelected = true;
            session.Working.Title = "接入隔离测试 · " + alias;
            var stage = session.Working.Stages[0]; stage.IsRunning = true;
            stage.Codex = new CodexBinding { ThreadId = thread, ThreadName = "测试" + (thread == threadA ? "A" : "B") };
            var item = controller.Engine.Submit(session); tasks.Add(alias, item.Id); return item;
        }
        Create("A", Lane.Running, threadA); Create("B", Lane.Running, threadB);
        var multi = Create("multi", Lane.Running, threadA);
        var editing = controller.Engine.Edit(multi.Id); var second = controller.Engine.AddStage(editing);
        second.IsRunning = true; second.Codex = new CodexBinding { ThreadId = threadB, ThreadName = "测试B" };
        controller.Engine.Submit(editing);
        var paused = Create("pausedStage", Lane.Running, threadA);
        editing = controller.Engine.Edit(paused.Id); editing.Working.Stages[0].IsRunning = false;
        controller.Engine.AddStage(editing).IsRunning = true; controller.Engine.Submit(editing);
        foreach (var lane in new[] { Lane.Inspiration, Lane.Refining, Lane.Paused, Lane.Done }) Create("manual" + lane, lane, threadA);
        var customColumn = new BoardColumn { Name = "接入隔离自定义列", Visible = false };
        controller.Engine.State.Preferences.Columns.Add(customColumn);
        var custom = Create("custom", Lane.Running, threadA); custom.ColumnId = customColumn.Id;
        controller.Engine.Notify([custom.Id]);
        await controller.RestartCodex();
        controller.SetExpanded(true);
        var area = controller.WorkArea(controller.Ball); var width = Math.Min(180, (area.Width - 100) / 6 - 10);
        for (var i = 0; i < controller.Columns.Count; i++)
        { controller.Columns[i].Left = area.Left + 20 + i * (width + 8); controller.Columns[i].Top = area.Top + 20; controller.Columns[i].Width = width; controller.Columns[i].Height = 410; }
        await Task.Delay(250);
        void Check(bool passed, string text)
        { if (!passed) throw new InvalidOperationException(text); checks.Add(text); }
        object[] Rows() => tasks.Select(pair =>
        {
            var item = Item(pair.Key);
            var column = controller.Columns.FirstOrDefault(x => x.Definition.Contains(item));
            return (object)new { alias = pair.Key, lane = item.Lane.ToString(), custom = item.ColumnId is not null, item.TimerUnread,
                phases = item.Stages.Select(x => new { running = x.IsRunning, phase = x.Codex?.Phase.ToString(), awaiting = x.Codex?.AwaitingUserAction, hasTurn = x.Codex?.TurnId is not null }).ToArray(),
                cardInExpectedColumn = column?.Locate(item.Id) is not null, columnVisible = column?.IsVisible ?? false };
        }).ToArray();
        void Snapshot(string label)
        {
            var observer = controller.Codex;
            var value = new { version = ++snapshotVersion, label, ms = clock.ElapsedMilliseconds,
                source = observer.SourceKind, connected = observer.TransportConnected, identityVerified = observer.SourceIdentityVerified,
                liveThreads = observer.Capabilities.Count(x => x.Live), epoch = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(observer.Epoch)))[..12],
                observer.PollCount, observer.TurnReadCount, observer.ReconciledTerminalCount, rows = Rows() };
            observations.Add(value);
            File.WriteAllText(Path.Combine(root, "lifecycle-state.json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
        var initialDeadline = DateTime.UtcNow.AddSeconds(15);
        while ((!controller.Codex.SourceIdentityVerified || controller.Codex.Capabilities.Count(x => x.Live) < 2) && DateTime.UtcNow < initialDeadline) await Task.Delay(100);
        Check(controller.Codex.SourceKind == "desktop-native" && controller.Codex.SourceIdentityVerified, "原桌面进程包身份、用户及会话已核实");
        Check(controller.Codex.Capabilities.Count(x => x.Live) >= 2, "A/B原桌面会话分别可读实时状态");
        Check(Item("A").Lane == Lane.Running && Item("B").Lane == Lane.Running && Item("A").Stages[0].Codex!.Phase == CodexPhase.Unknown,
            "绑定前已结束的历史轮次不误移列");
        await controller.Flush(); Snapshot("ready");
        File.WriteAllText(Path.Combine(root, "lifecycle-ready.json"), "{\"ready\":true}");
        EditorSurface? openEditor = null; var lastCommand = ""; var finished = false;
        while (!finished && clock.Elapsed < TimeSpan.FromMinutes(40))
        {
            await Task.Delay(100);
            var path = Path.Combine(root, "lifecycle-command.json");
            if (File.Exists(path))
            {
                var raw = File.ReadAllText(path);
                if (raw != lastCommand)
                {
                    using var command = JsonDocument.Parse(raw); var value = command.RootElement;
                    var kind = value.GetProperty("kind").GetString();
                    var alias = value.TryGetProperty("alias", out var target) ? target.GetString()! : "A";
                    switch (kind)
                    {
                        case "hide": controller.SetExpanded(false); break;
                        case "show": controller.SetExpanded(true); await Task.Delay(250); break;
                        case "edit":
                            openEditor = controller.EditTask(Item(alias).Id);
                            StageFeatureChecks.Children<TextBox>(openEditor).First().Text = "接入隔离草稿修改";
                            break;
                        case "saveEdit":
                            if (openEditor is null) throw new InvalidOperationException("没有隔离编辑窗口");
                            if (!await openEditor.SaveAndClose()) throw new InvalidOperationException("隔离保存失败");
                            openEditor = null;
                            Check(Item(alias).Title == "接入隔离草稿修改", "实际编辑保存保留用户修改"); break;
                        case "reminder":
                            var reminded = Item(alias); reminded.Reminder.Enabled = true; reminded.Reminder.NextLocal = DateTime.Now.AddSeconds(-1);
                            await (Task)typeof(BoardController).GetMethod("CheckReminders", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null)!;
                            break;
                        case "unbind": case "bind":
                            var session = controller.Engine.Edit(Item(alias).Id);
                            session.Working.Stages[0].Codex = kind == "unbind" ? null : new CodexBinding { ThreadId = alias == "B" ? threadB : threadA, ThreadName = "测试" + alias };
                            controller.Engine.Submit(session); break;
                        case "disconnect":
                            var field = typeof(CodexObserver).GetField("desktop", BindingFlags.Instance | BindingFlags.NonPublic)!;
                            if (field.GetValue(controller.Codex) is not CodexDesktopBridge bridge) throw new InvalidOperationException("并非原桌面来源");
                            await bridge.DisposeAsync(); break;
                        case "checkpoint":
                            controller.SetExpanded(true); await Task.Delay(200); await controller.Flush();
                            var saved = controller.Store.Load();
                            foreach (var expected in value.GetProperty("expect").EnumerateObject())
                            {
                                var item = Item(expected.Name); var state = expected.Value;
                                if (state.TryGetProperty("lane", out var lane)) Check(item.Lane.ToString() == lane.GetString(), expected.Name + "实际任务列符合" + lane.GetString());
                                if (state.TryGetProperty("phase", out var phase)) Check(item.Stages[0].Codex?.Phase.ToString() == phase.GetString(), expected.Name + "实际阶段状态符合" + phase.GetString());
                                if (state.TryGetProperty("awaiting", out var awaiting)) Check(item.Stages[0].Codex?.AwaitingUserAction == awaiting.GetBoolean(), expected.Name + "已确认待处理保护符合预期");
                                if (state.TryGetProperty("dot", out var dot)) Check(item.TimerUnread == dot.GetBoolean(), expected.Name + "提醒红点符合实际执行状态");
                                Check(saved.Tasks.Single(x => x.Id == item.Id).Lane == item.Lane && saved.Tasks.Single(x => x.Id == item.Id).TimerUnread == item.TimerUnread, expected.Name + "实际持久化列及红点一致");
                                if (item.ColumnId is null) Check(controller.Columns.Single(x => x.Definition.Contains(item)).Locate(item.Id) is not null, expected.Name + "实际卡片位于对应列表控件");
                            }
                            foreach (var lane in new[] { Lane.Inspiration, Lane.Refining, Lane.Paused, Lane.Done }) Check(Item("manual" + lane).Lane == lane, "原桌面事件不移动人工" + lane);
                            Check(Item("custom").ColumnId == customColumn.Id && Item("pausedStage").Lane == Lane.Running, "自定义列和未运行阶段不被原桌面事件移列");
                            break;
                        case "finish": finished = true; break;
                        default: throw new InvalidOperationException("未知隔离测试指令");
                    }
                    lastCommand = raw; Snapshot(value.TryGetProperty("label", out var label) ? label.GetString()! : kind!);
                    File.WriteAllText(Path.Combine(root, "lifecycle-command-done.json"), raw);
                }
            }
            if (snapshotVersion == 0 || clock.ElapsedMilliseconds % 500 < 110) Snapshot("poll");
        }
        Check(finished, "隔离桌面流程明确结束");
        await controller.Flush(); Snapshot("finished");
        File.WriteAllText(Path.Combine(root, "codex-lifecycle-report.json"), JsonSerializer.Serialize(new {
            passed = checks.Count, checks, originalDesktop = true, syntheticFixture = false, sourceKind = controller.Codex.SourceKind,
            packageIdentityVerified = controller.Codex.SourceIdentityVerified, actualCardsVerified = true, actualPersistenceVerified = true,
            conversationContentsLogged = false, productionDataChanged = false, observations,
            boundary = "检查仅声明实际到达的状态与检查点；未到达的提问/权限/恢复场景须由交付摘要单列，不以夹具替代。"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
