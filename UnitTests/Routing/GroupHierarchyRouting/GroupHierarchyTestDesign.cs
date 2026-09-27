using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;

namespace UnitTests.Routing.GroupHierarchyRouting;

/// <summary>
/// Shared builders for group-hierarchy routing tests: simple two-pin components,
/// groups, and connections without any routed geometry.
/// </summary>
internal static class GroupHierarchyTestDesign
{
    /// <summary>Creates a component with a left- and right-facing pin.</summary>
    public static Component CreateComponent(double x, double y, double width, double height, string id)
    {
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "left",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = height / 2,
                AngleDegrees = 180,
                LogicalPin = new Pin("left", 0, MatterType.Light, RectSide.Left)
            },
            new PhysicalPin
            {
                Name = "right",
                OffsetXMicrometers = width,
                OffsetYMicrometers = height / 2,
                AngleDegrees = 0,
                LogicalPin = new Pin("right", 1, MatterType.Light, RectSide.Right)
            }
        };

        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            id,
            new DiscreteRotation(),
            pins)
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = width,
            HeightMicrometers = height
        };
    }

    /// <summary>Creates a pathless connection from one component's right pin to another's left pin.</summary>
    public static WaveguideConnection Connect(Component from, Component to) =>
        new()
        {
            StartPin = from.PhysicalPins.First(p => p.Name == "right"),
            EndPin = to.PhysicalPins.First(p => p.Name == "left"),
        };

    /// <summary>Creates a group containing the given components.</summary>
    public static ComponentGroup Group(string name, params Component[] children)
    {
        var group = new ComponentGroup(name);
        foreach (var child in children)
            group.AddChild(child);
        return group;
    }
}
