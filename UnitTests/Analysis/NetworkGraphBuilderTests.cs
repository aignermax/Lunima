using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.Analysis;

public class NetworkGraphBuilderTests
{
    [Fact]
    public void BuildAdjacencyList_EmptyConnections_ReturnsEmptyGraph()
    {
        var result = NetworkGraphBuilder.BuildAdjacencyList(
            new List<WaveguideConnection>());

        result.Count.ShouldBe(0);
    }

    [Fact]
    public void BuildAdjacencyList_SingleConnection_CreatesTwoNodes()
    {
        var a = CreateComponent("A");
        var b = CreateComponent("B");
        var conn = CreateConnection(a, b);

        var result = NetworkGraphBuilder.BuildAdjacencyList(
            new List<WaveguideConnection> { conn });

        result.Count.ShouldBe(2);
        result[a].Count.ShouldBe(1);
        result[a][0].Target.ShouldBe(b);
        result[b].Count.ShouldBe(0);
    }

    [Fact]
    public void BuildAdjacencyList_BidirectionalConnections_CreatesBothEdges()
    {
        var a = CreateComponent("A");
        var b = CreateComponent("B");
        var connAB = CreateConnection(a, b);
        var connBA = CreateConnection(b, a);

        var result = NetworkGraphBuilder.BuildAdjacencyList(
            new List<WaveguideConnection> { connAB, connBA });

        result[a].Count.ShouldBe(1);
        result[b].Count.ShouldBe(1);
        result[a][0].Target.ShouldBe(b);
        result[b][0].Target.ShouldBe(a);
    }

    [Fact]
    public void BuildAdjacencyList_SkipsNullPins()
    {
        var conn = new WaveguideConnection { StartPin = null, EndPin = null };

        var result = NetworkGraphBuilder.BuildAdjacencyList(
            new List<WaveguideConnection> { conn });

        result.Count.ShouldBe(0);
    }

    [Fact]
    public void BuildAdjacencyList_MultipleEdgesSameSource_GroupsTogether()
    {
        var a = CreateComponent("A");
        var b = CreateComponent("B");
        var c = CreateComponent("C");
        var conn1 = CreateConnection(a, b);
        var conn2 = CreateConnection(a, c);

        var result = NetworkGraphBuilder.BuildAdjacencyList(
            new List<WaveguideConnection> { conn1, conn2 });

        result[a].Count.ShouldBe(2);
    }

    #region Helpers

    private static Component CreateComponent(string id)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("in0", 0, MatterType.Light, RectSide.Left),
            new("out0", 1, MatterType.Light, RectSide.Right)
        });

        var allPins = Component.GetAllPins(parts)
            .SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var matrix = new SMatrix(allPins, new());

        var component = new Component(
            new Dictionary<int, SMatrix> { { 1550, matrix } },
            new(), "test", "", parts, 0, id, DiscreteRotation.R0);

        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "in0",
            ParentComponent = component,
            LogicalPin = component.GetAllPins().First(p => p.Name == "in0")
        });
        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "out0",
            ParentComponent = component,
            LogicalPin = component.GetAllPins().First(p => p.Name == "out0")
        });

        return component;
    }

    private static WaveguideConnection CreateConnection(Component src, Component tgt)
    {
        return new WaveguideConnection
        {
            StartPin = src.PhysicalPins.First(p => p.Name == "out0"),
            EndPin = tgt.PhysicalPins.First(p => p.Name == "in0"),
            TransmissionCoefficient = new Complex(0.9, 0)
        };
    }

    #endregion
}
