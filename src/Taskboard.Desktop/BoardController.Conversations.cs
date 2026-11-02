using System;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private Task<CodexThread[]>? conversationLoad;
    private DateTime conversationLoadedAt;
    internal CodexThread[] ConversationFixture { get; set; } = [];
    internal string CodexConnectionHint => Codex.ConnectionHint;
    internal void EnsureCodexConnection() { if (!Isolated && !exiting && !dataTransition) Codex.EnsureConnection(); }
    private void RefreshCodexConnectionHint() { foreach (var column in Columns) column.RefreshCodexConnectionHint(); }
    internal async Task<CodexThread[]> LoadConversations(Func<Task<CodexThread[]>>? load = null)
    {
        if (Isolated && load is null) return ConversationFixture;
        EnsureCodexConnection();
        // Empty/error/cancelled reads must not poison subsequent user retries.
        if (conversationLoad is null || conversationLoad.IsFaulted || conversationLoad.IsCanceled ||
            conversationLoad.IsCompletedSuccessfully && conversationLoad.Result.Length == 0 || DateTime.UtcNow - conversationLoadedAt > TimeSpan.FromSeconds(30))
        { conversationLoadedAt = DateTime.UtcNow; conversationLoad = (load ?? Codex.Catalog)(); }
        return await conversationLoad;
    }
}
