using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core;
using CAP_Core.Components;
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
/// A blocked wire whose only way leads across another wire is connected through a placed
/// crossing: the crossing sits on the intersection, both wires are split and docked onto its
/// opposite ports, and every piece keeps its routed geometry, frozen and valid.
/// </summary>
public class CrossingChainInserterTests
{
    private const double ChipWidth = 800;
    private const double ChipHeight = 600;
    private const double WireY = 300;
    private const double PinX = 400;
    private static readonly CrossingRouteSettings Settings = new(10, 1, 50);

    [Fact]
    public async Task BlockedWire_AcrossASealingWire_IsConnectedThroughOneCrossing()
    {
        var (canvas, crossed, blocked) = await SceneAsync();
        var templates = TestPdkLoader.LoadAllTemplates();
        canvas.Router.PathfindingGrid!.RemoveWaveguideObstacle(blocked.Id);

        var reasons = new List<string>();
        var placed = new CrossingChainInserter { Rejected = reasons.Add }.TryInsert(
            blocked, canvas.ConnectionManager, canvas.Router,
            () => CrossingComponentInstance.CreateFromTemplates(templates, new[] { "Demo PDK" })?.Component,
            Settings, CancellationToken.None);

        var crossing = placed.ShouldNotBeNull(string.Join(", ", reasons)).ShouldHaveSingleItem();
        (crossing.PhysicalX + crossing.WidthMicrometers / 2).ShouldBe(PinX, 1.0, "the crossing sits on the vertical route");
        (crossing.PhysicalY + crossing.HeightMicrometers / 2).ShouldBe(WireY, 1e-6, "…exactly on the crossed wire");

        var connections = canvas.ConnectionManager.Connections;
        connections.ShouldNotContain(blocked);
        connections.ShouldNotContain(crossed);
        connections.Count.ShouldBe(4, "each wire becomes two pieces docked onto the crossing");
        connections.ShouldAllBe(c => c.IsRouteFrozen && c.IsPathValid && !c.IsBlockedFallback);
        crossing.PhysicalPins.ShouldAllBe(pin =>
            connections.Count(c => c.StartPin == pin || c.EndPin == pin) == 1, "every crossing port carries exactly one piece");
    }

    [Fact]
    public async Task NoCrossingAvailable_LeavesTheDesignUnchanged()
    {
        var (canvas, crossed, blocked) = await SceneAsync();
        canvas.Router.PathfindingGrid!.RemoveWaveguideObstacle(blocked.Id);

        var placed = new CrossingChainInserter().TryInsert(
            blocked, canvas.ConnectionManager, canvas.Router, () => null, Settings, CancellationToken.None);

        placed.ShouldBeNull();
        canvas.ConnectionManager.Connections.ShouldBe(new[] { crossed, blocked }, ignoreOrder: true);
    }

    [Fact]
    public async Task BlockedWire_WhoseWayHasOpenedUp_GetsAPlainRouteWithoutCrossings()
    {
        var (canvas, crossed, blocked) = await SceneAsync();
        var grid = canvas.Router.PathfindingGrid!;
        grid.RemoveWaveguideObstacle(blocked.Id);
        grid.RemoveWaveguideObstacle(crossed.Id);
        canvas.ConnectionManager.Connections.Remove(crossed);

        var placed = new CrossingChainInserter().TryInsert(
            blocked, canvas.ConnectionManager, canvas.Router, () => null, Settings, CancellationToken.None);

        placed.ShouldNotBeNull().ShouldBeEmpty("no crossing is needed once the sealing wire is gone");
        canvas.ConnectionManager.Connections.ShouldContain(blocked, "the wire keeps its identity");
        blocked.IsBlockedFallback.ShouldBeFalse();
        blocked.IsPathValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ChainPass_WithItsTimeBudgetSpent_LeavesTheBlockedWireAlone()
    {
        var (canvas, _, blocked) = await SceneAsync();
        var templates = TestPdkLoader.LoadAllTemplates();
        var service = new CrossingInsertionService(
            () => CrossingComponentInstance.CreateFromTemplates(templates, new[] { "Demo PDK" })?.Component)
        {
            ChainPassTimeBudget = TimeSpan.Zero,
        };

        service.ConnectBlockedWiresThroughCrossings(canvas.ConnectionManager, canvas.Router).ShouldBe(0);

        blocked.IsBlockedFallback.ShouldBeTrue("a spent budget must stop the pass, not leave a half-applied chain");
        canvas.ConnectionManager.Connections.ShouldContain(blocked);
    }

    /// <summary>
    /// A frozen horizontal wire from the left to the right chip edge seals the chip, so the
    /// vertical wire between the top and bottom gates can only cross it.
    /// </summary>
    private static async Task<(DesignCanvasViewModel Canvas, CAP_Core.Components.Connections.WaveguideConnection Crossed,
        CAP_Core.Components.Connections.WaveguideConnection Blocked)> SceneAsync()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, ChipWidth, ChipHeight);
        var left = Box(0, WireY - 10, 20, 20, (20, 10, 0));
        var right = Box(ChipWidth - 20, WireY - 10, 20, 20, (0, 10, 180));
        var top = Box(PinX - 160, 40, 320, 60, (160, 60, 90));
        var bottom = Box(PinX - 160, 500, 320, 60, (160, 0, 270));
        canvas.AddComponent(left, left.Identifier, "test");
        canvas.AddComponent(right, right.Identifier, "test");
        var crossed = canvas.ConnectPins(left.PhysicalPins[0], right.PhysicalPins[0])!.Connection;
        await canvas.RecalculateRoutesAsync();
        crossed.IsRouteFrozen = true;

        canvas.AddComponent(top, top.Identifier, "test");
        canvas.AddComponent(bottom, bottom.Identifier, "test");
        var blocked = canvas.ConnectPins(top.PhysicalPins[0], bottom.PhysicalPins[0])!.Connection;
        await canvas.RecalculateRoutesAsync();
        crossed.IsBlockedFallback.ShouldBeFalse("the sealing wire routes straight across");
        blocked.IsBlockedFallback.ShouldBeTrue("without a crossing there is no way through");
        return (canvas, crossed, blocked);
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
