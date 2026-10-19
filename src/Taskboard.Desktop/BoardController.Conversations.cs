using System;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private Task<CodexThread[]>? conversationLoad;
    private DateTime conversationLoadedAt;
    internal CodexThread[] ConversationFixture { get; set; } = [];
    internal async Task<CodexThread[]> LoadConversations()
    {
        if (Isolated) return ConversationFixture;
        if (conversationLoad is null || conversationLoad.IsFaulted || DateTime.UtcNow - conversationLoadedAt > TimeSpan.FromSeconds(30))
        { conversationLoadedAt = DateTime.UtcNow; conversationLoad = Codex.Catalog(); }
        return await conversationLoad;
    }
}
