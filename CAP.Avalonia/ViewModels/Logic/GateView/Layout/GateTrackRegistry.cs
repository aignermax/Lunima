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
    private const double BucketSize = 50;

    private readonly Dictionary<long, List<Track>> _vertical = new();
    private readonly Dictionary<long, List<Track>> _horizontal = new();

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
    public void AddVertical(double x, double y1, double y2) => Add(_vertical, new Track(x, Math.Min(y1, y2), Math.Max(y1, y2)));

    /// <summary>Records a horizontal run.</summary>
    public void AddHorizontal(double y, double x1, double x2) => Add(_horizontal, new Track(y, Math.Min(x1, x2), Math.Max(x1, x2)));

    private static void Add(Dictionary<long, List<Track>> tracks, Track track)
    {
        var key = BucketOf(track.Position);
        if (!tracks.TryGetValue(key, out var list))
            tracks[key] = list = new List<Track>();
        list.Add(track);
    }

    private static long BucketOf(double position) => (long)Math.Floor(position / BucketSize);

    /// <summary>True when a recorded run lies within half a lane of <paramref name="position"/> over the extent.</summary>
    private static bool IsTaken(Dictionary<long, List<Track>> tracks, double position, double from, double to, double step)
    {
        for (var key = BucketOf(position - step * 0.5); key <= BucketOf(position + step * 0.5); key++)
        {
            if (tracks.TryGetValue(key, out var list)
                && list.Any(t => Math.Abs(t.Position - position) < step * 0.5 && t.From < to && t.To > from))
                return true;
        }
        return false;
    }

    private static double Find(Dictionary<long, List<Track>> tracks, double preferred, double a, double b, double step, int direction)
    {
        var from = Math.Min(a, b) - step;
        var to = Math.Max(a, b) + step;
        for (var shift = 0; shift < MaxShifts; shift++)
        {
            var position = preferred + direction * shift * step;
            if (!IsTaken(tracks, position, from, to, step))
                return position;
        }
        return preferred + direction * MaxShifts * step;
    }
}
