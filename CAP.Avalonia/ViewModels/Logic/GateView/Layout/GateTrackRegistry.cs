namespace CAP.Avalonia.ViewModels.Logic.GateView.Layout;

/// <summary>
/// Greedy track assignment for the gate view's orthogonal wires: every vertical and
/// horizontal run claims a track, and a later run that would lie on top of an earlier one
/// (same line, overlapping extent) is shifted by one lane step until it is free. Stacked
/// gates — a counter drawn as one tall column — then show their wires side by side
/// instead of drawn over each other.
/// </summary>
internal sealed class GateTrackRegistry
{
    private const int MaxShifts = 24;

    private readonly List<Track> _vertical = new();
    private readonly List<Track> _horizontal = new();

    private readonly record struct Track(double Position, double From, double To);

    /// <summary>First free vertical track at or beyond <paramref name="preferredX"/> (not claimed).</summary>
    public double FindVertical(double preferredX, double y1, double y2, double step, int direction) =>
        Find(_vertical, preferredX, y1, y2, step, direction);

    /// <summary>First free horizontal track at or beyond <paramref name="preferredY"/> (not claimed).</summary>
    public double FindHorizontal(double preferredY, double x1, double x2, double step, int direction) =>
        Find(_horizontal, preferredY, x1, x2, step, direction);

    /// <summary>Claims a free vertical track and returns its x.</summary>
    public double ClaimVertical(double preferredX, double y1, double y2, double step, int direction)
    {
        var x = FindVertical(preferredX, y1, y2, step, direction);
        AddVertical(x, y1, y2);
        return x;
    }

    /// <summary>Records a vertical run.</summary>
    public void AddVertical(double x, double y1, double y2) => _vertical.Add(new Track(x, Math.Min(y1, y2), Math.Max(y1, y2)));

    /// <summary>Records a horizontal run.</summary>
    public void AddHorizontal(double y, double x1, double x2) => _horizontal.Add(new Track(y, Math.Min(x1, x2), Math.Max(x1, x2)));

    private static double Find(List<Track> tracks, double preferred, double a, double b, double step, int direction)
    {
        var from = Math.Min(a, b) - step;
        var to = Math.Max(a, b) + step;
        for (var shift = 0; shift < MaxShifts; shift++)
        {
            var position = preferred + direction * shift * step;
            if (!tracks.Any(t => Math.Abs(t.Position - position) < step * 0.5 && t.From < to && t.To > from))
                return position;
        }
        return preferred + direction * MaxShifts * step;
    }
}
