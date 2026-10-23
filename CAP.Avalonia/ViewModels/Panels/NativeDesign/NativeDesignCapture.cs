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

/// <summary>Captures native design topology independently from dialogs and file persistence.</summary>
public sealed class NativeDesignCapture
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly NativeComponentMapper _componentMapper;
    private readonly NativeGroupCapture _groups;
    /// <summary>Creates native topology capture from canvas presentation state.</summary>
    public NativeDesignCapture(DesignCanvasViewModel canvas, NativeComponentMapper components,
        NativeGroupCapture groups)
    {
        _canvas = canvas;
        _componentMapper = components;
        _groups = groups;
    }
    /// <summary>Captures components, groups, connections and frozen geometry without file I/O.</summary>
    public (DesignFileData Data, List<string> UnresolvedPins) Capture(
        Func<ComponentGroup, ActiveProcessData?> resolveBinding)
    {
        // Identify which components are groups vs standalone
        var componentsList = _canvas.Groups.RootComponents.ToList();
        var groupComponents = componentsList
            .Where(c => c.Component is ComponentGroup)
            .ToList();
        var childComponentIds = CAP_Core.Analysis.Hierarchy.DesignHierarchy
            .From(groupComponents.Select(view => view.Component)).Leaves.Select(child => child.Id).ToHashSet();

        var components = componentsList.Select(vm => vm.Component).ToList();
        var unresolvedConnectionPins = new List<string>();
        var designData = new DesignFileData
        {
            // Only save non-group, non-child components in the main list
            Components = componentsList
                .Where(c => c.Component is not ComponentGroup
                            && !childComponentIds.Contains(c.Component.Id))
                .Select(c => _componentMapper.CreateComponentData(c))
                .ToList(),
            Connections = _canvas.Groups.RootConnections.Select(c =>
            {
                var (startIdx, startPinName, startId, startAncestor) =
                    NativeEndpointMapper.ResolveConnectionEndpointWithOwner(components, c.Connection.StartPin);
                var (endIdx, endPinName, endId, endAncestor) =
                    NativeEndpointMapper.ResolveConnectionEndpointWithOwner(components, c.Connection.EndPin);
                if (startId == null)
                    unresolvedConnectionPins.Add(c.Connection.StartPin.Name);
                if (endId == null)
                    unresolvedConnectionPins.Add(c.Connection.EndPin.Name);
                return new ConnectionData
                {
                    StartComponentIndex = startIdx,
                    StartPinName = startPinName,
                    EndComponentIndex = endIdx,
                    EndPinName = endPinName,
                    StartComponentId = startId,
                    EndComponentId = endId,
                    StartAncestorGroupId = startAncestor,
                    EndAncestorGroupId = endAncestor,
                    CachedSegments = c.Connection.RoutedPath != null
                        ? PathSegmentConverter.ToDtoList(c.Connection.RoutedPath.Segments)
                        : null,
                    IsBlockedFallback = c.Connection.IsBlockedFallback ? true : null,
                    IsInvalidGeometry = c.Connection.RoutedPath?.IsInvalidGeometry == true ? true : null,
                    // Written as an explicit true/false whenever a route exists (never
                    // omitted for false) so a reload can tell "explicitly false" apart
                    // from "file predates this field" — see ConnectionData's doc comment.
                    IsPlaceholderGeometry = c.Connection.RoutedPath?.IsPlaceholderGeometry,
                    IsLocked = c.Connection.IsLocked ? true : null,
                    RoutingStyle = c.Connection.Type != WaveguideType.Auto ? c.Connection.Type.ToString() : null,
                    WidthMicrometers = c.Connection.WidthMicrometers,
                    BendRadiusMicrometers = c.Connection.BendRadiusMicrometers,
                    IsRouteFrozen = c.Connection.IsRouteFrozen ? true : null,
                    FacetGapMediumIndex = c.Connection.FacetGapMediumIndex > WaveguideConnection.AirRefractiveIndex
                        ? c.Connection.FacetGapMediumIndex
                        : null,
                    BendRadiusOverrides = c.Connection.BendRadiusOverrides.Count > 0
                        ? new Dictionary<int, double>(c.Connection.BendRadiusOverrides)
                        : null,
                    StraightShiftOffsets = c.Connection.StraightShiftOffsets.Count > 0
                        ? new Dictionary<int, double>(c.Connection.StraightShiftOffsets)
                        : null,
                    SourceGdsLayer = c.Connection.SourceGdsLayer,
                    SourceGdsDataType = c.Connection.SourceGdsDataType,
                    TargetLengthMicrometers = c.Connection.TargetLengthMicrometers,
                    LengthToleranceMicrometers = c.Connection.LengthToleranceMicrometers,
                    AsDrawnPolygons = CAP_DataAccess.Persistence.DTOs.AsDrawnPolygonDto.FromGeometry(c.Connection.AsDrawnGeometry),
                };
            }).ToList()
        };

        if (groupComponents.Count > 0)
            designData.Groups = _groups.Capture(groupComponents, resolveBinding);

        // Canvas-level pin-less frozen paths (issue #856) survive save/load.
        if (_canvas.CanvasFrozenPaths.Count > 0)
        {
            designData.CanvasFrozenPaths = _canvas.CanvasFrozenPaths
                .Select(p => CAP_DataAccess.Persistence.ComponentGroupSerializer.ToCanvasFrozenPathDto(p.Path))
                .ToList();
        }

        return (designData, unresolvedConnectionPins);
    }

}
