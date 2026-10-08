using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core;
using CAP_Core.Analysis;
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
/// Two wires leave a gate from neighbouring pins (an MMI's two arms, a copy gate's two
/// outputs). The upper one was routed first and bent down right at its pin, closing the lower
/// pin in: the lower wire is blocked although the design has room for both. Routing the lower
/// wire first and the upper one after it (through a crossing) connects both; when the upper
/// one cannot be connected that way, nothing changes.
/// </summary>
public class PinPairRerouterTests
{
    private const double ChipWidth = 600;
    private const double ChipHeight = 500;
    /// <summary>Pin pitch of an MMI's two arms (µm).</summary>
    private const double PinPitchMmi = 8;

    /// <summary>
    /// Pin pitch of a copy gate's two outputs (µm) — close enough that the grid opens the
    /// neighbour's cells at the pin, which once let the sealed wire cut through its bend.
    /// </summary>
    private const double PinPitchCopyGate = 4;

    private static readonly CrossingRouteSettings Settings = new(10, 1, 50, HeuristicWeight: 1.5);

    [Theory]
    [InlineData(PinPitchMmi)]
    [InlineData(PinPitchCopyGate)]
    public void SealedPin_IsFreedBySwappingTheRoutingOrder(double pinPitch)
    {
        var (canvas, upper, lower) = SealedScene(pinPitch);
        var templates = TestPdkLoader.LoadAllTemplates();

        var placed = new PinPairRerouter(new CrossingChainInserter()).TryResolve(
            lower, canvas.ConnectionManager, canvas.Router,
            () => CrossingComponentInstance.CreateFromTemplates(templates, new[] { "Demo PDK" })?.Component,
            Settings, CancellationToken.None);

        placed.ShouldNotBeNull("both wires fit once the inner pin routes first")
            .ShouldHaveSingleItem("the upper wire now crosses the lower one through a crossing");
        var connections = canvas.ConnectionManager.Connections;
        connections.Count.ShouldBe(4, "both wires are split onto the crossing's opposite ports");
        connections.ShouldAllBe(c => !c.IsBlockedFallback && c.IsPathValid);
        connections.Count(c => c.StartPin == lower.StartPin || c.EndPin == lower.EndPin).ShouldBe(2,
            "the freed wire runs from its pin to its target, through the crossing");
        new DesignValidator().Validate(connections.ToList())
            .ShouldNotContain(i => i.Type == DesignIssueType.WaveguideCrossing, "no wire may cut through another");
    }

    [Theory]
    [InlineData(PinPitchMmi)]
    [InlineData(PinPitchCopyGate)]
    public void SwapThatCannotConnectTheNeighbour_LeavesTheDesignAsItWas(double pinPitch)
    {
        var (canvas, upper, lower) = SealedScene(pinPitch);
        var upperPath = upper.RoutedPath!;

        // No crossing component: after the swap the upper wire would have to cross the lower one.
        var placed = new PinPairRerouter(new CrossingChainInserter()).TryResolve(
            lower, canvas.ConnectionManager, canvas.Router, () => null, Settings, CancellationToken.None);

        placed.ShouldBeNull();
        lower.IsBlockedFallback.ShouldBeTrue();
        canvas.ConnectionManager.Connections.ShouldBe(new[] { upper, lower }, ignoreOrder: true);
        upper.RoutedPath.ShouldBeSameAs(upperPath, "the neighbour gets its old route back");
        upper.IsBlockedFallback.ShouldBeFalse();
    }

    [Fact]
    public void NeighbourTheUserFroze_IsNeverRerouted()
    {
        var (canvas, upper, lower) = SealedScene(PinPitchMmi);
        upper.IsRouteFrozen = true;
        var upperPath = upper.RoutedPath!;
        var templates = TestPdkLoader.LoadAllTemplates();

        var placed = new PinPairRerouter(new CrossingChainInserter()).TryResolve(
            lower, canvas.ConnectionManager, canvas.Router,
            () => CrossingComponentInstance.CreateFromTemplates(templates, new[] { "Demo PDK" })?.Component,
            Settings, CancellationToken.None);

        placed.ShouldBeNull("a frozen wire between two components is the user's decision");
        upper.RoutedPath.ShouldBeSameAs(upperPath);
        lower.IsBlockedFallback.ShouldBeTrue();
    }

    /// <summary>
    /// A gate flush with the chip's top-left corner (no way around it) with outputs U (100, 100)
    /// and L one pitch below, both facing east. U goes to a pin below-right on a block that fills the
    /// chip's lower-right corner, L straight east. U is routed while a temporary obstacle forces
    /// it to turn down at once, which seals L in.
    /// </summary>
    private static (DesignCanvasViewModel Canvas, WaveguideConnection Upper, WaveguideConnection Lower) SealedScene(double pinPitch)
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, ChipWidth, ChipHeight);
        var gate = Box("gate", 0, 0, 100, 140, (100, 100, 0), (100, 100 + pinPitch, 0));
        var lowerTarget = Box("lowerTarget", 400, 80 + pinPitch, 80, 40, (0, 20, 180));
        // Reaches the chip's right and bottom edges, so nothing can pass around it.
        var upperTarget = Box("upperTarget", 400, 200, ChipWidth - 400, ChipHeight - 200, (0, 100, 180));
        foreach (var box in new[] { gate, lowerTarget, upperTarget })
            canvas.AddComponent(box, box.Identifier, "test");

        var router = canvas.Router;
        var manager = canvas.ConnectionManager;
        var wall = Box("wall", 130, 0, 240, 150);
        router.AddComponentObstacle(wall);
        var upper = new WaveguideConnection { StartPin = gate.PhysicalPins[0], EndPin = upperTarget.PhysicalPins[0] };
        upper.RestoreCachedPath(router.Route(upper.StartPin, upper.EndPin));
        router.RemoveComponentObstacle(wall);
        upper.IsBlockedFallback.ShouldBeFalse("the scene needs the sealing route");
        manager.Connections.Add(upper);
        router.PathfindingGrid!.AddWaveguideObstacle(upper.Id, upper.RoutedPath!.Segments, manager.WaveguideWidthMicrometers);

        var lower = new WaveguideConnection { StartPin = gate.PhysicalPins[1], EndPin = lowerTarget.PhysicalPins[0] };
        lower.RestoreCachedPath(router.Route(lower.StartPin, lower.EndPin));
        lower.IsBlockedFallback.ShouldBeTrue("the upper wire's turn seals the lower pin in — a route must never cut through it");
        manager.Connections.Add(lower);
        return (canvas, upper, lower);
    }

    private static Component Box(string name, double x, double y, double width, double height,
                                 params (double X, double Y, double Angle)[] pins)
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
            identifier: name,
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: pins.Select((p, i) => new PhysicalPin
            {
                Name = $"p{i}", OffsetXMicrometers = p.X, OffsetYMicrometers = p.Y, AngleDegrees = p.Angle,
            }).ToList());
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        component.PhysicalX = x;
        component.PhysicalY = y;
        foreach (var pin in component.PhysicalPins)
            pin.ParentComponent = component;
        return component;
    }
}
