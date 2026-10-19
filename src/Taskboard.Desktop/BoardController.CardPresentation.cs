using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private readonly HashSet<(Guid Task, Guid Stage)> retainedStages = [];
    private readonly SemaphoreSlim humanTaskGate = new(1);
    private Task? stageCleanup;
    internal bool VerifyCardAnimations { get; set; }
    internal bool CardAnimationsEnabled => !Smoke || VerifyCardAnimations;
    internal IReadOnlyList<Stage> VisibleCardStages(WorkItem item) => item.Stages.Count == 1 ? item.Stages : item.Stages.Where(x => x.IsRunning == true || retainedStages.Contains((item.Id, x.Id))).ToArray();
    internal bool SetStageRunningByUser(Guid task, Guid stage, bool running)
    {
        var item = Engine.State.Tasks.First(x => x.Id == task);
        if (!BoardEngine.CanSetStageRunning(item, stage, running)) return false;
        if (!running) retainedStages.Add((task, stage)); else retainedStages.Remove((task, stage));
        return Engine.SetStageRunning(task, stage, running);
    }
    internal Task CleanupRetainedStages(Guid? removingTask = null)
    {
        if (stageCleanup is { IsCompleted: false }) return stageCleanup;
        if (retainedStages.Count == 0) return Task.CompletedTask;
        return stageCleanup = Cleanup();
        async Task Cleanup()
        {
            var snapshot = retainedStages.ToArray();
            if (Expanded && CardAnimationsEnabled)
                for (var i = 0; i < 75 && VisibleColumns.Any(x => x.IsScrollAnimating || x.IsContentSwapHandoff); i++) await Task.Delay(10);
            var animations = snapshot.Where(x => x.Task != removingTask).GroupBy(x => x.Task).SelectMany(group =>
                Columns.Select(column => column.RenderedCard(group.Key)).OfType<TaskCard>().Select(card => card.CollapseStages(group.Select(x => x.Stage).ToArray()))).ToArray();
            await Task.WhenAll(animations);
            foreach (var key in snapshot) retainedStages.Remove(key);
            foreach (var column in Columns) column.RefreshCards(snapshot.Select(x => x.Task).Distinct().ToArray());
        }
    }
    private async Task RunHumanTask(Action action, Guid? removingTask = null)
    {
        await humanTaskGate.WaitAsync(); var cards = removingTask is Guid id ? Columns.Select(x => x.RenderedCard(id)).OfType<TaskCard>().ToArray() : [];
        try
        {
            await Task.WhenAll(cards.Select(x => x.CollapseTask()).Append(CleanupRetainedStages(removingTask)));
            action();
        }
        catch (Exception e) { if (!exiting) Ui.Error("操作未完成：" + e.Message); }
        finally { foreach (var card in cards) card.RestoreAfterRemoval(); humanTaskGate.Release(); }
    }
}
