using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Process;
using CAP_Core.Routing;
using CAP_DataAccess.Persistence;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Converters;
using CAP.Avalonia.ViewModels.Panels.DesignModels;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

/// <summary>Restores connection endpoints, routing settings and cached geometry.</summary>
public sealed class NativeConnectionLoader
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ErrorConsoleService? _errorConsole;
    private readonly NativeLoadDiagnostics _diagnostics;
    /// <summary>Creates connection restoration with an operation diagnostic collector.</summary>
    public NativeConnectionLoader(DesignCanvasViewModel canvas, NativeLoadDiagnostics diagnostics,
        ErrorConsoleService? errorConsole)
    {
        _canvas = canvas;
        _diagnostics = diagnostics;
        _errorConsole = errorConsole;
    }

    /// <summary>
    /// Finds a canvas component by identifier string (preferred) or by index (fallback for old files).
    /// The identifier lookup also searches group children at any nesting depth, because a saved
    /// connection endpoint can reference a non-exposed pin of a component inside a group. For such
    /// nested endpoints <paramref name="ancestorGroupId"/> names the top-level ancestor group and
    /// scopes the search — child identifiers are only unique within one group, not across groups,
    /// and the saved component index is not stable across save/load for mixed designs.
    /// Returns null if the component cannot be found.
    /// </summary>
    private Component? ResolveComponentForLoad(string? componentId, int fallbackIndex, string? ancestorGroupId)
    {
        if (!string.IsNullOrEmpty(componentId))
        {
            var topLevel = _canvas.Components.FirstOrDefault(c => c.Component.Identifier == componentId);
            if (topLevel != null)
                return topLevel.Component;

            // Nested endpoint: search inside the persisted top-level ancestor group first.
            if (!string.IsNullOrEmpty(ancestorGroupId)
                && _canvas.Components.FirstOrDefault(c => c.Component.Identifier == ancestorGroupId)
                        ?.Component is ComponentGroup ancestor)
            {
                var scoped = NativeEndpointMapper.FindComponentByIdentifierRecursive(ancestor, componentId);
                if (scoped != null)
                    return scoped;
            }

            // Unscoped fallback (ancestor missing or saved by an older format).
            foreach (var vm in _canvas.Components)
            {
                if (vm.Component is ComponentGroup group)
                {
                    var nested = NativeEndpointMapper.FindComponentByIdentifierRecursive(group, componentId);
                    if (nested != null)
                        return nested;
                }
            }
            return null;
        }

        if (fallbackIndex >= 0 && fallbackIndex < _canvas.Components.Count)
            return _canvas.Components[fallbackIndex].Component;

        return null;
    }

    /// <summary>
    /// Loads a single connection from saved data.
    /// Prefers identifier-based lookup (StartComponentId/EndComponentId) over index-based
    /// to correctly handle mixed standalone+group designs where load order differs from save order.
    /// Falls back to index-based for old files that predate the identifier fields.
    /// </summary>
    public void LoadConnectionFromData(ConnectionData connData)
    {
        var startComp = ResolveComponentForLoad(
            connData.StartComponentId, connData.StartComponentIndex, connData.StartAncestorGroupId);
        var endComp = ResolveComponentForLoad(
            connData.EndComponentId, connData.EndComponentIndex, connData.EndAncestorGroupId);

        if (startComp == null || endComp == null)
            return;

        var startPin = NativeEndpointMapper.ResolvePin(startComp, connData.StartPinName);
        var endPin = NativeEndpointMapper.ResolvePin(endComp, connData.EndPinName);

        if (startPin == null || endPin == null)
            return;

        // Older PDK exports lost the fiber role and allowed physically impossible
        // planar routes. Preserve the components/ports, but report each refused wire.
        if (!startPin.IsPlanarRoutable || !endPin.IsPlanarRoutable)
        {
            _diagnostics.VerticalPortConnections.Add(
                $"{startComp.Identifier}.{startPin.Name} -> {endComp.Identifier}.{endPin.Name}");
            return;
        }

        var cachedPath = PathSegmentConverter.ToRoutedPath(
            connData.CachedSegments, connData.IsBlockedFallback ?? false,
            connData.IsInvalidGeometry ?? false, connData.IsPlaceholderGeometry);

        // Pin-calibration migration (round-5 review [2]): when a PDK release corrected a
        // component's pin ANGLES (positions unchanged), the saved geometry still touches
        // the pins, so the incremental router would keep it — visibly docking against the
        // port direction and kinking into the component on GDS export. Discard the stale
        // geometry (the post-load pass re-routes) and drop the frozen state with it.
        bool pinCalibrationChanged = false;
        if (cachedPath != null && cachedPath.IsValid)
        {
            var (startOk, endOk) = CachedRouteValidator.CheckPinDirections(startPin, endPin, cachedPath);
            if (!startOk) _diagnostics.PinCalibrationMigrations.Add(startComp.Name);
            if (!endOk) _diagnostics.PinCalibrationMigrations.Add(endComp.Name);
            pinCalibrationChanged = !startOk || !endOk;
            if (pinCalibrationChanged) cachedPath = null;
        }

        WaveguideConnectionViewModel? connVm;

        // Replacement-on-connect (both endpoints drop their existing wire) is intended
        // UX interactively, but during load it quietly discards earlier wires from the
        // file's netlist whenever a pin is wired more than once. Collect what is about
        // to be displaced so the load report can name it — the wires stay dropped.
        foreach (var existing in _canvas.Connections
                     .Where(c => c.Connection.StartPin == startPin || c.Connection.EndPin == startPin
                                 || c.Connection.StartPin == endPin || c.Connection.EndPin == endPin)
                     .ToList())
        {
            var description = DescribeConnectionForLoadReport(existing.Connection);
            if (!_diagnostics.DisplacedConnections.Contains(description))
                _diagnostics.DisplacedConnections.Add(description);
        }

        if (cachedPath != null && cachedPath.IsValid)
        {
            connVm = _canvas.ConnectPinsWithCachedRoute(startPin, endPin, cachedPath);
        }
        else
        {
            connVm = _canvas.ConnectPins(startPin, endPin);
        }

        // Restore lock state
        if (connVm != null && connData.IsLocked == true)
        {
            connVm.Connection.IsLocked = true;
        }

        // Restore the import source-layer tag (route-derived GDS connections); null in
        // files that predate the field — the connection stays untagged, unchanged.
        if (connVm != null)
        {
            connVm.Connection.SourceGdsLayer = connData.SourceGdsLayer;
            connVm.Connection.SourceGdsDataType = connData.SourceGdsDataType;
            // Meander length intent (issue #1008); null in files that predate the field.
            connVm.Connection.TargetLengthMicrometers = connData.TargetLengthMicrometers;
            connVm.Connection.LengthToleranceMicrometers = connData.LengthToleranceMicrometers;
        }

        // Restore routing style / interconnect settings / freeze state (issue #574)
        if (connVm != null)
        {
            RestoreRoutingSettings(connVm.Connection, connData, keepFrozenGeometry: !pinCalibrationChanged);
            // Bound to the restored route: only a still-frozen, unedited route shows them.
            var asDrawn = CAP_DataAccess.Persistence.DTOs.AsDrawnPolygonDto.ToGeometry(connData.AsDrawnPolygons, out int corrupt);
            // A partial skin would silently miss pieces; the fitted centerline is the honest fallback.
            if (asDrawn is not null && corrupt == 0)
                connVm.Connection.AttachAsDrawnGeometry(asDrawn);
            if (corrupt > 0)
                _errorConsole?.LogWarning($"Connection {connVm.Connection.StartPin.ParentComponent.Identifier}.{connVm.Connection.StartPin.Name}: " +
                    $"{corrupt} drawn polygon(s) in the file are damaged and were skipped — the route is drawn from its centerline instead.");
        }
    }

    /// <summary>
    /// One-line description of a connection for the load report, identifying both
    /// endpoint pins by component identifier and pin name.
    /// </summary>
    private static string DescribeConnectionForLoadReport(WaveguideConnection connection) =>
        $"{connection.StartPin.ParentComponent?.Identifier}.{connection.StartPin.Name} ↔ "
        + $"{connection.EndPin.ParentComponent?.Identifier}.{connection.EndPin.Name}";

    /// <summary>
    /// Restores per-connection routing style, width/radius and freeze state from saved data.
    /// <paramref name="keepFrozenGeometry"/> is false when the cached geometry was
    /// discarded by the pin-calibration migration — the freeze flag and the per-bend
    /// overrides describe exactly that stale geometry and must not survive it.
    /// </summary>
    private static void RestoreRoutingSettings(
        WaveguideConnection connection, ConnectionData connData, bool keepFrozenGeometry = true)
    {
        // Legacy styles removed from WaveguideType: "Euler" was drawn as the same generous
        // arc as Bend (migrate to Bend); "Straight" (and any other unknown name) falls back
        // to Auto by simply not parsing — the connection keeps its default Type.
        if (connData.RoutingStyle == "Euler")
            connection.Type = WaveguideType.Bend;
        else if (connData.RoutingStyle != null &&
            Enum.TryParse<WaveguideType>(connData.RoutingStyle, ignoreCase: false, out var style) &&
            Enum.IsDefined(style))
            connection.Type = style;
        if (connData.WidthMicrometers.HasValue)
            connection.WidthMicrometers = connData.WidthMicrometers.Value;
        if (connData.BendRadiusMicrometers.HasValue)
            connection.BendRadiusMicrometers = connData.BendRadiusMicrometers.Value;
        if (keepFrozenGeometry && connData.IsRouteFrozen == true)
            connection.IsRouteFrozen = true;
        if (connData.FacetGapMediumIndex.HasValue)
            connection.FacetGapMediumIndex = connData.FacetGapMediumIndex.Value;
        if (keepFrozenGeometry && connData.BendRadiusOverrides != null)
        {
            foreach (var (bendIndex, radius) in connData.BendRadiusOverrides)
                connection.BendRadiusOverrides[bendIndex] = radius;
        }
        if (keepFrozenGeometry && connData.StraightShiftOffsets != null)
        {
            foreach (var (straightIndex, offset) in connData.StraightShiftOffsets)
                connection.StraightShiftOffsets[straightIndex] = offset;
        }
    }
}
