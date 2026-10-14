namespace Taskboard.Core;

public static class ReminderClock
{
    public static DateTime Next(Reminder reminder, DateTime after)
    {
        if (reminder.Repeat == Repeat.Once) return reminder.NextLocal;
        var candidate = after.Date.Add(reminder.Time);
        if (reminder.Repeat == Repeat.Daily) return candidate > after ? candidate : candidate.AddDays(1);
        var days = ((int)reminder.Weekday - (int)candidate.DayOfWeek + 7) % 7;
        candidate = candidate.AddDays(days);
        return candidate > after ? candidate : candidate.AddDays(7);
    }

    public static IReadOnlyList<Guid> Tick(BoardState state, DateTime now)
    {
        List<Guid> due = [];
        foreach (var task in state.Tasks)
        {
            var r = task.Reminder;
            if (!r.Enabled || task.Lane == Lane.Done || r.NextLocal > now) continue;
            var occurrence = r.NextLocal;
            if (r.LastOccurrence != occurrence)
            {
                r.LastOccurrence = occurrence;
                if (!task.PendingReasons.Contains("定时提醒到期")) task.PendingReasons.Add("定时提醒到期");
                task.Lane = Lane.Action;
                task.ColumnId = null; task.TimerUnread = true;
                task.UpdatedUtc = DateTime.UtcNow;
                due.Add(task.Id);
            }
            if (r.Repeat == Repeat.Once) r.Enabled = false;
            else r.NextLocal = Next(r, now); // one catch-up notification, no replay of every missed occurrence
        }
        return due;
    }
}
