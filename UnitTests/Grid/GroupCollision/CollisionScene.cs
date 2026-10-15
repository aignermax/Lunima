using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;

namespace UnitTests.Grid.GroupCollision;

/// <summary>
/// Small, hand-measurable group layouts for the geometry collision tests. Parts are
/// 50 × 50 µm, waveguides are 1 µm wide (placement clearance between two waveguide
/// centerlines: 0.5 + 0.5 + 2 µm spacing = 3 µm).
/// </summary>
internal static class CollisionScene
{
    /// <summary>Width of every scene waveguide in µm.</summary>
    public const double WaveguideWidth = 1.0;

    /// <summary>A 50 × 50 µm (or given size) leaf with one pin.</summary>
    public static Component Part(string id, double x, double y, double width = 50, double height = 50) =>
        new(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            0,
            id,
            new DiscreteRotation(),
            new List<PhysicalPin> { new() { Name = "pin0", OffsetXMicrometers = 0, OffsetYMicrometers = 0 } })
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = width,
            HeightMicrometers = height,
        };

    /// <summary>A frozen straight-piece polyline between two parts' pins.</summary>
    public static FrozenWaveguidePath Frozen(Component from, Component to, params (double X, double Y)[] points)
    {
        var path = new RoutedPath();
        for (int i = 0; i < points.Length - 1; i++)
            path.Segments.Add(new StraightSegment(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, 0));
        return new FrozenWaveguidePath
        {
            Path = path,
            StartPin = from.PhysicalPins[0],
            EndPin = to.PhysicalPins[0],
            WidthMicrometers = WaveguideWidth,
        };
    }

    /// <summary>
    /// An L: part at (x, y), part at (x+300, y+300), waveguide (50,25) → (250,25) →
    /// (250,325) → (300,325) relative to (x, y). The box (350 × 350) is mostly empty: the
    /// bottom-left region x ∈ [0, 200], y ∈ [100, 350] holds nothing.
    /// </summary>
    public static (ComponentGroup Group, FrozenWaveguidePath Path) LGroup(string name, double x, double y)
    {
        var group = new ComponentGroup(name) { PhysicalX = x, PhysicalY = y };
        var first = Part(name + "1", x, y);
        var second = Part(name + "2", x + 300, y + 300);
        group.AddChild(first);
        group.AddChild(second);
        var path = Frozen(first, second, (x + 50, y + 25), (x + 250, y + 25), (x + 250, y + 325), (x + 300, y + 325));
        group.AddInternalPath(path);
        return (group, path);
    }

    /// <summary>Parts at (x, y) and (x+200, y), waveguide (x+50, y+25) → (x+200, y+25).</summary>
    public static (ComponentGroup Group, FrozenWaveguidePath Path) StraightGroup(string name, double x, double y)
    {
        var group = new ComponentGroup(name) { PhysicalX = x, PhysicalY = y };
        var left = Part(name + "1", x, y);
        var right = Part(name + "2", x + 200, y);
        group.AddChild(left);
        group.AddChild(right);
        var path = Frozen(left, right, (x + 50, y + 25), (x + 200, y + 25));
        group.AddInternalPath(path);
        return (group, path);
    }

    /// <summary>True when <paramref name="b"/>'s box, shifted by the delta, overlaps <paramref name="a"/>'s box.</summary>
    public static bool BoxesOverlap(ComponentGroup a, ComponentGroup b, double deltaX, double deltaY) =>
        b.PhysicalX + deltaX < a.PhysicalX + a.WidthMicrometers
        && a.PhysicalX < b.PhysicalX + deltaX + b.WidthMicrometers
        && b.PhysicalY + deltaY < a.PhysicalY + a.HeightMicrometers
        && a.PhysicalY < b.PhysicalY + deltaY + b.HeightMicrometers;
}
