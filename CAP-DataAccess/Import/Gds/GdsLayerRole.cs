namespace CAP_DataAccess.Import.Gds;

/// <summary>Confirmed interpretation of geometry or text on a GDS layer/datatype pair.</summary>
[Flags]
public enum GdsLayerRole
{
    Unknown = 0,
    OpticalCore = 1,
    Slab = 2,
    Etch = 4,
    Electrical = 8,
    Heater = 16,
    PortMarker = 32,
    PortLabel = 64,
    Auxiliary = 128,
}

/// <summary>One layer assignment; marker and label roles may share the same pair.</summary>
public sealed record GdsLayerAssignment(int Layer, int Datatype, GdsLayerRole Role);

/// <summary>Translates confirmed roles into strict detector and routing configuration.</summary>
public static class GdsLayerRoleMapping
{
    public static GdsHierarchyImportOptions Apply(
        GdsHierarchyImportOptions options, IReadOnlyList<GdsLayerAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(assignments);
        var roles = assignments.GroupBy(a => (a.Layer, a.Datatype))
            .Select(g => new GdsLayerAssignment(g.Key.Layer, g.Key.Datatype,
                g.Aggregate(GdsLayerRole.Unknown, (role, assignment) => role | assignment.Role)))
            .ToList();
        foreach (var assignment in roles)
        {
            if ((assignment.Role & GdsLayerRole.OpticalCore) != 0
                && (assignment.Role & (GdsLayerRole.Electrical | GdsLayerRole.Heater)) != 0)
                throw new ArgumentException(
                    $"Layer ({assignment.Layer}, {assignment.Datatype}) cannot be both optical core and electrical/heater.",
                    nameof(assignments));
        }

        var core = Pairs(GdsLayerRole.OpticalCore);
        var electrical = Pairs(GdsLayerRole.Electrical | GdsLayerRole.Heater);
        return options with
        {
            AllowAnyLayerLabelFallback = false,
            RouteLayers = core,
            MetalRouteLayers = electrical,
            PinDetection = options.PinDetection with
            {
                WaveguideLayers = core,
                ElectricalLayers = electrical,
                PortLayers = Pairs(GdsLayerRole.PortLabel),
                PortMarkerLayers = Pairs(GdsLayerRole.PortMarker),
            },
        };

        IReadOnlyList<(int Layer, int Datatype)> Pairs(GdsLayerRole role) =>
            roles.Where(a => (a.Role & role) != 0)
                .Select(a => (a.Layer, a.Datatype)).ToArray();
    }
}
