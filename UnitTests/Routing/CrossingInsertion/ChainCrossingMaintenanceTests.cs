using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// Crossings placed by crossing-aware routing behave like adaptive crossings, checked on a
/// shipped example that carries such chains: loading adopts them (flagged, pieces not frozen),
/// deleting one keeps the wires through it, and moving a wire's end takes its chain out so
/// the wire routes again.
/// </summary>
public class ChainCrossingMaintenanceTests
{
    private const string ExampleFileName = "Logic Gate Full Adder.lun";

    /// <summary>Far enough that every route ending at the moved component goes stale.</summary>
    private const double MoveDistanceMicrometers = 200;

    [Fact]
    public async Task Load_AdoptsChainCrossings_AsAdaptiveWithUnfrozenPieces()
    {
        var canvas = await LoadAsync();
        var crossings = Crossings(canvas);

        crossings.ShouldNotBeEmpty("the example ships crossings placed by crossing-aware routing");
        crossings.ShouldAllBe(c => c.IsInsertedCrossing);
        Pieces(canvas, crossings).ShouldAllBe(p => !p.IsRouteFrozen,
            "chain pieces are ordinary auto routes, not frozen design parts");
    }

    [Fact]
    public async Task DeleteChainCrossing_KeepsBothWires_AndUndoRestoresTheCrossing()
    {
        var canvas = await LoadAsync();
        var crossing = Crossings(canvas).First();
        var pieces = Pieces(canvas, new[] { crossing });
        pieces.Count.ShouldBe(4);
        var outerPins = pieces.SelectMany(p => new[] { p.StartPin, p.EndPin })
            .Where(pin => pin.ParentComponent != crossing).ToHashSet();
        int wiresBefore = canvas.ConnectionManager.Connections.Count;
        var commands = new CommandManager();

        commands.ExecuteCommand(new DeleteComponentCommand(canvas, canvas.Components.Single(vm => vm.Component == crossing)));

        canvas.ConnectionManager.Connections.Count.ShouldBe(wiresBefore - 2, "four pieces merge into two wires");
        var merged = canvas.ConnectionManager.Connections
            .Where(c => outerPins.Contains(c.StartPin) || outerPins.Contains(c.EndPin)).ToList();
        merged.Count.ShouldBe(2);
        merged.ShouldAllBe(c => outerPins.Contains(c.StartPin) && outerPins.Contains(c.EndPin));
        canvas.Connections.Select(vm => vm.Connection).ShouldBe(canvas.ConnectionManager.Connections, ignoreOrder: true);

        commands.Undo();

        canvas.Components.ShouldContain(vm => vm.Component == crossing);
        canvas.ConnectionManager.Connections.Count.ShouldBe(wiresBefore);
        Pieces(canvas, new[] { crossing }).Count.ShouldBe(4);
        canvas.ConnectionManager.Connections.ShouldNotContain(c => merged.Contains(c));
    }

    [Fact]
    public async Task MovingAWireEnd_TakesItsChainOut()
    {
        var canvas = await LoadAsync();
        EnableAdaptiveCrossings(canvas);
        var crossing = Crossings(canvas).First();
        var outerComponent = Pieces(canvas, new[] { crossing })
            .SelectMany(p => new[] { p.StartPin.ParentComponent, p.EndPin.ParentComponent })
            .First(c => !CrossingComponentCatalog.IsCrossing(c));
        var movedVm = canvas.Components.Single(vm => vm.Component == outerComponent
            || (vm.Component is ComponentGroup g && g.GetAllComponentsRecursive().Contains(outerComponent)));

        canvas.MoveComponent(movedVm, 0, MoveDistanceMicrometers);
        await canvas.RecalculateRoutesAsync();

        canvas.Components.ShouldNotContain(vm => vm.Component == crossing,
            "the crossing of a wire whose end moved is re-evaluated, not kept forever");
        canvas.ConnectionManager.Connections.ShouldNotContain(
            c => c.StartPin.ParentComponent == crossing || c.EndPin.ParentComponent == crossing);
    }

    private static async Task<DesignCanvasViewModel> LoadAsync()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;
        return canvas;
    }

    private static void EnableAdaptiveCrossings(DesignCanvasViewModel canvas)
    {
        var templates = TestPdkLoader.LoadAllTemplates();
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        var binder = new CrossingInsertionCanvasBinder(
            canvas, () => CrossingComponentInstance.CreateFromTemplates(templates, preferred), action => action());
        binder.IsEnabled = true;
    }

    private static List<Component> Crossings(DesignCanvasViewModel canvas) =>
        canvas.Components.Select(vm => vm.Component).Where(CrossingComponentCatalog.IsCrossing).ToList();

    private static List<CAP_Core.Components.Connections.WaveguideConnection> Pieces(
        DesignCanvasViewModel canvas, IReadOnlyCollection<Component> crossings) =>
        canvas.ConnectionManager.Connections
            .Where(c => crossings.Contains(c.StartPin.ParentComponent) || crossings.Contains(c.EndPin.ParentComponent))
            .ToList();
}
