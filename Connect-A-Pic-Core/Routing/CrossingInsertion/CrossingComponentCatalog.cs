using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// The 4-port waveguide crossings Lunima places and recognises. Light entering a crossing
/// leaves through the opposite port, straight on — the other axis is a separate path — so
/// layers that trace a signal along wires (the logic network) can follow it through.
/// </summary>
public static class CrossingComponentCatalog
{
    /// <summary>Nazca function of the SiEPIC EBeam 4-port crossing.</summary>
    public const string SiepicCrossingFunction = "ebeam_crossing4";

    /// <summary>Nazca function of the Demo PDK crossing (two crossing demofab straights).</summary>
    public const string DemoCrossingFunction = "demo_crossing";

    /// <summary>Largest deviation (degrees) from exactly opposite pin angles that still counts as straight through.</summary>
    private const double OppositeAngleToleranceDegrees = 1.0;

    private const double HalfTurnDegrees = 180.0;
    private const double FullTurnDegrees = 360.0;

    /// <summary>The known crossings, in default priority order.</summary>
    public static IReadOnlyList<string> KnownFunctions { get; } = new[] { SiepicCrossingFunction, DemoCrossingFunction };

    /// <summary>True when <paramref name="nazcaFunctionName"/> names a known 4-port crossing.</summary>
    public static bool IsCrossingFunction(string? nazcaFunctionName) =>
        nazcaFunctionName != null
        && KnownFunctions.Contains(nazcaFunctionName, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="component"/> is a known 4-port crossing.</summary>
    public static bool IsCrossing(Component? component) =>
        component is not null and not ComponentGroup && IsCrossingFunction(component.NazcaFunctionName);

    /// <summary>
    /// The port light entering <paramref name="pin"/> leaves through: the crossing port
    /// facing the opposite way. Null when the pin's component is no known crossing or the
    /// opposite port is not unique.
    /// </summary>
    public static PhysicalPin? StraightThroughExit(PhysicalPin pin)
    {
        var crossing = pin.ParentComponent;
        if (!IsCrossing(crossing) || !crossing!.PhysicalPins.Contains(pin))
            return null;
        var opposite = crossing.PhysicalPins
            .Where(p => !ReferenceEquals(p, pin) && IsOpposite(p.AngleDegrees, pin.AngleDegrees))
            .ToList();
        return opposite.Count == 1 ? opposite[0] : null;
    }

    private static bool IsOpposite(double angleA, double angleB)
    {
        double difference = Math.Abs(((angleA - angleB) % FullTurnDegrees + FullTurnDegrees) % FullTurnDegrees - HalfTurnDegrees);
        return difference <= OppositeAngleToleranceDegrees;
    }
}
