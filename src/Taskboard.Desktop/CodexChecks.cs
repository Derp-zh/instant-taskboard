using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
namespace Taskboard.Desktop;
internal static class CodexChecks
{
    internal static async Task Run(string directory)
    {
        if (new[]{"thread/start","thread/resume","turn/start","turn/interrupt"}.Any(CodexRpc.Allowed)) throw new InvalidOperationException("观察器允许了写入");
        var executable = CodexObserver.FindExecutable(); await using var observer = new CodexObserver(); observer.Start(executable,"");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while ((observer.Status.StartsWith("正在") || observer.Status.StartsWith("尚未")) && DateTime.UtcNow<deadline) await Task.Delay(100);
        var threads = await observer.Catalog();
        if (threads.Length==0 || threads.Any(x => x.Id.Length==0)) throw new InvalidOperationException("未读到准确会话目录");
        // Do not put chat titles, content or identifiers into test artifacts.
        File.WriteAllText(Path.Combine(directory,"codex-report.json"),JsonSerializer.Serialize(new { passed=true,existingThreadsRead=threads.Length,status=observer.Status,
            liveTransportConnected=observer.Authoritative,metadataOnly=!observer.Authoritative,mutationMethodsBlocked=true,conversationContentsLogged=false }));
    }
}
