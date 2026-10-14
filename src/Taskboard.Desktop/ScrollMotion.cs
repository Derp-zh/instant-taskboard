using System;

namespace Taskboard.Desktop;

// One reversible timeline: flash at the centre, then reveal the unchanged UI.
internal static class ScrollMotion
{
    internal const double DurationMs = 100;
    internal const double FlashEnd = .25;
    internal static double Aperture(double progress)
    {
        var t = Math.Clamp((progress - FlashEnd) / (1 - FlashEnd), 0, 1);
        return t * t * (3 - 2 * t);
    }
    internal static double Light(double progress)
    {
        var t = Math.Clamp(progress / FlashEnd, 0, 1);
        return Math.Min(1, Math.Min(t / .2, (1 - t) / .2));
    }
    internal static double Progress(double from, double to, double elapsedMs, double durationMs)
        => from + (to - from) * Math.Clamp(elapsedMs / Math.Max(1, durationMs), 0, 1);
}
