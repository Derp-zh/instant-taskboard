using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
namespace Taskboard.Desktop;
internal static class CodexChecks
{
    internal static async Task<bool> Run(string directory)
    {
        var blocked = new[] { "thread/start", "thread/resume", "turn/start", "turn/interrupt", "create_thread", "send_message_to_thread", "request/answer", "request/approve" };
        if (blocked.Any(CodexRpc.Allowed) || blocked.Any(CodexDesktopBridge.Allowed)) throw new InvalidOperationException("观察器允许了写入");
        var executable = CodexObserver.FindExecutable(); await using var observer = new CodexObserver(); observer.Start(executable,"");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while ((observer.Status.StartsWith("正在") || observer.Status.StartsWith("尚未")) && DateTime.UtcNow<deadline) await Task.Delay(100);
        var threads = await observer.Catalog();
        var directoryPassed = threads.Length > 0 && threads.All(x => x.Id.Length > 0);
        Directory.CreateDirectory(directory);
        // Catalog availability cannot establish lifecycle or actual board movement.
        File.WriteAllText(Path.Combine(directory,"codex-report.json"),JsonSerializer.Serialize(new {
            schemaVersion = 2, passed = false, acceptanceScope = "directory_and_readonly_transport_only",
            directoryCheck = new { passed = directoryPassed, existingThreadsRead = threads.Length },
            sourceCheck = new { observer.TransportConnected, observer.SourceKind, observer.SourceIdentityVerified,
                perSessionChecked = observer.Capabilities.Length, liveSessions = observer.Capabilities.Count(x => x.Live) },
            desktopLifecycleCheck = new { verified = false, questions = false, approvals = false, recovery = false, completion = false },
            boardMovementCheck = new { verified = false, actualCards = false, persistedState = false },
            fullIntegrationPassed = false, diagnosticExitCode = directoryPassed ? 3 : 1,
            existingThreadsRead = threads.Length, status = observer.Status,
            liveTransportConnected = observer.TransportConnected && observer.SourceIdentityVerified,
            metadataOnly = observer.SourceKind == "historical", mutationMethodsBlocked = true,
            conversationContentsLogged = false, identifiersLogged = false,
            boundary = "目录可读或桌面通道可用不等于逐会话实时可用，更不等于原桌面生命周期及实际移列通过；退出码3表示部分诊断完成、完整接入未验收。"
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!directoryPassed) throw new InvalidOperationException("会话目录检查未通过");
        return false;
    }
}
