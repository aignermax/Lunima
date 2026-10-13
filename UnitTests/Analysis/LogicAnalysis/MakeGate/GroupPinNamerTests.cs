using CAP_Core.Components.Core;
using CAP_Core.Components.GroupPins;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// Readable external pin names for new groups (<see cref="GroupPinNamer"/>) and the
/// Ctrl+G integration: west IN1… top to bottom, east OUT1…, north N…, south S…,
/// electrical E…; deterministic; saved files keep their names.
/// </summary>
public class GroupPinNamerTests
{
    private readonly GroupPinNamer _namer = new();

    [Theory]
    [InlineData(0, PinFacing.East)]
    [InlineData(90, PinFacing.South)]
    [InlineData(180, PinFacing.West)]
    [InlineData(270, PinFacing.North)]
    [InlineData(-90, PinFacing.North)]
    [InlineData(359, PinFacing.East)]
    [InlineData(44, PinFacing.East)]
    [InlineData(45, PinFacing.South)]
    [InlineData(540, PinFacing.West)]
    public void Classify_SnapsAngleToNearestSide(double angle, PinFacing expected) =>
        PinFacingClassifier.Classify(angle).ShouldBe(expected);

    [Fact]
    public void AssignNames_NumbersEachSideInReadingOrder()
    {
        var names = _namer.AssignNames(new[]
        {
            new GroupPinCandidate(0, 40, 180, false),   // west, lower
            new GroupPinCandidate(0, 10, 180, false),   // west, upper
            new GroupPinCandidate(300, 25, 0, false),   // east
            new GroupPinCandidate(80, 0, 270, false),   // north, right
            new GroupPinCandidate(20, 0, 270, false),   // north, left
            new GroupPinCandidate(50, 60, 90, false),   // south
            new GroupPinCandidate(200, 0, 270, true),   // electrical
            new GroupPinCandidate(100, 0, 270, true),   // electrical, further left
        });

        names.ShouldBe(new[] { "IN2", "IN1", "OUT1", "N2", "N1", "S1", "E2", "E1" });
    }

    [Fact]
    public void AssignNames_SamePositions_KeepsInputOrderDeterministically()
    {
        var pins = new[]
        {
            new GroupPinCandidate(0, 10, 180, false),
            new GroupPinCandidate(0, 10, 180, false),
        };

        _namer.AssignNames(pins).ShouldBe(new[] { "IN1", "IN2" });
        _namer.AssignNames(pins).ShouldBe(_namer.AssignNames(pins), "the same pins always get the same names");
    }

    [Fact]
    public void AssignNames_Empty_ReturnsEmpty() =>
        _namer.AssignNames(Array.Empty<GroupPinCandidate>()).ShouldBeEmpty();

    [Fact]
    public async Task CtrlG_OnNotNandParts_NamesPinsByDirection()
    {
        var (_, groupVm) = await GateKitFixture.RegroupedAsync(GateKitFixture.NotNandExample);
        var group = (ComponentGroup)groupVm.Component;

        GateKitFixture.PinExposing(group, "input_a", "a0").Name.ShouldBe("IN1");
        GateKitFixture.PinExposing(group, "input_b", "a0").Name.ShouldBe("IN2");
        GateKitFixture.PinExposing(group, "phase", "in").Name.ShouldBe("IN3");
        GateKitFixture.PinExposing(group, "output", "b0").Name.ShouldBe("OUT1",
            "the output waveguide sits higher than the couplers' spare out2 ports");
        GateKitFixture.PinExposing(group, "phase", "elec1").Name.ShouldBe("E1");
        GateKitFixture.PinExposing(group, "phase", "elec2").Name.ShouldBe("E2");
        group.ExternalPins.Select(p => p.Name).ShouldBeUnique();
        group.ExternalPins.ShouldNotContain(p => p.Name.Contains('_'),
            "no raw <component>_<pin> ids on a freshly created group");
    }

    [Fact]
    public async Task LoadingASavedGate_KeepsItsPinNames()
    {
        var canvas = await GateKitFixture.LoadAsync(GateKitFixture.NotNandExample);
        var group = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();

        group.ExternalPins.Select(p => p.Name).ShouldBe(new[] { "A", "B", "BIAS", "Y" }, ignoreOrder: true,
            customMessage: "only new groups get direction names; saved files load unchanged");
    }
}
