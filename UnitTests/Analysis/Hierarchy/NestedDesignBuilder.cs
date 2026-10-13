using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using UnitTests.Helpers;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.Analysis.Hierarchy;

/// <summary>
/// Small builders for hierarchical test designs: leaf components with an "in"/"out" pin
/// pair, groups nested at any depth, frozen internal paths and top-level connections —
/// all in absolute coordinates, exactly like the canvas stores them.
/// </summary>
internal static class NestedDesignBuilder
{
    /// <summary>Default leaf footprint height; pins sit at half of it.</summary>
    public const double LeafHeight = 20;

    /// <summary>Creates a leaf with an "in" pin on its left and an "out" pin on its right edge.</summary>
    public static Component Leaf(string id, double x, double y, double width = 100, double height = LeafHeight)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.Identifier = id;
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        AddPin(component, "in", 0, height / 2, 180);
        AddPin(component, "out", width, height / 2, 0);
        return component;
    }

    /// <summary>Creates a named group holding the given children.</summary>
    public static ComponentGroup Group(string name, params Component[] children)
    {
        var group = new ComponentGroup(name) { Identifier = name };
        foreach (var child in children)
            group.AddChild(child);
        return group;
    }

    /// <summary>Returns the named pin of a component.</summary>
    public static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.Single(p => p.Name == name);

    /// <summary>Exposes <paramref name="internalPin"/> as a group pin of <paramref name="group"/>.</summary>
    public static GroupPin Expose(ComponentGroup group, PhysicalPin internalPin, string name)
    {
        var (x, y) = internalPin.GetAbsolutePosition();
        var groupPin = new GroupPin
        {
            Name = name,
            InternalPin = internalPin,
            RelativeX = x - group.PhysicalX,
            RelativeY = y - group.PhysicalY,
        };
        group.AddExternalPin(groupPin);
        return groupPin;
    }

    /// <summary>Adds a frozen polyline path to <paramref name="group"/>.</summary>
    public static FrozenWaveguidePath Frozen(
        ComponentGroup group, PhysicalPin? start, PhysicalPin? end, params (double X, double Y)[] points)
    {
        var frozen = new FrozenWaveguidePath { Path = Polyline(points), StartPin = start, EndPin = end };
        group.AddInternalPath(frozen);
        return frozen;
    }

    /// <summary>Creates a routed top-level connection along the given polyline.</summary>
    public static WaveguideConnection Connection(PhysicalPin start, PhysicalPin end, params (double X, double Y)[] points)
    {
        var connection = new WaveguideConnection { StartPin = start, EndPin = end };
        connection.RestoreCachedPath(Polyline(points));
        return connection;
    }

    private static RoutedPath Polyline((double X, double Y)[] points)
    {
        var path = new RoutedPath();
        for (int i = 0; i + 1 < points.Length; i++)
            path.Segments.Add(new StraightSegment(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, 0));
        return path;
    }

    private static void AddPin(Component component, string name, double offsetX, double offsetY, double angle) =>
        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = name,
            ParentComponent = component,
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = angle,
        });
}
