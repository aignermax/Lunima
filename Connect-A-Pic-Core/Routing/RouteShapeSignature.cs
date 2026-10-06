namespace CAP_Core.Routing;

/// <summary>
/// Shape fingerprint of a routed path: segment count, end points and length. An
/// identical copy matches; a re-route, translation, rotation or edit does not. Used to
/// bind imported drawn polygons to the exact route they describe, so any shape change
/// hides them instead of leaving them stale.
/// </summary>
public readonly record struct RouteShapeSignature(int Count, (double X, double Y) Start, (double X, double Y) End, double Length)
{
    /// <summary>Coordinate tolerance (µm) for "the same route".</summary>
    private const double ToleranceUm = 1e-6;

    /// <summary>The signature of <paramref name="path"/>, or null when it has no segments.</summary>
    public static RouteShapeSignature? Of(RoutedPath? path) =>
        path is null || path.Segments.Count == 0
            ? null
            : new RouteShapeSignature(path.Segments.Count, path.Segments[0].StartPoint,
                path.Segments[^1].EndPoint, path.TotalLengthMicrometers);

    /// <summary>True when <paramref name="path"/> has this shape.</summary>
    public bool Matches(RoutedPath? path) =>
        Of(path) is { } other
        && other.Count == Count
        && Close(other.Start, Start)
        && Close(other.End, End)
        && Math.Abs(other.Length - Length) <= ToleranceUm * Count;

    private static bool Close((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) <= ToleranceUm && Math.Abs(a.Y - b.Y) <= ToleranceUm;
}
