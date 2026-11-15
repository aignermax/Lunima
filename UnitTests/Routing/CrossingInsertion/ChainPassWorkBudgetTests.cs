using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Tiles;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// The crossing chain pass's deterministic work budget
/// (<see cref="CrossingInsertionService.MaxChainPassNodesExpanded"/>): every crossing-aware route
/// of the pass draws from one node-expansion counter, so the pass stops at the same point on
/// every machine. The scene is a chip sealed by a frozen wire from edge to edge; each vertical
/// wire below it can only be connected through one crossing.
/// </summary>
public class ChainPassWorkBudgetTests
{
    private const double ChipWidth = 1200;
    private const double ChipHeight = 600;
    private const double SealY = 300;
    private const int AllWires = 3;
    private static readonly double[] WireXs = { 300, 600, 900 };

    /// <summary>A per-route budget too small for any vertical wire to reach its other pin.</summary>
    private const long HopelessRouteBudget = 20;

    [Fact]
    public async Task ZeroBudget_PlacesNoCrossing_AndMarksNoWireChainSearchExhausted()
    {
        var outcome = await RunPassAsync(AllWires, passBudget: 0);

        outcome.Connected.ShouldBe(0);
        outcome.Crossings.ShouldBeEmpty();
        outcome.Blocked.ShouldAllBe(blocked => blocked, "no search may run, so every wire keeps its fallback");
        outcome.Exhausted.ShouldAllBe(exhausted => !exhausted, "a pass that searched nothing proves nothing");
        outcome.NodesExpanded.ShouldBe(0);
    }

    [Fact]
    public async Task DefaultBudget_ConnectsEverySealedWireThroughACrossing()
    {
        var outcome = await RunPassAsync(AllWires, CrossingInsertionService.DefaultMaxChainPassNodesExpanded);

        outcome.Connected.ShouldBe(AllWires);
        outcome.Crossings.Count.ShouldBe(AllWires);
        outcome.Blocked.ShouldAllBe(blocked => !blocked);
        outcome.NodesExpanded.ShouldBeInRange(1, CrossingInsertionService.DefaultMaxChainPassNodesExpanded);
    }

    [Fact]
    public async Task SmallBudget_GivesTheSameResultOnTwoFreshCanvases()
    {
        var full = await RunPassAsync(AllWires, CrossingInsertionService.UnlimitedChainPassNodesExpanded);
        long small = full.NodesExpanded / 2;

        var first = await RunPassAsync(AllWires, small);
        var second = await RunPassAsync(AllWires, small);

        first.NodesExpanded.ShouldBe(small, "the pass must run into its budget");
        first.Connected.ShouldBeInRange(1, full.Connected - 1, "the budget stops the pass part-way through the wires");
        second.NodesExpanded.ShouldBe(first.NodesExpanded);
        second.Connected.ShouldBe(first.Connected);
        second.Crossings.ShouldBe(first.Crossings);
        second.Blocked.ShouldBe(first.Blocked);
        second.Exhausted.ShouldBe(first.Exhausted);
        second.Wires.ShouldBe(first.Wires);
        first.Exhausted.ShouldAllBe(exhausted => !exhausted, "a pass cut short by its budget marks no wire");
        first.RouteBudgetAfterPass.ShouldBe(WaveguideRouter.DefaultMaxRouteNodesExpanded,
            "the pass hands each route only what is left, then restores the router's own budget");
    }

    [Fact]
    public async Task PassWithinBudget_MarksTheFailedWire_OneCutShortByItsBudgetDoesNot()
    {
        // The tiny per-route budget makes the chain route fail cheaply; the pass itself still
        // searched every wire, which is what earns the chain-search-exhausted mark.
        var searched = await RunPassAsync(1, CrossingInsertionService.DefaultMaxChainPassNodesExpanded, HopelessRouteBudget);
        var cut = await RunPassAsync(1, searched.NodesExpanded - 1, HopelessRouteBudget);

        searched.Blocked.ShouldHaveSingleItem().ShouldBeTrue();
        searched.Exhausted.ShouldHaveSingleItem().ShouldBeTrue("the pass searched the wire completely");
        searched.NodesExpanded.ShouldBeInRange(1, HopelessRouteBudget);
        cut.Blocked.ShouldHaveSingleItem().ShouldBeTrue();
        cut.Exhausted.ShouldHaveSingleItem().ShouldBeFalse("the budget ran out before the wire's search was complete");
        cut.NodesExpanded.ShouldBe(searched.NodesExpanded - 1);
        cut.RouteBudgetAfterPass.ShouldBe(HopelessRouteBudget);
    }

