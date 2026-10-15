using System;

namespace Taskboard.Desktop;

// One progress value drives both the aperture and the gradual centre glow.
internal static class ScrollMotion
{
    internal const double DurationMs = 100;
    internal const double CoreWidth = 1;
    internal static double Ease(double progress)
    {
        var t = Math.Clamp(progress, 0, 1); return t * t * (3 - 2 * t);
    }
    internal static double Aperture(double progress) => Math.Clamp(progress, 0, 1);
    internal static double Light(double fraction, bool opening, double initialLight = 0)
    {
        var t = opening ? fraction : 1 - fraction;
        var pulse = Ease(t / .24) * (1 - Ease((t - .24) / .5));
        var carried = initialLight * (1 - Ease(fraction));
        return carried + (1 - carried) * pulse;
    }
    internal static double Progress(double from, double to, double elapsedMs, double durationMs)
        => from + (to - from) * Ease(elapsedMs / Math.Max(1, durationMs));
    internal static string EaseExpression(string t) => $"(({t})*({t})*(3-2*({t})))";
    internal static string LightExpression(bool opening)
    {
        var t = opening ? "motion.Frame" : "(1-motion.Frame)";
        var pulse = $"{EaseExpression($"Clamp({t}/0.24,0,1)")}*(1-{EaseExpression($"Clamp(({t}-0.24)/0.5,0,1)")})";
        var carried = $"(InitialLight*(1-{EaseExpression("motion.Frame")}))";
        return $"{carried}+(1-{carried})*({pulse})";
    }
}
