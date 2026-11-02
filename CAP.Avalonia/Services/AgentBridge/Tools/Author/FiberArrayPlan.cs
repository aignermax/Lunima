using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>One coupler of a fibre array: its name, the device port it serves and its top-left corner.</summary>
public sealed record FiberSite(string Name, string Port, double X, double Y);

/// <summary>
/// Where the couplers of a fibre array go: a straight row at <c>pitch</c>, centred on the ports,
/// <c>offset</c> beside the device's bounding box on the chosen side, each coupler turned so its
/// waveguide pin faces the device, in the ports' order along the row so no two wires cross.
/// </summary>
public sealed class FiberArrayPlan
{
    private FiberArrayPlan(ComponentTemplate coupler, string couplerPin, int quarterTurns, IReadOnlyList<FiberSite> sites) =>
        (Coupler, CouplerPin, QuarterTurns, Sites) = (coupler, couplerPin, quarterTurns, sites);

    /// <summary>The coupler type.</summary>
    public ComponentTemplate Coupler { get; }

    /// <summary>The coupler pin that is wired to the device.</summary>
    public string CouplerPin { get; }

    /// <summary>Rotation of every coupler (quarter turns, counter-clockwise).</summary>
    public int QuarterTurns { get; }

    /// <summary>The couplers in row order.</summary>
    public IReadOnlyList<FiberSite> Sites { get; }

    /// <summary>Plans the array from the tool arguments; null with a reason when impossible.</summary>
    public static FiberArrayPlan? Create(AgentDesignSession session, JsonElement arguments,
        IReadOnlyList<string> pinHints, double defaultPitch, double defaultOffset, out string? error)
    {
        var coupler = TemplateLookup.Find(session.Templates, ToolArguments.GetString(arguments, "coupler"),
            ToolArguments.GetString(arguments, "pdk"), out error);
        if (coupler == null) return null;
        var couplerPin = ToolArguments.GetString(arguments, "coupler_pin")
            ?? pinHints.Select(h => coupler.PinDefinitions.FirstOrDefault(p => p.Name.Contains(h, StringComparison.OrdinalIgnoreCase))?.Name)
                .FirstOrDefault(n => n != null);
        if (couplerPin == null)
            return Fail($"Name the coupler's waveguide pin (coupler_pin); pins: {string.Join(", ", coupler.PinDefinitions.Select(p => p.Name))}.", out error);

        var ports = new List<(string Ref, double X, double Y, ComponentPinOwner Owner)>();
        foreach (var reference in ToolArguments.GetStringList(arguments, "ports"))
        {
            var dot = reference.LastIndexOf('.');
            var owner = dot > 0 ? session.FindComponent(reference[..dot]) : null;
            var pin = owner == null ? null : ComponentPins.Find(owner.Component, reference[(dot + 1)..]);
            if (pin == null) return Fail($"No port '{reference}'.", out error);
            var (x, y) = pin.Pin.GetAbsolutePosition();
            ports.Add((reference, x, y, new ComponentPinOwner(DesignDescriber.Describe(owner!).Bounds)));
        }
        if (ports.Count == 0) return Fail("Pass the device ports to bring out.", out error);

        var side = (ToolArguments.GetString(arguments, "side") ?? "west").ToLowerInvariant();
        var facing = side switch { "west" => 0.0, "east" => 180.0, "north" => 90.0, "south" => 270.0, _ => double.NaN };
        if (double.IsNaN(facing)) return Fail("side must be west, east, north or south.", out error);
        var turned = PortAttachment.Facing(session.Canvas, coupler, couplerPin, facing);
        if (turned == null) return Fail($"'{coupler.Name}.{couplerPin}' cannot be turned to face the device.", out error);

        var pitch = ToolArguments.GetDouble(arguments, "pitch_um") ?? defaultPitch;
        var offset = ToolArguments.GetDouble(arguments, "offset_um") ?? defaultOffset;
        var prefix = ToolArguments.GetString(arguments, "prefix") ?? "fa";
        var box = (MinX: ports.Min(p => p.Owner.Bounds.MinX), MinY: ports.Min(p => p.Owner.Bounds.MinY),
            MaxX: ports.Max(p => p.Owner.Bounds.MaxX), MaxY: ports.Max(p => p.Owner.Bounds.MaxY));
        var horizontal = side is "west" or "east";
        var ordered = horizontal ? ports.OrderBy(p => p.Y).ToList() : ports.OrderBy(p => p.X).ToList();
        var centre = horizontal ? ordered.Average(p => p.Y) : ordered.Average(p => p.X);
        var (pinX, pinY) = (turned.Value.PinX, turned.Value.PinY);

        var sites = ordered.Select((port, i) =>
        {
            var along = centre + (i - (ordered.Count - 1) / 2.0) * pitch;
            var (anchorX, anchorY) = side switch
            {
                "west" => (box.MinX - offset, along),
                "east" => (box.MaxX + offset, along),
                "north" => (along, box.MinY - offset),
                _ => (along, box.MaxY + offset),
            };
            return new FiberSite($"{prefix}{i}", port.Ref, Math.Round(anchorX - pinX, 3), Math.Round(anchorY - pinY, 3));
        }).ToList();
        return new FiberArrayPlan(coupler, couplerPin, turned.Value.QuarterTurns, sites);
    }

    private sealed record ComponentPinOwner(BoundsInfo Bounds);

    private static FiberArrayPlan? Fail(string message, out string? error)
    {
        error = message;
        return null;
    }
}
