using System;

namespace Taskboard.Desktop;

internal static class DraftSwitchMotion
{
    internal const double BaseDurationMs = 360;
#if FAST_DRAFT_SWITCH
    internal const double Speed = 1.35;
#else
    internal const double Speed = 1;
#endif
    internal const double DurationMs = BaseDurationMs / Speed;
    internal static string VariantLabel => Speed > 1 ? "暂存切换1.35倍 · 约267毫秒" : "暂存切换原速 · 360毫秒";
    internal const double Lift = 0;
    internal static double Padding(double pane) => pane + 12;
    internal static (double OldX, double NewX, double NewY) Pose(double progress, double pane, double selectedSlot)
    {
        var t = Math.Clamp(progress, 0, 1); var apart = Padding(pane);
        // 0–150ms: pull the selected card right; the current card waits.
        // 150–250ms: the current card slides right into its rear slot.
        // 190–360ms: bring the selected card left over the current card.
        // Normalized phase boundaries keep paths/layers identical in both builds.
        var oldX = pane / 5 * Ease(Math.Clamp((t - 150 / BaseDurationMs) / (100 / BaseDurationMs), 0, 1));
        var newX = t <= 150 / BaseDurationMs
            ? selectedSlot + (apart - selectedSlot) * Ease(t / (150 / BaseDurationMs))
            : apart * (1 - Ease(Math.Clamp((t - 190 / BaseDurationMs) / (170 / BaseDurationMs), 0, 1)));
        return (oldX, newX, 0);
    }
    private static double Ease(double t) => t * t * (3 - 2 * t);
}
