using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Components.ComponentHelpers;
using Shouldly;
using UnitTests.Integration;

namespace UnitTests.Analysis.LogicAnalysis.GateView;

/// <summary>
/// The gate-level graph of the shipped logic examples: which gates and symbols appear,
/// how the nets join them, and — the contract that matters — that the view wires exactly
/// what the logic network the Logic tab builds wires.
/// </summary>
public class GateViewGraphExtractorTests
{
    private static readonly int Wavelength = StandardWaveLengths.RedNM;

    [Fact]
    public async Task NotNand_IsOneNandWithTwoNamedInputs()
    {
        var graph = await ExtractAsync("Logic Gate NOT-NAND.lun");

        graph.Nodes.ShouldHaveSingleItem().Kind.ShouldBe(GateSymbolKind.Nand);
        InputNet(graph, "A").Loads.ShouldHaveSingleItem();
        InputNet(graph, "B").Loads.ShouldHaveSingleItem();
        graph.Nets.Count(net => net.Driver != null).ShouldBe(1, "the gate's single output is one net");
    }

    [Fact]
    public async Task AndFromNand_ReadsAsAnAndGate()
    {
        var graph = await ExtractAsync("Logic Gate AND-from-NAND.lun");

        graph.Nodes.ShouldHaveSingleItem().Kind.ShouldBe(GateSymbolKind.And,
            "the two-stage interference cell computes AND — the symbol shows the function, not the build");
    }

    [Fact]
    public async Task HalfAdder_SixNandsAndOneNot_WiredLikeTheTextbook()
    {
        var graph = await ExtractAsync("Logic Gate Half Adder.lun");

        graph.Nodes.Count.ShouldBe(7);
        graph.Nodes.Count(n => n.Kind == GateSymbolKind.Nand).ShouldBe(6);
        graph.NodesById["NOT1"].Kind.ShouldBe(GateSymbolKind.Not);
        InputNet(graph, "A").Loads.Count.ShouldBe(4, "the four addend-A pins merge into one input net");
        InputNet(graph, "B").Loads.Count.ShouldBe(4);
        DrivenNet(graph, "NAND5", "Y").Loads.ShouldBe([new LogicPinRef("NOT1", "A")]);
        DrivenNet(graph, "NAND4", "Y").Loads.ShouldBeEmpty("the sum is a primary output");
        DrivenNet(graph, "NOT1", "Y").Loads.ShouldBeEmpty("the carry is a primary output");
    }

    [Fact]
    public async Task Counter_ShowsRegistersFanOutsAndTheFeedbackLoop()
    {
        var graph = await ExtractAsync("Logic Gate Counter 2-bit.lun");

        graph.Nodes.Count(n => n.Kind == GateSymbolKind.Register).ShouldBe(2);
        graph.Nodes.Count(n => n.Kind == GateSymbolKind.FanOut).ShouldBe(4);
        graph.Nodes.Count(n => n.Kind == GateSymbolKind.Nand).ShouldBe(3);
        var c0 = graph.Nets.Single(net => net.Name == "C0");
        c0.IsNamed.ShouldBeTrue();
        c0.Loads.ShouldBe([new LogicPinRef("COPY0", "A")]);
        DrivenNet(graph, "COPY0", "Y1").Loads.ShouldBe([new LogicPinRef("Q0", "A")],
            "the register's own output feeds back into it through the copy");
        InputNet(graph, "S̄").Loads.ShouldBe([new LogicPinRef("Q0", "B")]);
    }

    [Theory]
    [InlineData("Logic Gate Half Adder.lun")]
    [InlineData("Logic Gate Counter 2-bit.lun")]
    [InlineData("Logic Gate ALU 1-bit.lun")]
    public async Task Wiring_MatchesTheLogicNetworkTheLogicTabBuilds(string file)
    {
        var (components, connections) = await LoadAsync(file);
        var graph = new GateViewGraphExtractor().Extract(components, connections);
        var network = await new LogicNetworkAssembler().AssembleAsync(components, connections, Wavelength);

        foreach (var (load, driver) in network.InputWiring)
        {
            var net = graph.Nets.Single(n => n.Loads.Contains(load));
            switch (driver)
            {
                case LogicNetDriver.GateOutput output:
                    net.Driver.ShouldBe(output.Pin, $"{load} must be driven by {output.Pin}");
                    break;
                case LogicNetDriver.NetworkInput input:
                    net.Driver.ShouldBeNull();
                    net.Name.ShouldBe(input.PinName);
                    break;
            }
        }
        graph.Nodes.Select(n => n.GateId).ShouldBe(network.Gates.Keys, ignoreOrder: true);
    }

    [Fact]
    public async Task CpuSizedExample_ExtractsAndClassifiesFromAFewStructures()
    {
        var (components, connections) = await LoadAsync("Logic Gate ALU + RAM + ACC.lun");
        var classifier = new GateFunctionClassifier();

        var graph = new GateViewGraphExtractor().Extract(components, connections);
        await classifier.ClassifyAsync(graph, Wavelength);

        graph.Nodes.Count.ShouldBeGreaterThan(500);
        classifier.CachedStructureCount.ShouldBeLessThan(10, "hundreds of gates are copies of a handful of cells");
        graph.Nodes.ShouldAllBe(n => n.Kind != GateSymbolKind.Unknown);
        graph.Nodes.ShouldContain(n => !ReferenceEquals(n.Group, n.TopLevelGroup),
            "gates nested in cell wrappers select their top-level cell");
    }

    [Fact]
    public void DesignWithoutGates_GivesTheEmptyGraph()
    {
        new GateViewGraphExtractor().Extract([], []).ShouldBeSameAs(GateViewGraph.Empty);
    }

    [Fact]
    public async Task EqualGateCells_ShareOneStructureSignature()
    {
        var (components, _) = await LoadAsync("Logic Gate Half Adder.lun");
        var groups = components.OfType<CAP_Core.Components.Core.ComponentGroup>().ToList();
        var nand1A = groups.Single(g => g.GroupName == "NAND1A");
        var nand2 = groups.Single(g => g.GroupName == "NAND2");
        var not1 = groups.Single(g => g.GroupName == "NOT1");

        GateStructureSignature.Of(nand1A, Wavelength).ShouldBe(GateStructureSignature.Of(nand2, Wavelength));
        GateStructureSignature.Of(not1, Wavelength).ShouldNotBe(GateStructureSignature.Of(nand2, Wavelength),
            "NOT reads the same cell with other pin roles");
        GateStructureSignature.Of(nand1A, Wavelength).ShouldNotBe(GateStructureSignature.Of(nand1A, Wavelength + 10));
    }

    private static GateViewNet InputNet(GateViewGraph graph, string name) =>
        graph.Nets.Single(net => net.Driver == null && net.Name == name);

    private static GateViewNet DrivenNet(GateViewGraph graph, string gate, string pin) =>
        graph.Nets.Single(net => net.Driver == new LogicPinRef(gate, pin));

    private static async Task<GateViewGraph> ExtractAsync(string file)
    {
        var (components, connections) = await LoadAsync(file);
        var graph = new GateViewGraphExtractor().Extract(components, connections);
        await new GateFunctionClassifier().ClassifyAsync(graph, Wavelength);
        return graph;
    }

    internal static async Task<(List<CAP_Core.Components.Core.Component>, List<CAP_Core.Components.Connections.WaveguideConnection>)>
        LoadAsync(string file)
    {
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(
            Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), file));
        return (canvas.Components.Select(c => c.Component).ToList(),
                canvas.Connections.Select(c => c.Connection).ToList());
    }
}
