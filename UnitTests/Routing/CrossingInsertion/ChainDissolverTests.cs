using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core;
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
/// Dissolving a chain gives each wire through a crossing its pieces back as one connection —
/// exactly the geometry it had, straight through where the crossing sat — and takes the
/// crossing out of the grid; the undo restores the chain exactly.
/// </summary>
public class ChainDissolverTests
{
    private const double ChipWidth = 800;
    private const double ChipHeight = 600;
    private const double WireY = 300;
    private const double PinX = 400;

    [Fact]
    public async Task DissolvingAChain_MergesBothWiresBackPinToPin()
    {
        var (canvas, vertical, horizontal, crossing) = await ChainedSceneAsync();
        var manager = canvas.ConnectionManager;
        var piece = manager.Connections.First(c => c.StartPin == vertical.StartPin);

        var merged = ChainDissolver.TryDissolveChain(piece, manager, canvas.Router, out var dissolved, out _);

        merged.ShouldNotBeNull();
        merged.StartPin.ShouldBe(vertical.StartPin);
        merged.EndPin.ShouldBe(vertical.EndPin);
        dissolved.ShouldBe(new[] { crossing });
        manager.Connections.Count.ShouldBe(2, "both wires are whole again");
        manager.Connections.ShouldContain(c => c.StartPin == horizontal.StartPin && c.EndPin == horizontal.EndPin);
        manager.Connections.ShouldAllBe(c => c.IsPathValid && !c.IsBlockedFallback);
        merged.RoutedPath!.Segments[0].StartPoint.ShouldBe(vertical.StartPin.GetAbsolutePosition());
        merged.RoutedPath.Segments[^1].EndPoint.ShouldBe(vertical.EndPin.GetAbsolutePosition());
        var (cx, cy) = canvas.Router.PathfindingGrid!.PhysicalToGrid(crossing.PhysicalX + 1, crossing.PhysicalY + 1);
        canvas.Router.PathfindingGrid.GetCellState(cx, cy).ShouldNotBe((byte)1, "the crossing body left the grid");
    }

    [Fact]
    public async Task Undo_RestoresTheChainExactly()
    {
        var (canvas, vertical, _, crossing) = await ChainedSceneAsync();
        var manager = canvas.ConnectionManager;
        var before = manager.Connections.ToList();
        var piece = manager.Connections.First(c => c.StartPin == vertical.StartPin);

        ChainDissolver.TryDissolveChain(piece, manager, canvas.Router, out _, out var undo).ShouldNotBeNull();
        undo();

        manager.Connections.ShouldBe(before, ignoreOrder: true);
        var (cx, cy) = canvas.Router.PathfindingGrid!.PhysicalToGrid(
            crossing.PhysicalX + crossing.WidthMicrometers / 2, crossing.PhysicalY + crossing.HeightMicrometers / 2);
        canvas.Router.PathfindingGrid.GetCellState(cx, cy).ShouldBe((byte)1, "the crossing body is an obstacle again");
    }

    /// <summary>A vertical wire chained across a frozen horizontal wire through one Demo crossing.</summary>
    private static async Task<(DesignCanvasViewModel Canvas, WaveguideConnection Vertical, WaveguideConnection Horizontal, Component Crossing)>
        ChainedSceneAsync()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, ChipWidth, ChipHeight);
        var left = Box("left", 0, WireY - 10, 20, 20, (20, 10, 0));
        var right = Box("right", ChipWidth - 20, WireY - 10, 20, 20, (0, 10, 180));
        canvas.AddComponent(left, left.Identifier, "test");
        canvas.AddComponent(right, right.Identifier, "test");
        var horizontal = canvas.ConnectPins(left.PhysicalPins[0], right.PhysicalPins[0])!.Connection;
        await canvas.RecalculateRoutesAsync();
        horizontal.IsRouteFrozen = true;
        var top = Box("top", PinX - 160, 40, 320, 60, (160, 60, 90));
        var bottom = Box("bottom", PinX - 160, 500, 320, 60, (160, 0, 270));
        canvas.AddComponent(top, top.Identifier, "test");
        canvas.AddComponent(bottom, bottom.Identifier, "test");
        var vertical = canvas.ConnectPins(top.PhysicalPins[0], bottom.PhysicalPins[0])!.Connection;
        await canvas.RecalculateRoutesAsync();
        canvas.Router.PathfindingGrid!.RemoveWaveguideObstacle(vertical.Id);

        var templates = TestPdkLoader.LoadAllTemplates();
        var placed = new CrossingChainInserter().TryInsert(
            vertical, canvas.ConnectionManager, canvas.Router,
            () => CrossingComponentInstance.CreateFromTemplates(templates, new[] { "Demo PDK" })?.Component,
            new CrossingRouteSettings(10, 1, 50), CancellationToken.None);
        var crossing = placed.ShouldNotBeNull().ShouldHaveSingleItem();
        return (canvas, vertical, horizontal, crossing);
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
