using CAP_Core.Routing;

namespace CAP_Core.Components.Connections
{
    public partial class WaveguideConnection
    {
        /// <summary>Coordinate tolerance (µm) for "the route is still the imported one".</summary>
        private const double AsDrawnRouteToleranceUm = 1e-6;

        private AsDrawnGeometry? _asDrawnGeometry;
        private RouteSignature? _asDrawnRoute;

        /// <summary>
        /// The polygons this connection was drawn with in an imported layout, or null.
        /// They describe the connection only while its route is exactly the imported
        /// one: frozen, unedited, and geometrically the route they were attached to
        /// (routing passes may swap in an identical copy — that keeps them). Any
        /// re-route, unfreeze or manual bend/shift edit therefore hides them
        /// automatically and the fitted centerline (<see cref="RoutedPath"/>) becomes
        /// the drawn and exported geometry — there is no second copy to keep in sync.
        /// </summary>
        public AsDrawnGeometry? AsDrawnGeometry =>
            _asDrawnGeometry is not null
            && IsRouteFrozen
            && !HasManualPathEdits
            && _asDrawnRoute is { } bound
            && bound.Matches(RoutedPath)
                ? _asDrawnGeometry
                : null;

        /// <summary>
        /// Binds imported polygons to the CURRENT <see cref="RoutedPath"/>. Call after
        /// the cached route is in place (see <see cref="RestoreCachedPath"/>).
        /// </summary>
        /// <param name="geometry">The polygons, absolute canvas coordinates.</param>
        public void AttachAsDrawnGeometry(AsDrawnGeometry geometry)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            _asDrawnGeometry = geometry;
            _asDrawnRoute = RouteSignature.Of(RoutedPath);
        }

        /// <summary>
        /// Shape fingerprint of a route: segment count, end points and length. A copy of
        /// the same route matches; a re-route, a translation or a different style does not.
        /// </summary>
        private readonly record struct RouteSignature(int Count, (double X, double Y) Start, (double X, double Y) End, double Length)
        {
            public static RouteSignature? Of(RoutedPath? path) =>
                path is null || path.Segments.Count == 0
                    ? null
                    : new RouteSignature(path.Segments.Count, path.Segments[0].StartPoint,
                        path.Segments[^1].EndPoint, path.TotalLengthMicrometers);

            public bool Matches(RoutedPath? path) =>
                Of(path) is { } other
                && other.Count == Count
                && Close(other.Start, Start)
                && Close(other.End, End)
                && Math.Abs(other.Length - Length) <= AsDrawnRouteToleranceUm * Count;

            private static bool Close((double X, double Y) a, (double X, double Y) b) =>
                Math.Abs(a.X - b.X) <= AsDrawnRouteToleranceUm && Math.Abs(a.Y - b.Y) <= AsDrawnRouteToleranceUm;
        }
    }
}
