using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Taskboard.Desktop;

// One barrier for a single workspace restore. Animation durations remain local.
internal sealed class WorkspaceRevealBatch
{
    internal static WorkspaceRevealBatch? Current { get; set; }
    private static long sequence;
    internal long Id { get; } = ++sequence;
    private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer deadline;
    private int participants, ready;
    private bool sealedBatch;
    internal WorkspaceRevealBatch()
    {
        deadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(500) };
        deadline.Tick += (_, _) => Release();
    }
    internal Ticket Enroll() { participants++; return new Ticket(this); }
    internal void Seal() { sealedBatch = true; if (ready == participants) Release(); else deadline.Start(); }
    private void Release() { deadline.Stop(); gate.TrySetResult(); }
    private Task Arrive() { ready++; if (sealedBatch && ready == participants) Release(); return gate.Task; }
    internal sealed class Ticket(WorkspaceRevealBatch batch) : IDisposable
    {
        private bool arrived;
        internal long GroupId => batch.Id;
        internal Task Ready() { if (!arrived) { arrived = true; return batch.Arrive(); } return batch.gate.Task; }
        public void Dispose() { if (!arrived) { arrived = true; _ = batch.Arrive(); } }
    }
}
