using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    // Presentation only. The domain mutation and normal save notification happen
    // immediately; no Codex message waits on a decorative animation.
    private sealed class AutomaticMove(Guid id, Lane destination, ColumnSurface source, ColumnSurface target, TaskCard? card)
    {
        internal readonly Guid Id = id;
        internal readonly Lane Destination = destination;
        internal readonly ColumnSurface Source = source, Target = target;
        internal readonly TaskCard? Card = card;
        internal Task Completion = Task.CompletedTask;
    }
    private readonly Dictionary<Guid, AutomaticMove> automaticMoves = new();
    internal int AutomaticExitCount { get; private set; }
    internal int AutomaticEntryCount { get; private set; }
    internal Task PendingAutomaticMoves => Task.WhenAll(automaticMoves.Values.Select(x => x.Completion));
    private void CancelAutomaticMove(AutomaticMove move)
    {
        automaticMoves.Remove(move.Id);
        move.Card?.RestoreAfterRemoval();
        move.Source.ReleaseAutomaticMove(move.Id); move.Target.ReleaseAutomaticMove(move.Id);
        move.Source.Refresh(); move.Target.Refresh();
    }
    private void CancelAutomaticMoves()
    { foreach (var move in automaticMoves.Values.ToArray()) CancelAutomaticMove(move); }
    private void ReconcileAutomaticMoves()
    {
        foreach (var move in automaticMoves.Values.ToArray())
        {
            var item = Engine.State.Tasks.FirstOrDefault(x => x.Id == move.Id);
            if (item is null || item.ColumnId is not null || item.Lane != move.Destination || move.Source.Definition.Deleted || move.Target.Definition.Deleted)
                CancelAutomaticMove(move);
        }
    }
    private AutomaticMove? PrepareAutomaticMove(WorkItem task, Lane previous)
    {
        if (automaticMoves.TryGetValue(task.Id, out var old) && task.Lane != old.Destination) CancelAutomaticMove(old);
        AutomaticMove? move = null;
        if (previous != task.Lane && previous is Lane.Running or Lane.Action && task.Lane is Lane.Running or Lane.Action &&
            task.ColumnId is null && Expanded && CardAnimationsEnabled && CardDrag is null)
        {
            var source = Columns.FirstOrDefault(x => x.Definition.BuiltinLane == previous && x.IsVisible);
            var target = Columns.FirstOrDefault(x => x.Definition.BuiltinLane == task.Lane && x.IsVisible);
            if (source is not null && target is not null)
            {
                var card = source.RenderedCard(task.Id);
                move = new AutomaticMove(task.Id, task.Lane, source, target, card);
                automaticMoves[task.Id] = move;
                if (card is not null) source.HoldAutomaticExit(task);
                target.HoldAutomaticEntry(task.Id);
            }
        }
        return move;
    }
    internal void NotifyCodexChange(WorkItem task, Lane previous)
    {
        var move = PrepareAutomaticMove(task, previous);
        if (previous != task.Lane) { Engine.State.Tasks.Remove(task); Engine.State.Tasks.Add(task); }
        task.UpdatedUtc = DateTime.UtcNow; Engine.Notify([task.Id]);
        if (move is not null) move.Completion = Animate(move);
    }
    internal IReadOnlyList<Guid> TickReminders(DateTime now)
    {
        var previous = Engine.State.Tasks.ToDictionary(x => x.Id, x => x.Lane);
        var due = ReminderClock.Tick(Engine.State, now);
        if (due.Count == 0) return due;
        var moves = due.Select(id => Engine.State.Tasks.First(x => x.Id == id))
            .Select(task => PrepareAutomaticMove(task, previous[task.Id])).OfType<AutomaticMove>().ToArray();
        // Preserve the reminder's original ordering and single save notification.
        Engine.Notify(due);
        foreach (var move in moves) move.Completion = Animate(move);
        return due;
    }
    private async Task Animate(AutomaticMove move)
    {
        bool Current() => !exiting && automaticMoves.TryGetValue(move.Id, out var current) && ReferenceEquals(current, move);
        try
        {
            if (move.Card is not null) { AutomaticExitCount++; await move.Card.CollapseTask(); }
            if (!Current()) return;
            move.Source.ReleaseAutomaticMove(move.Id); move.Target.ReleaseAutomaticMove(move.Id);
            move.Source.Refresh();
            var entering = move.Target.PrepareAutomaticEntry(move.Id);
            if (Expanded && entering is not null)
            { AutomaticEntryCount++; await entering.ExpandTask(); }
        }
        catch (Exception error) { if (!exiting) Report("任务已更新，移列动画未完成：" + error.Message); }
        finally
        {
            if (Current()) CancelAutomaticMove(move);
        }
    }
}
