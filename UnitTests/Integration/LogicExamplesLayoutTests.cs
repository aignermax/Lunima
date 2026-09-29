using System.Text.Json;
using CAP_Core.Components.Core;
using Shouldly;

namespace UnitTests.Integration;

/// <summary>
/// Geometry sweep over every shipped logic example: the manifest-driven theory loads each
/// <c>examples/Logic Gate *.lun</c> through the real load path and checks what the canvas
/// will actually show — no two gate groups overlap, every external pin sits on the body of
/// its own gate, and the whole design lies inside the chip. The logic sweeps are blind to
/// this: a file whose gate bodies all pile onto one spot still assembles and evaluates.
/// </summary>
public class LogicExamplesLayoutTests
{
    /// <summary>Allowed distance between a group pin and the component pin it stands for.</summary>
    private const double PinOnBodyToleranceMicrometers = 0.01;

    /// <summary>Drawn straight waveguides (10 µm) overlap by 2 µm at the MMI pin pitch (8 µm).</summary>
    private const double PinPitchOverlapToleranceMicrometers = 2.5;

    [Theory]
    [MemberData(nameof(LogicExamplesSweepTests.LogicExampleFiles), MemberType = typeof(LogicExamplesSweepTests))]
    public async Task LogicExample_GateBodiesDoNotOverlap_AndPinsSitOnTheirGate(string exampleFileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var chip = DeclaredChipSize(path);
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
        var gates = LogicGateHalfAdderExampleTests.GroupsOf(canvas)
            .Select(group => (Group: group, Body: BodyOf(group)))
            .ToList();

        gates.ShouldNotBeEmpty($"'{exampleFileName}' must contain at least one gate group");
        foreach (var (group, body) in gates)
        {
            AssertPinsSitOnBody(group, body, exampleFileName);
            AssertInsideChip(group, body, chip, exampleFileName);
            AssertPartsInsideGateDoNotOverlap(group, exampleFileName);
        }
        AssertNoOverlaps(gates, exampleFileName);
    }

    /// <summary>
    /// The components inside one gate must not sit on top of each other. Straight waveguide
    /// stubs are drawn 10 µm tall while MMI and splitter pins sit 4 to 8 µm apart, so two
    /// stubs feeding neighbouring pins overlap by construction — pairs of stubs are skipped;
    /// everything else may touch by at most the small tolerance.
    /// </summary>
    private static void AssertPartsInsideGateDoNotOverlap(ComponentGroup group, string exampleFileName)
    {
        var parts = group.GetAllComponentsRecursive()
            .Select(c => (Part: c, Box: new Box(c.PhysicalX, c.PhysicalY, c.PhysicalX + c.WidthMicrometers, c.PhysicalY + c.HeightMicrometers)))
            .ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            for (var j = i + 1; j < parts.Count; j++)
            {
                if (IsStraightStub(parts[i].Part) && IsStraightStub(parts[j].Part))
                    continue;
                parts[i].Box.Shrunk(PinPitchOverlapToleranceMicrometers).Intersects(parts[j].Box).ShouldBeFalse(
                    $"'{exampleFileName}': inside gate '{group.GroupName}' part '{parts[i].Part.Identifier}' {parts[i].Box} " +
                    $"overlaps part '{parts[j].Part.Identifier}' {parts[j].Box}");
            }
        }
    }

    private static bool IsStraightStub(Component part) =>
        (part.HumanReadableName ?? part.Name).Contains("Straight Waveguide", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The chip size the file declares. The headless load path leaves applying it to the
    /// main window, so the file is the authority here.
    /// </summary>
    private static Box DeclaredChipSize(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return new Box(0, 0,
            doc.RootElement.GetProperty("ChipWidthMicrometers").GetDouble(),
            doc.RootElement.GetProperty("ChipHeightMicrometers").GetDouble());
    }

    private static void AssertPinsSitOnBody(ComponentGroup group, Box body, string exampleFileName)
    {
        foreach (var pin in group.ExternalPins)
        {
            var x = group.PhysicalX + pin.RelativeX;
            var y = group.PhysicalY + pin.RelativeY;
            var (internalX, internalY) = pin.InternalPin.GetAbsolutePosition();
            (Math.Abs(x - internalX) <= PinOnBodyToleranceMicrometers &&
             Math.Abs(y - internalY) <= PinOnBodyToleranceMicrometers).ShouldBeTrue(
                $"'{exampleFileName}': pin '{pin.Name}' of gate '{group.GroupName}' is drawn at ({x}, {y}) " +
                $"but its component pin sits at ({internalX}, {internalY}); gate body {body}");
        }
    }

    private static void AssertInsideChip(ComponentGroup group, Box body, Box chip, string exampleFileName)
    {
        (chip.Contains(body.Left, body.Top, 0) && chip.Contains(body.Right, body.Bottom, 0)).ShouldBeTrue(
            $"'{exampleFileName}': gate '{group.GroupName}' body {body} lies outside the declared chip {chip}");
    }

    private static void AssertNoOverlaps(List<(ComponentGroup Group, Box Body)> gates, string exampleFileName)
    {
        for (var i = 0; i < gates.Count; i++)
        {
            for (var j = i + 1; j < gates.Count; j++)
            {
                gates[i].Body.Intersects(gates[j].Body).ShouldBeFalse(
                    $"'{exampleFileName}': gate '{gates[i].Group.GroupName}' {gates[i].Body} overlaps " +
                    $"gate '{gates[j].Group.GroupName}' {gates[j].Body}");
            }
        }
    }

    /// <summary>Axis-aligned bounding box of a group's child components — what the canvas draws.</summary>
    private static Box BodyOf(ComponentGroup group)
    {
        var children = group.GetAllComponentsRecursive();
        children.ShouldNotBeEmpty($"gate '{group.GroupName}' has no child components");
        return new Box(
            children.Min(c => c.PhysicalX),
            children.Min(c => c.PhysicalY),
            children.Max(c => c.PhysicalX + c.WidthMicrometers),
            children.Max(c => c.PhysicalY + c.HeightMicrometers));
    }

    /// <summary>Axis-aligned rectangle in physical micrometres.</summary>
    private readonly record struct Box(double Left, double Top, double Right, double Bottom)
    {
        public bool Contains(double x, double y, double tolerance) =>
            x >= Left - tolerance && x <= Right + tolerance &&
            y >= Top - tolerance && y <= Bottom + tolerance;

        public Box Shrunk(double by) => new(Left + by, Top + by, Right - by, Bottom - by);

        public bool Intersects(Box other) =>
            Left < other.Right && other.Left < Right &&
            Top < other.Bottom && other.Top < Bottom;

        public override string ToString() => $"[{Left:0.#}..{Right:0.#} x {Top:0.#}..{Bottom:0.#}]";
    }
}
