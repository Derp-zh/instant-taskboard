using System;
using System.Linq;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private DateTime lastArchiveScan;
    private bool checkingArchives;
    internal async Task CheckAutomaticArchives(bool force = false)
    {
        if (checkingArchives || exiting || dataTransition || explicitSave > 0 || humanTaskGate.CurrentCount == 0) return;
        var now = DateTime.UtcNow; if (!force && now - lastArchiveScan < TimeSpan.FromMinutes(1)) return;
        lastArchiveScan = now; checkingArchives = true;
        await humanTaskGate.WaitAsync();
        var observed = Engine;
        TaskCard[] cards = [];
        try
        {
            var due = Engine.DueArchives(now);
            cards = due.SelectMany(id => Columns.Select(x => x.RenderedCard(id)).OfType<TaskCard>()).Distinct().ToArray();
            await Task.WhenAll(cards.Select(x => x.CollapseTask()));
            if (exiting || dataTransition || !ReferenceEquals(Engine, observed)) return;
            var stillDue = Engine.DueArchives(DateTime.UtcNow).ToHashSet();
            foreach (var id in due.Where(stillDue.Contains)) Engine.Archive(id);
            var removed = Engine.TrimArchiveHistory();
            if (due.Length > 0 || removed > 0) { settings?.RefreshArchive(); await Flush(); }
        }
        catch (Exception e) { if (!exiting) Report("自动归档未完成，任务仍按当前保存结果保留：" + e.Message); }
        finally { foreach (var card in cards) card.RestoreAfterRemoval(); checkingArchives = false; humanTaskGate.Release(); }
    }
}