    private sealed record PassOutcome(
        int Connected, long NodesExpanded, long RouteBudgetAfterPass, List<(double X, double Y)> Crossings,
        List<bool> Blocked, List<bool> Exhausted, List<string> Wires);

    /// <summary>Builds a fresh sealed chip with <paramref name="wireCount"/> blocked wires and runs one chain pass.</summary>
    private static async Task<PassOutcome> RunPassAsync(int wireCount, long passBudget, long? routeBudget = null)
    {
        var (canvas, wires) = await SealedChipAsync(wireCount);
        var manager = canvas.ConnectionManager;
        if (routeBudget is { } budget) canvas.Router.MaxRouteNodesExpanded = budget;
        var templates = TestPdkLoader.LoadAllTemplates();
        var placed = new List<Component>();
        var service = new CrossingInsertionService(
            () => CrossingComponentInstance.CreateFromTemplates(templates, new[] { "Demo PDK" })?.Component)
        {
            MaxChainPassNodesExpanded = passBudget,
            ComponentAdded = placed.Add,
        };

        int connected = service.ConnectBlockedWiresThroughCrossings(manager, canvas.Router);

        return new PassOutcome(connected, service.LastChainPassNodesExpanded, canvas.Router.MaxRouteNodesExpanded,
            placed.Select(c => (c.PhysicalX, c.PhysicalY)).ToList(),
            wires.Select(w => manager.Connections.Contains(w) && w.IsBlockedFallback).ToList(),
            wires.Select(manager.UnroutableWires.IsChainSearchExhausted).ToList(),
            manager.Connections.Select(Describe).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// A frozen wire from the left to the right chip edge seals the chip; the vertical wires
    /// from the top gates to the bottom gates are blocked and remembered as unroutable, so a
    /// chain pass that searched them completely may mark them chain-search-exhausted.
    /// </summary>
    private static async Task<(DesignCanvasViewModel Canvas, List<WaveguideConnection> Wires)> SealedChipAsync(int wireCount)
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, ChipWidth, ChipHeight);
        var left = Add(canvas, Box(0, SealY - 10, 20, 20, (20, 10, 0)));
        var right = Add(canvas, Box(ChipWidth - 20, SealY - 10, 20, 20, (0, 10, 180)));
        var seal = canvas.ConnectPins(left.PhysicalPins[0], right.PhysicalPins[0])!.Connection;
        await canvas.RecalculateRoutesAsync();
        seal.IsRouteFrozen = true;

        var wires = WireXs.Take(wireCount).Select(x =>
        {
            var top = Add(canvas, Box(x - 50, 40, 100, 60, (50, 60, 90)));
            var bottom = Add(canvas, Box(x - 50, 500, 100, 60, (50, 0, 270)));
            return canvas.ConnectPins(top.PhysicalPins[0], bottom.PhysicalPins[0])!.Connection;
        }).ToList();
        await canvas.RecalculateRoutesAsync();
        seal.IsBlockedFallback.ShouldBeFalse("the sealing wire routes straight across");
        wires.ShouldAllBe(w => w.IsBlockedFallback, "without a crossing there is no way through");
        foreach (var wire in wires)
            canvas.ConnectionManager.UnroutableWires.Remember(wire);
        return (canvas, wires);
    }

    private static string Describe(WaveguideConnection connection)
    {
        var (sx, sy) = connection.StartPin.GetAbsolutePosition();
        var (ex, ey) = connection.EndPin.GetAbsolutePosition();
        return FormattableString.Invariant(
            $"{sx:R},{sy:R}->{ex:R},{ey:R} blocked={connection.IsBlockedFallback} length={connection.RoutedPath?.TotalLengthMicrometers:R}");
    }

    private static Component Add(DesignCanvasViewModel canvas, Component component)
    {
        canvas.AddComponent(component, component.Identifier, "test");
        return component;
    }

    private static Component Box(double x, double y, double width, double height, (double X, double Y, double Angle) pin)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"Box_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>
            {
                new() { Name = "p", OffsetXMicrometers = pin.X, OffsetYMicrometers = pin.Y, AngleDegrees = pin.Angle },
            });
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        component.PhysicalX = x;
        component.PhysicalY = y;
        return component;
    }
}
