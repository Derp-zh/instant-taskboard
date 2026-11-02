using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    // Preview seeds and diagnostics are test-only. Drag binding itself is an
    // ordinary card action and has no preview or integration-mode gate.
    internal bool ConversationPreviewMode { get; set; }
    internal ConversationBindingDrag? ConversationDrag { get; set; }
    internal async Task PrepareConversationPreview()
    {
        if (!ConversationPreviewMode || !Isolated || Smoke) return;
        tray.Text = "即时任务看板 · 拖动绑定独立测试";
        Ball.ToolTip = "拖动绑定独立测试 · 数据保存在测试包内";
        if (Engine.State.Tasks.Count == 0)
        {
            var sample = Engine.NewSession(Lane.Refining); sample.Working.Title = "拖动绑定测试（独立数据）";
            sample.Working.Stages[0].Text = "按住右侧“未绑定”拖出本列表，移到 Codex 侧栏会话或聊天区域，松开直接绑定。";
            var second = Engine.AddStage(sample); second.IsRunning = true;
            second.Text = "第二个独立阶段。Esc 或鼠标右键取消，原绑定保留。";
            Engine.Submit(sample); await Flush();
        }
        foreach (var column in Columns) column.Repaint();
        SetExpanded(true);
    }
    internal async Task<string> CompleteConversationBinding(Guid task, Guid stage, Guid? expected, ConversationDropTarget target, object? expectedState = null)
    {
        WriteDropDiagnostic(task, stage, target, false);
        if (target.ThreadId is null) return "未绑定：" + target.Reason;
        var state = Engine.State;
        await humanTaskGate.WaitAsync();
        try
        {
            if (exiting || dataTransition || !ReferenceEquals(state, Engine.State) || expectedState is not null && !ReferenceEquals(expectedState, state))
                return "未绑定：任务数据已变化，请重新拖动";
            await CleanupRetainedStages();
            if (!Engine.BindStageConversation(task, stage, expected, target.ThreadId, target.Name))
                return "未绑定：任务、阶段或原绑定已变化，请重新拖动";
            var saved = await Flush();
            WriteDropDiagnostic(task, stage, target, saved);
            var result = saved ? "已绑定 · " + target.Name : "绑定仍在内存中，保存失败，请重试保存";
            return target.ClipboardRestored == false ? result + "；剪贴板未恢复，已有新内容时不会覆盖" : result;
        }
        finally { humanTaskGate.Release(); }
    }
    private void WriteDropDiagnostic(Guid task, Guid stage, ConversationDropTarget target, bool saved)
    {
        if (!ConversationPreviewMode || !Isolated) return;
        try
        {
            File.WriteAllText(Path.Combine(Store.DirectoryPath, "last-conversation-drop.json"), JsonSerializer.Serialize(new
            { task, stage, target.ThreadId, target.Name, target.Method, target.Reason, target.ClipboardRestored, saved, menuStep = CodexConversationMenu.LastStep }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { /* A test receipt cannot invalidate or conceal a completed save. */ }
    }
}
