using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis;

/// <summary>
/// Issue #1437: the builder follows passive pass-through optics (any non-group
/// component with exactly one optical in→out path — a waveguide, an edge coupler,
/// the link between two chiplets) between two gate pins, so a logic signal keeps
/// its driver across a chiplet edge-coupler link. A gate input whose path cannot
/// be resolved uniquely (branching, a dead end, a non-pass-through element) is
/// rejected with a diagnostic instead of silently degenerating into a network input.
/// </summary>
public class LogicNetworkBuilderPassThroughTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Build_GatesJoinedThroughPassThroughComponent_ResolvesTheDriver(bool a, bool b, bool expected)
    {
        var nand = NandInstance("NAND");
        var inv = NotInstance("INV");
        var coupler = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var connections = new[]
        {
            Connect(Pin(nand.Group, "Y"), Pin(coupler, "in")),
            Connect(Pin(coupler, "out"), Pin(inv.Group, "A")),
        };

        var network = new LogicNetworkBuilder().Build(new[] { nand, inv }, connections);

        network.InputPinNames.ShouldBe(new[] { "NAND.A", "NAND.B" },
            "INV.A is driven by NAND.Y through the pass-through component");
        network.Evaluate(Bits(("NAND.A", a), ("NAND.B", b)))["INV.Y"].ShouldBe(expected);
    }

    [Fact]
    public void Build_InputPathBranchingBehindPassThrough_ThrowsDiagnosticInsteadOfSilentInput()
    {
        var first = NotInstance("NOT1");
        var second = NotInstance("NOT2");
        var inv = NotInstance("INV");
        var coupler = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var exitPin = Pin(coupler, "out");
        var connections = new[]
        {
            Connect(Pin(inv.Group, "A"), Pin(coupler, "in")),
            Connect(exitPin, Pin(first.Group, "Y")),
            Connect(exitPin, Pin(second.Group, "Y")),
        };

        var error = Should.Throw<ArgumentException>(
            () => new LogicNetworkBuilder().Build(new[] { first, second, inv }, connections));

        error.Message.ShouldContain("INV.A");
        error.Message.ShouldContain("branches");
        error.Message.ShouldContain("network input");
    }

    [Fact]
    public void Build_InputPathDeadEndingBehindPassThrough_ThrowsDiagnosticInsteadOfSilentInput()
    {
        var inv = NotInstance("INV");
        var coupler = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var connections = new[]
        {
            Connect(Pin(inv.Group, "A"), Pin(coupler, "in")),
        };

        var error = Should.Throw<ArgumentException>(
            () => new LogicNetworkBuilder().Build(new[] { inv }, connections));

        error.Message.ShouldContain("INV.A");
        error.Message.ShouldContain("without reaching a gate output");
        error.Message.ShouldContain("network input");
    }

    [Fact]
    public void Build_OutputPathDeadEndingBehindPassThrough_KeepsTheOutputTap()
    {
        // A gate output wired towards something that is not a gate stays an output
        // tap, as before — nothing degenerates silently on the output side.
        var inv = NotInstance("INV");
        var coupler = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var connections = new[]
        {
            Connect(Pin(inv.Group, "Y"), Pin(coupler, "in")),
        };

        var network = new LogicNetworkBuilder().Build(new[] { inv }, connections);

        network.InputPinNames.ShouldBe(new[] { "INV.A" });
        network.OutputPinNames.ShouldBe(new[] { "INV.Y" });
        network.Evaluate(Bits(("INV.A", false)))["INV.Y"].ShouldBeTrue();
    }

    /// <summary>A NAND gate instance on a synthetic group exposing the example's pin interface.</summary>
    private static LogicGateInstance NandInstance(string groupName) =>
        new(
            CreateGateGroup(groupName, "A", "B", "BIAS", "Y"),
            PinnedGateTables.NandGate(),
            new GateRoleAssignment(new[] { "A", "B" }, new[] { "Y" }, new[] { "BIAS" }, PinnedGateTables.NandThreshold));

    /// <summary>A NOT gate instance on a synthetic group exposing the example's pin interface.</summary>
    private static LogicGateInstance NotInstance(string groupName) =>
        new(
            CreateGateGroup(groupName, "A", "BIAS", "Y"),
            PinnedGateTables.NotGate(),
            new GateRoleAssignment(new[] { "A" }, new[] { "Y" }, new[] { "BIAS" }, PinnedGateTables.NotThreshold));

    /// <summary>A bare group whose external pins are connectable like canvas-synced group pins.</summary>
    private static ComponentGroup CreateGateGroup(string groupName, params string[] externalPinNames)
    {
        var group = new ComponentGroup(groupName);
        foreach (var pinName in externalPinNames)
        {
            var physicalPin = new PhysicalPin { Name = pinName, ParentComponent = group };
            group.PhysicalPins.Add(physicalPin);
            group.AddExternalPin(new GroupPin { Name = pinName, InternalPin = physicalPin });
        }
        return group;
    }

    /// <summary>A design connection between two physical pins.</summary>
    private static WaveguideConnection Connect(PhysicalPin from, PhysicalPin to) =>
        new() { StartPin = from, EndPin = to };

    /// <summary>Looks up a group's connectable external pin.</summary>
    private static PhysicalPin Pin(ComponentGroup group, string name) =>
        group.PhysicalPins.Single(p => p.Name == name);

    /// <summary>Looks up one physical pin of a pass-through component.</summary>
    private static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.Single(p => p.Name == name);

    /// <summary>Builds an input-bit dictionary from (name, bit) pairs.</summary>
    private static Dictionary<string, bool> Bits(params (string Name, bool Bit)[] bits) =>
        bits.ToDictionary(pair => pair.Name, pair => pair.Bit);
}
