using CAP_Core.Routing;

namespace CAP_Core.Components.Connections
{
    public partial class WaveguideConnection
    {
        /// <summary>
        /// The drawn polygons, the route shape they belong to and a copy of that imported
        /// route — swapped as ONE reference.
        /// </summary>
        private sealed record AsDrawnBinding(AsDrawnGeometry Geometry, RouteShapeSignature? Route, RoutedPath? ImportedRoute);

        private AsDrawnBinding? _asDrawn;

        /// <summary>
        /// True after an endpoint move pushed the imported route aside; cleared when the
        /// route snaps back or the polygons are re-attached. Explicit re-routes never set it.
        /// </summary>
        private bool _drawnRouteDisplacedByMove;

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
            _asDrawn is { } binding
            && IsRouteFrozen
            && !HasManualPathEdits
            && binding.Route is { } route
            && route.Matches(RoutedPath)
                ? binding.Geometry
                : null;

        /// <summary>
        /// Binds imported polygons to the CURRENT <see cref="RoutedPath"/>. Call after
        /// the cached route is in place (see <see cref="RestoreCachedPath"/>). The polygons
        /// and their route binding are published together, so a concurrent reader never
        /// pairs new polygons with an old route or vice versa.
        /// </summary>
        /// <param name="geometry">The polygons, absolute canvas coordinates.</param>
        public void AttachAsDrawnGeometry(AsDrawnGeometry geometry)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            _asDrawn = new AsDrawnBinding(geometry, RouteShapeSignature.Of(RoutedPath), RoutedPath?.DeepCopy());
            _drawnRouteDisplacedByMove = false;
        }

        /// <summary>
        /// Records that an endpoint move is about to unfreeze a route that still showed its
        /// drawn polygons, so <see cref="TryRestoreDrawnRoute"/> can bring it back.
        /// </summary>
        private void MarkDrawnRouteDisplaced()
        {
            if (AsDrawnGeometry is not null)
                _drawnRouteDisplacedByMove = true;
        }

        /// <summary>
        /// Puts the imported route back — frozen, with its drawn polygons — when a move had
        /// displaced it and both pins are back exactly where the route ends (undo of the
        /// move, or moving the component back). Returns true when it did.
        /// </summary>
        private bool TryRestoreDrawnRoute(double wavelengthNm)
        {
            if (!_drawnRouteDisplacedByMove || _asDrawn?.ImportedRoute is not { } imported
                || imported.Segments.Count == 0 || StartPin == null || EndPin == null)
                return false;
            var (startX, startY) = StartPin.GetAbsolutePosition();
            var (endX, endY) = EndPin.GetAbsolutePosition();
            if (Distance(imported.Segments[0].StartPoint.X, imported.Segments[0].StartPoint.Y, startX, startY) > FrozenEndpointToleranceMicrometers
                || Distance(imported.Segments[^1].EndPoint.X, imported.Segments[^1].EndPoint.Y, endX, endY) > FrozenEndpointToleranceMicrometers)
                return false;
            RoutedPath = imported.DeepCopy();
            IsRouteFrozen = true;
            _drawnRouteDisplacedByMove = false;
            UpdateLossFromPath(wavelengthNm);
            return true;
        }

        /// <summary>
        /// The attached polygons regardless of whether they currently describe the route,
        /// for undo snapshots that must restore them together with an earlier route.
        /// </summary>
        public AsDrawnGeometry? AttachedAsDrawnGeometry => _asDrawn?.Geometry;
    }
}
