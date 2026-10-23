using CAP_DataAccess.Persistence.PIR;

namespace CAP_DataAccess.Persistence.NativeDesign;

public class ConnectionData
{
    public int StartComponentIndex { get; set; }
    public string StartPinName { get; set; } = "";
    public int EndComponentIndex { get; set; }
    public string EndPinName { get; set; } = "";

    /// <summary>
    /// Stable component identifier for the start endpoint (preferred over StartComponentIndex).
    /// Populated in new saves; null in old files (fall back to StartComponentIndex).
    /// </summary>
    public string? StartComponentId { get; set; }

    /// <summary>
    /// Stable component identifier for the end endpoint (preferred over EndComponentIndex).
    /// Populated in new saves; null in old files (fall back to EndComponentIndex).
    /// </summary>
    public string? EndComponentId { get; set; }

    /// <summary>
    /// Identifier of the top-level group containing the start endpoint's component, set only
    /// when the endpoint is a non-exposed pin of a component nested inside a group (#1444):
    /// child identifiers are unique within one group but not across groups (two instances of
    /// the same template share them), so the ancestor scopes the load-side search. Null for
    /// top-level endpoints and in files that predate the field.
    /// </summary>
    public string? StartAncestorGroupId { get; set; }

    /// <summary>Ancestor group identifier for the end endpoint — see <see cref="StartAncestorGroupId"/>.</summary>
    public string? EndAncestorGroupId { get; set; }

    public List<PathSegmentData>? CachedSegments { get; set; }
    public bool? IsBlockedFallback { get; set; }

    /// <summary>
    /// True when the cached route violates a physical constraint (e.g. bend radius). Null in
    /// old files (predates this field) — treated as false, matching their pre-existing behavior.
    /// </summary>
    public bool? IsInvalidGeometry { get; set; }

    /// <summary>
    /// True when the cached route is an honest placeholder rather than real geometry (the
    /// router replaced a self-crossing fallback with a straight line — see
    /// <see cref="CAP_Core.Routing.RoutedPath.IsPlaceholderGeometry"/>). Written as an
    /// explicit true/false whenever a route was cached (never omitted just because it is
    /// false), so null unambiguously means the file predates this field — <see
    /// cref="IsPlaceholderGeometry"/> is then inferred by the route loader from
    /// the route's shape instead of trusting a bare false.
    /// </summary>
    public bool? IsPlaceholderGeometry { get; set; }

    public bool? IsLocked { get; set; }

    /// <summary>Routing style name (WaveguideType); null = Auto (issue #574).</summary>
    public string? RoutingStyle { get; set; }

    /// <summary>Waveguide width in µm; null = model default.</summary>
    public double? WidthMicrometers { get; set; }

    /// <summary>Bend radius in µm; null = model default.</summary>
    public double? BendRadiusMicrometers { get; set; }

    /// <summary>True when the routed path is frozen (manual bend edits); null = false.</summary>
    public bool? IsRouteFrozen { get; set; }

    /// <summary>Refractive index of a die-to-die link's facet gap; null = air.</summary>
    public double? FacetGapMediumIndex { get; set; }

    /// <summary>Manual per-bend radius overrides (bend index → radius µm); null = none.</summary>
    public Dictionary<int, double>? BendRadiusOverrides { get; set; }

    /// <summary>Manual straight-segment shifts (straight index → offset µm, issue #791); null = none.</summary>
    public Dictionary<int, double>? StraightShiftOffsets { get; set; }

    /// <summary>
    /// GDS layer of the import source route polygons (route-derived GDS connections),
    /// paired with <see cref="SourceGdsDataType"/>. Null for app-created connections
    /// and in files that predate the field — the export then uses the process defaults.
    /// </summary>
    public int? SourceGdsLayer { get; set; }

    /// <summary>GDS datatype of the import source route polygons — see <see cref="SourceGdsLayer"/>.</summary>
    public int? SourceGdsDataType { get; set; }

    /// <summary>
    /// Target route length (µm) the connection was meandered to (issue #1008);
    /// null = no length intent (also in files that predate the field).
    /// </summary>
    public double? TargetLengthMicrometers { get; set; }

    /// <summary>Accepted deviation (µm) from <see cref="TargetLengthMicrometers"/>; null = no length intent.</summary>
    public double? LengthToleranceMicrometers { get; set; }

    /// <summary>
    /// The exact polygons an imported, still untouched route was drawn with; null for
    /// routed connections and in files that predate the field.
    /// </summary>
    public List<CAP_DataAccess.Persistence.DTOs.AsDrawnPolygonDto>? AsDrawnPolygons { get; set; }
}
