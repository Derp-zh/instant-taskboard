using System;

namespace Taskboard.Desktop;

internal static class DraftSwitchMotion
{
    internal const double DurationMs = 200;
    internal const double Lift = 12;
    internal static double Padding(double pane) => pane / 2 + 6;
    internal static (double OldX, double NewX, double NewY) Pose(double progress, double pane, double selectedSlot)
    {
        var t = Math.Clamp(progress, 0, 1); var apart = Padding(pane);
        if (t <= .45) { var q = Ease(t / .45); return (-apart * q, selectedSlot + (apart - selectedSlot) * q, 0); }
        if (t <= .6) return (-apart, apart, -Lift * Ease((t - .45) / .15));
        var back = Ease((t - .6) / .4);
        return (-apart + (pane / 5 + apart) * back, apart * (1 - back), -Lift * (1 - back));
    }
    private static double Ease(double t) => t * t * (3 - 2 * t);
}
