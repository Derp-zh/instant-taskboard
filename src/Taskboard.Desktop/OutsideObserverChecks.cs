using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class OutsideObserverChecks
{
    internal static async Task Run(BoardController controller, bool rawOnly = false)
    {
        var directory = controller.Store.DirectoryPath;
        var checks = new List<string>();
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        if (rawOnly) controller.DisableLegacyInputForIsolatedCheck();
        var draft = controller.NewTask(Lane.Running); draft.Session.Working.Title = "繁忙界面后仍保留输入";
        var host = draft.Host; await Task.Delay(600);
        File.WriteAllText(Path.Combine(directory, "observer-ready.json"), "{}");
        await Wait("block-ui.signal");
        File.WriteAllText(Path.Combine(directory, "observer-blocking.json"), "{}");
        // Deliberately longer than Windows' maximum low-level hook timeout.
        // Only this explicitly isolated verifier blocks the UI.
        Thread.Sleep(1700);
        await Task.Delay(400);
        if (rawOnly) File.WriteAllText(Path.Combine(directory, "raw-first-diagnostic.json"), JsonSerializer.Serialize(new { expanded=controller.Expanded,input=controller.OutsideInputStatus,foreground=Native.GetForegroundWindow().ToInt64() }));
        Assert(!controller.Expanded, "界面阻塞1700毫秒期间的外部点击仍收起整组");
        controller.SetExpanded(true); await Task.Delay(600);
        Assert(draft.Host == host && host!.IsVisible && draft.Session.Working.Title == "繁忙界面后仍保留输入", "繁忙后展开恢复同一悬浮窗口和输入");
        File.WriteAllText(Path.Combine(directory, "observer-second-ready.json"), "{}");
        await Wait("second-click.signal"); await Task.Delay(600);
        Assert(!controller.Expanded, "繁忙恢复后的下一次外部点击仍有效，监听没有被系统移除");
        controller.SetExpanded(true); await Task.Delay(600);
        Assert(draft.Host == host && host!.IsVisible, "第二次收起恢复继续保持原悬浮窗口");
        if (rawOnly)
        {
            var status = JsonSerializer.SerializeToElement(controller.OutsideInputStatus);
            Assert(status.GetProperty("HookPresses").GetInt32() == 0 && status.GetProperty("RawPresses").GetInt32() >= 2, "关闭普通鼠标钩子后，仅原始输入仍能收起恢复整组");
        }
        File.WriteAllText(Path.Combine(directory, "observer-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, blockedUiMs = 1700, rawOnly, input = controller.OutsideInputStatus, isolated = true }, new JsonSerializerOptions { WriteIndented = true }));
        async Task Wait(string file)
        {
            for (var i = 0; i < 300; i++) { if (File.Exists(Path.Combine(directory, file))) return; await Task.Delay(50); }
            throw new TimeoutException("鼠标验证信号超时：" + file);
        }
    }
}
