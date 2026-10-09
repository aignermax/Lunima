using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis;

/// <summary>
/// A waveguide crossing between gates is transparent to the logic network: light entering
/// one port leaves straight on through the opposite port, and the crossing's other axis is a
/// separate, independent signal. Without this, a routed crossing ends the trace and the
/// gate input behind it silently becomes a network input.
/// </summary>
public class LogicNetworkBuilderCrossingTests
{
    private const string WestPort = "port 1";
    private const string EastPort = "port 2";
    private const string NorthPort = "port 3";
    private const string SouthPort = "port 4";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Build_TwoSignalsThroughOneCrossing_EachKeepsItsOwnDriver(bool a, bool b)
    {
        var first = NotInstance("NOT1");
        var second = NotInstance("NOT2");
        var horizontalLoad = NotInstance("LOADH");
        var verticalLoad = NotInstance("LOADV");
        var crossing = CreateDemoCrossing();
        var connections = new[]
        {
            Connect(Pin(first.Group, "Y"), Pin(crossing, WestPort)),
            Connect(Pin(crossing, EastPort), Pin(horizontalLoad.Group, "A")),
            Connect(Pin(second.Group, "Y"), Pin(crossing, NorthPort)),
            Connect(Pin(crossing, SouthPort), Pin(verticalLoad.Group, "A")),
        };

        var network = new LogicNetworkBuilder().Build(new[] { first, second, horizontalLoad, verticalLoad }, connections);

        network.InputPinNames.ShouldBe(new[] { "NOT1.A", "NOT2.A" }, ignoreOrder: true,
            customMessage: "both loads are driven through the crossing — neither becomes a network input");
        var outputs = network.Evaluate(Bits(("NOT1.A", a), ("NOT2.A", b)));
        outputs["LOADH.Y"].ShouldBe(a, "W→E carries NOT1's signal (double inversion)");
        outputs["LOADV.Y"].ShouldBe(b, "N→S carries NOT2's signal (double inversion)");
    }

    [Fact]
    public void StraightThroughExit_IsTheOppositePort()
    {
        var crossing = CreateDemoCrossing();

        CrossingComponentCatalog.StraightThroughExit(Pin(crossing, WestPort))!.Name.ShouldBe(EastPort);
        CrossingComponentCatalog.StraightThroughExit(Pin(crossing, SouthPort))!.Name.ShouldBe(NorthPort);
    }

    [Fact]
    public void StraightThroughExit_OfANonCrossing_IsNull()
    {
        var waveguide = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        CrossingComponentCatalog.StraightThroughExit(waveguide.PhysicalPins[0]).ShouldBeNull();
    }

    private static Component CreateDemoCrossing() =>
        ComponentTemplates.CreateFromTemplate(
            TestPdkLoader.LoadAllTemplates().Single(t => t.NazcaFunctionName == CrossingComponentCatalog.DemoCrossingFunction),
            0, 0);

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

    private static WaveguideConnection Connect(PhysicalPin from, PhysicalPin to) => new() { StartPin = from, EndPin = to };

    private static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.Single(p => p.Name == name);

    private static Dictionary<string, bool> Bits(params (string Name, bool Value)[] bits) =>
        bits.ToDictionary(b => b.Name, b => b.Value);
}
