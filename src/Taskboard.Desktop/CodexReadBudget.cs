using System;
using System.Collections.Concurrent;

namespace Taskboard.Desktop;

// Survives transport reconnects inside one observer. A failed read consumes
// its slot as well; reconnecting must not turn a 30-second limit into 5 seconds.
internal sealed class CodexReadBudget(TimeSpan interval)
{
    private readonly ConcurrentDictionary<string, long> due = new(StringComparer.Ordinal);
    internal bool IsDue(string threadId, long now) => !due.TryGetValue(threadId, out var next) || now >= next;
    internal bool Reserve(string threadId, long now)
    {
        if (!IsDue(threadId, now)) return false;
        due[threadId] = now + Math.Max(1, (long)interval.TotalMilliseconds); return true;
    }
}
