using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;

namespace UnitTests.Dies;

/// <summary>Shared builders for die tests: edge couplers, plain parts and die groups.</summary>
internal static class DieTestFixtures
{
    /// <summary>Demo-PDK edge-coupler footprint (100 × 19 µm, facet "fiber" west, "waveguide" east).</summary>
    public const double CouplerWidth = 100;

    /// <summary>Demo-PDK edge-coupler footprint height.</summary>
    public const double CouplerHeight = 19;

    /// <summary>Facet and waveguide pin height inside the coupler.</summary>
    public const double PinY = 9.5;

    /// <summary>An edge coupler (template-stamped like a placed demo-PDK part) at (x, y).</summary>
    public static Component EdgeCoupler(string id, double x, double y)
    {
        var coupler = TestComponentFactory.CreatePinlessComponent(x, y, CouplerWidth, CouplerHeight);
        coupler.Identifier = id;
        coupler.TemplateName = "Edge Coupler";
        coupler.PhysicalPins.Add(Pin(coupler, "fiber", 0, PinY, 180));
        coupler.PhysicalPins.Add(Pin(coupler, "waveguide", CouplerWidth, PinY, 0));
        return coupler;
    }

    /// <summary>A plain two-pin part (not an edge coupler) at (x, y).</summary>
    public static Component Part(string id, double x, double y, double width = 50, double height = 20)
    {
        var part = TestComponentFactory.CreatePinlessComponent(x, y, width, height);
        part.Identifier = id;
        part.TemplateName = "Straight";
        part.PhysicalPins.Add(Pin(part, "a0", 0, height / 2, 180));
        part.PhysicalPins.Add(Pin(part, "b0", width, height / 2, 0));
        return part;
    }

    /// <summary>A die group with the given outline (origin at its top-left) and children.</summary>
    public static ComponentGroup Die(string name, double x, double y, double width, double height,
        ActiveProcessSelection? process = null, params Component[] children)
    {
        var die = new ComponentGroup(name) { PhysicalX = x, PhysicalY = y, ProcessBinding = process };
        die.Die = new DieDefinition(width, height);
        foreach (var child in children)
            die.AddChild(child);
        return die;
    }

    /// <summary>A process selection with a fingerprint of the given core material.</summary>
    public static ActiveProcessSelection Process(string name, string material, params string[] pdks) =>
        new(name, new ProcessFingerprint(material, 300, "SiO2", 1550, name), pdks.ToList(), IsPlayground: false);

    private static PhysicalPin Pin(Component parent, string name, double x, double y, double angle) => new()
    {
        Name = name,
        ParentComponent = parent,
        OffsetXMicrometers = x,
        OffsetYMicrometers = y,
        AngleDegrees = angle,
    };
}
