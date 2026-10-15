using System.Diagnostics;
using Avalonia;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis.GateView;

namespace UnitTests.Logic.GateView;

/// <summary>
/// Gate-view layout: symbols sit at their physical gate, never overlap, and nets are
/// orthogonal wires that do not cut through symbols — on the shipped examples, including
/// the CPU-sized one within the UI frame budget.
/// </summary>
public class GateViewLayoutTests
{
    private const int UiBudgetMs = 100;

    [Fact]
    public void Symbol_IsCentredOnItsGroup_InputsLeft_OutputsRight()
    {
        var node = Node(inputs: 2, outputs: 1);
        var bounds = new Rect(400, 100, 920, 170);

        var symbol = GateSymbolGeometry.Place(node, bounds);

        symbol.Body.Center.ShouldBe(bounds.Center);
        bounds.Contains(symbol.Body).ShouldBeTrue();
        symbol.InputPins.Count.ShouldBe(2);
        symbol.InputPins.ShouldAllBe(p => p.X < symbol.Body.Left);
        symbol.OutputPins.ShouldHaveSingleItem().X.ShouldBeGreaterThan(symbol.Body.Right);
    }

    [Fact]
    public void ForwardWire_IsThreeOrthogonalSegments()
    {
        var target = new Rect(1000, 80, 200, 100);
        var request = new GateNetRouter.Request(new Point(300, 100), new Point(1000, 150), target, 30, TrunkX: 315);

        var wire = GateNetRouter.Route(request, new GateObstacleIndex([target]), new GateTrackRegistry());

        wire.ShouldBe([new Point(300, 100), new Point(315, 100), new Point(315, 150), new Point(1000, 150)]);
    }

    [Fact]
    public void FeedbackWire_RunsThroughAChannel_AroundTheSymbols()
    {
        var source = new Rect(500, 400, 200, 100);
        var target = new Rect(500, 100, 200, 100);
        var request = new GateNetRouter.Request(new Point(700, 450), new Point(500, 150), target, 30, TrunkX: 715);

        var wire = GateNetRouter.Route(request, new GateObstacleIndex([source, target]), new GateTrackRegistry());

        wire.Length.ShouldBe(6);
        GateNetRouter.IsClear(wire, new GateObstacleIndex([source, target])).ShouldBeTrue();
        wire[^1].ShouldBe(new Point(500, 150));
    }

    [Fact]
    public void ParallelRuns_ClaimSeparateTracks()
    {
        var tracks = new GateTrackRegistry();

        var first = tracks.ClaimVertical(100, 0, 500, 20, direction: 1);
        var second = tracks.ClaimVertical(100, 200, 800, 20, direction: 1);
        var elsewhere = tracks.ClaimVertical(100, 900, 1000, 20, direction: 1);

        first.ShouldBe(100);
        second.ShouldBe(120, "an overlapping run shifts one lane");
        elsewhere.ShouldBe(100, "a run further down reuses the free track");
    }

    [Theory]
    [InlineData("Logic Gate Half Adder.lun")]
    [InlineData("Logic Gate Counter 2-bit.lun")]
    [InlineData("Logic Gate ALU 1-bit.lun")]
    public async Task ExampleScenes_HaveNoOverlappingSymbols_AndNoWireThroughASymbol(string file)
    {
        var scene = await SceneAsync(file);

        var footprints = scene.Symbols.Select(s => s.Footprint).ToList();
        for (var i = 0; i < footprints.Count; i++)
        {
            for (var j = i + 1; j < footprints.Count; j++)
                footprints[i].Intersects(footprints[j]).ShouldBeFalse($"{scene.Symbols[i].Node.GateId} overlaps {scene.Symbols[j].Node.GateId}");
        }
        var index = new GateObstacleIndex(footprints);
        foreach (var route in scene.Routes)
        {
            foreach (var wire in route.Wires)
                GateNetRouter.IsClear(wire, index).ShouldBeTrue($"net {route.Net.Name} cuts through a symbol");
        }
    }

    [Fact]
    public async Task CpuSizedScene_LaysOutWithinTheFrameBudget()
    {
        var (components, connections) = await GateViewGraphExtractorTests.LoadAsync("Logic Gate ALU + RAM + ACC.lun");
        var graph = new GateViewGraphExtractor().Extract(components, connections);
        GateViewScene scene = GateViewScene.Empty;
        var fastest = long.MaxValue;
        // Best of three: a shared CI runner's one-off GC pause is noise; a slow layout is slow every time.
        for (var run = 0; run < 3; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            scene = GateViewSceneBuilder.Build(graph, components, ComponentGroupRenderer.CalculateGroupBounds);
            fastest = Math.Min(fastest, stopwatch.ElapsedMilliseconds);
        }

        fastest.ShouldBeLessThan(UiBudgetMs, "a dragged gate re-lays the scene out on the UI thread");
        scene.Symbols.Count.ShouldBe(graph.Nodes.Count);
        scene.Routes.Count.ShouldBe(graph.Nets.Count);
    }

    private static async Task<GateViewScene> SceneAsync(string file)
    {
        var (components, connections) = await GateViewGraphExtractorTests.LoadAsync(file);
        var graph = new GateViewGraphExtractor().Extract(components, connections);
        return GateViewSceneBuilder.Build(graph, components, ComponentGroupRenderer.CalculateGroupBounds);
    }

    private static GateViewNode Node(int inputs, int outputs)
    {
        var group = new ComponentGroup("G");
        return new GateViewNode(
            "G",
            group,
            group,
            Enumerable.Range(0, inputs).Select(i => new GateViewPin($"I{i}", null)).ToList(),
            Enumerable.Range(0, outputs).Select(i => new GateViewPin($"O{i}", null)).ToList(),
            isRegister: false);
    }
}
