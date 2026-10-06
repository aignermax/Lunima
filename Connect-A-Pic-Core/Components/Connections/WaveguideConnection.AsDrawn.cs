using CAP_Core.Routing;

namespace CAP_Core.Components.Connections
{
    public partial class WaveguideConnection
    {
        private AsDrawnGeometry? _asDrawnGeometry;
        private RoutedPath? _asDrawnRoute;

        /// <summary>
        /// The polygons this connection was drawn with in an imported layout, or null.
        /// They describe the connection only while its route is exactly the imported
        /// one: frozen, unedited, and the very path instance they were attached to. Any
        /// re-route, unfreeze or manual bend/shift edit therefore hides them
        /// automatically and the fitted centerline (<see cref="RoutedPath"/>) becomes
        /// the drawn and exported geometry — there is no second copy to keep in sync.
        /// </summary>
        public AsDrawnGeometry? AsDrawnGeometry =>
            _asDrawnGeometry is not null
            && IsRouteFrozen
            && !HasManualPathEdits
            && ReferenceEquals(_asDrawnRoute, RoutedPath)
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
            _asDrawnRoute = RoutedPath;
        }
    }
}
