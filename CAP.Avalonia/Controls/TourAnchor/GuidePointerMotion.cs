using Avalonia;

namespace CAP.Avalonia.Controls.TourAnchor;

/// <summary>A decorative pointer pose. It never represents real input.</summary>
public readonly record struct GuidePointerPose(Point Position, double ClickOpacity, double ClickRadius);

/// <summary>Deterministic demonstration timing, independent of the UI clock.</summary>
public static class GuidePointerMotion
{
    public static GuidePointerPose SampleConnection(Point start, Point end, double elapsedSeconds, bool reducedMotion)
    {
        if (reducedMotion) return Sample(start, 0, true);
        var phase = Math.Max(0, elapsedSeconds) % 3.6;
        if (phase < 1.35) return Sample(start, phase, false);
        var progress = Math.Clamp((phase - 1.35) / 1.15, 0, 1);
        var eased = progress * progress * (3 - 2 * progress);
        var position = new Point(start.X + (end.X - start.X) * eased, start.Y + (end.Y - start.Y) * eased);
        var release = Math.Clamp((phase - 2.6) / .5, 0, 1);
        return new GuidePointerPose(position, phase < 2.6 ? .65 : 1 - release, 6 + 18 * release);
    }

    public static GuidePointerPose Sample(Point target, double elapsedSeconds, bool reducedMotion)
    {
        if (reducedMotion)
            return new GuidePointerPose(target, .65, 10);

        var phase = Math.Max(0, elapsedSeconds) % 3.6;
        var progress = Math.Clamp(phase / .9, 0, 1);
        var remaining = Math.Pow(1 - progress, 3);
        var position = target + new Point(64 * remaining, 42 * remaining);
        var clickProgress = Math.Clamp((phase - 1) / .55, 0, 1);
        var opacity = phase is >= 1 and < 1.55 ? 1 - clickProgress : 0;
        return new GuidePointerPose(position, opacity, 6 + 18 * clickProgress);
    }
}
