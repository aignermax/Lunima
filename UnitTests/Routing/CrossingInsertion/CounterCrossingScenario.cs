using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using UnitTests.Integration;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// The shipped 2-bit counter with its router-placed crossing chains, loaded through the real
/// load path with the crossing binder attached (adaptive insertion on or off), plus the move
/// that makes two of its chains stale: the gate group a chain piece ends at goes 150 µm down.
/// </summary>
internal sealed class CounterCrossingScenario
{
    public const string ExampleFileName = "Logic Gate Counter 2-bit.lun";

    /// <summary>Far enough that every route ending at the moved group goes stale.</summary>
    public const double MoveDistanceMicrometers = 150;

    private CounterCrossingScenario(DesignCanvasViewModel canvas, CrossingInsertionCanvasBinder binder)
    {
        Canvas = canvas;
        Binder = binder;
    }

    public DesignCanvasViewModel Canvas { get; }
    public CrossingInsertionCanvasBinder Binder { get; }
    public WaveguideConnectionManager Manager => Canvas.ConnectionManager;

    public static async Task<CounterCrossingScenario> LoadAsync(bool adaptive)
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;
        var templates = TestPdkLoader.LoadAllTemplates();
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        var binder = new CrossingInsertionCanvasBinder(
            canvas, () => CrossingComponentInstance.CreateFromTemplates(templates, preferred), action => action())
        {
            IsEnabled = adaptive,
        };
        return new CounterCrossingScenario(canvas, binder);
    }

    public List<Component> Crossings() =>
        Canvas.Components.Select(vm => vm.Component).Where(CrossingComponentCatalog.IsCrossing).ToList();

    public int BlockedCount() => Manager.Connections.Count(c => c.IsBlockedFallback);

    /// <summary>The gate group the first chain piece leaving a crossing towards a gate ends at.</summary>
    public ComponentViewModel GroupToMove()
    {
        var crossing = Crossings().First();
        var outer = Pieces(crossing)
            .SelectMany(p => new[] { p.StartPin.ParentComponent, p.EndPin.ParentComponent })
            .First(c => !CrossingComponentCatalog.IsCrossing(c));
        return Canvas.Components.Single(vm => vm.Component == outer
            || (vm.Component is ComponentGroup g && g.GetAllComponentsRecursive().Contains(outer)));
    }

    /// <summary>Crossings with a piece ending directly at a pin inside <paramref name="moved"/>.</summary>
    public List<Component> CrossingsDockedTo(ComponentViewModel moved)
    {
        var scope = new ComponentWireScope(moved.Component);
        return Manager.Connections
            .Where(scope.Touches)
            .SelectMany(c => new[] { c.StartPin.ParentComponent, c.EndPin.ParentComponent })
            .Where(CrossingComponentCatalog.IsCrossing)
            .Distinct()
            .ToList();
    }

    public List<WaveguideConnection> Pieces(Component crossing) =>
        Manager.Connections.Where(c => c.StartPin.ParentComponent == crossing || c.EndPin.ParentComponent == crossing).ToList();

    public async Task MoveAndRouteAsync(ComponentViewModel group)
    {
        Canvas.MoveComponent(group, 0, MoveDistanceMicrometers);
        await Canvas.RecalculateRoutesAsync();
    }
}
