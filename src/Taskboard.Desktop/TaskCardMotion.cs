using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace Taskboard.Desktop;

internal sealed partial class TaskCard
{
    internal const int CollapseDurationMs = 180;
    private readonly Dictionary<Guid, FrameworkElement> stageSections = new();
    private int collapseScopes;
    private Thickness? beforeRemovalMargin;
    private long wholeMotion;
    internal int TaskExpandCount { get; private set; }
    internal int StageCollapseCount { get; private set; }
    internal int TaskCollapseCount { get; private set; }
    internal Task CollapseStages(IReadOnlyCollection<Guid> ids)
    {
        var targets = ids.Where(stageSections.ContainsKey).Select(x => stageSections[x]).ToArray();
        if (targets.Length == 0 || !Controller.CardAnimationsEnabled || !IsVisible) return Task.CompletedTask;
        StageCollapseCount += targets.Length; return Collapse(targets, false);
    }
    internal Task CollapseTask()
    {
        if (!Controller.CardAnimationsEnabled || !IsVisible || ActualHeight <= 0) return Task.CompletedTask;
        TaskCollapseCount++; beforeRemovalMargin ??= Margin; return Collapse([this], true);
    }
    private async Task Collapse(FrameworkElement[] targets, bool whole)
    {
        var generation = whole ? ++wholeMotion : 0;
        collapseScopes++;
        try
        {
            var completions = new List<Task>();
            foreach (var target in targets)
            {
                target.ClipToBounds = true; target.IsHitTestVisible = false;
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var animation = new DoubleAnimation(target.ActualHeight, 0, TimeSpan.FromMilliseconds(CollapseDurationMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                animation.Completed += (_, _) => done.TrySetResult(); target.BeginAnimation(HeightProperty, animation); completions.Add(done.Task);
                if (whole) target.BeginAnimation(MarginProperty, new ThicknessAnimation(target.Margin, new Thickness(target.Margin.Left, 0, target.Margin.Right, 0), TimeSpan.FromMilliseconds(CollapseDurationMs)));
            }
            await Task.WhenAny(Task.WhenAll(completions), Task.Delay(CollapseDurationMs + 100));
            if (!whole || generation == wholeMotion)
                foreach (var target in targets) { target.Height = 0; target.BeginAnimation(HeightProperty, null); }
        }
        finally { collapseScopes--; }
    }
    internal void RestoreAfterRemoval()
    {
        wholeMotion++;
        if (beforeRemovalMargin is not Thickness original) return;
        beforeRemovalMargin = null; BeginAnimation(MarginProperty, null); Margin = original;
        BeginAnimation(HeightProperty, null); Height = double.NaN; IsHitTestVisible = true; ClipToBounds = false; Rebuild();
    }
    internal async Task ExpandTask()
    {
        if (!Controller.CardAnimationsEnabled || !IsVisible || ActualHeight <= 0) return;
        RestoreAfterRemoval();
        var generation = ++wholeMotion; var height = ActualHeight; var margin = Margin;
        beforeRemovalMargin = margin; TaskExpandCount++;
        ClipToBounds = true; IsHitTestVisible = false; Height = 0;
        Margin = new Thickness(margin.Left, 0, margin.Right, 0);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = new DoubleAnimation(0, height, TimeSpan.FromMilliseconds(CollapseDurationMs))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        animation.Completed += (_, _) => done.TrySetResult();
        BeginAnimation(HeightProperty, animation);
        BeginAnimation(MarginProperty, new ThicknessAnimation(Margin, margin, TimeSpan.FromMilliseconds(CollapseDurationMs)));
        await Task.WhenAny(done.Task, Task.Delay(CollapseDurationMs + 100));
        if (generation == wholeMotion) RestoreAfterRemoval();
    }
}
