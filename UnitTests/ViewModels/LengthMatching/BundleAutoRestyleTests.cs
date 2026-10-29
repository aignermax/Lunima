using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using Xunit;

namespace UnitTests.ViewModels.LengthMatching;

/// <summary>
/// Switching a multi-selection back to Auto: a bundle of facing pins is laid out at once as
/// crossing-free arches (no wire-by-wire search), equalized routes keep their common length, the
/// whole switch is one undo step, and "Release length lock" drops the constraint while keeping
/// every route's shape.
/// </summary>
public class BundleAutoRestyleTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static async Task<(DesignCanvasViewModel Canvas, List<WaveguideConnectionViewModel> Connections, CommandManager Commands)>
        CreateSelectedFanInAsync(int count)
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(count);
        var commands = new CommandManager();
        canvas.CommandManager = commands;
        foreach (var connection in connections)
            canvas.Selection.SelectedConnections.Add(connection);
        return (canvas, connections, commands);
    }

    private static async Task SetStyleAsync(DesignCanvasViewModel canvas, ConnectionRoutingViewModel routing,
        List<WaveguideConnectionViewModel> connections, WaveguideType style)
    {
        routing.SelectedStyle = style;
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline && connections.Any(c => c.Connection.Type != style))
            await Task.Delay(20);
        await canvas.RecalculateRoutesAsync();
    }

    [Fact]
    public async Task BendThenAuto_LaysTheBundleOutWithoutCrossings_AsOneUndoStep()
    {
        var (canvas, connections, commands) = await CreateSelectedFanInAsync(10);
        var routing = new ConnectionRoutingViewModel(canvas, commands);
        await SetStyleAsync(canvas, routing, connections, WaveguideType.Bend);
        var bendRoutes = connections.Select(c => c.Connection.RoutedPath).ToList();

        await SetStyleAsync(canvas, routing, connections, WaveguideType.Auto);

        connections.ShouldAllBe(c => c.Connection.Type == WaveguideType.Auto && c.Connection.RoutedPath != null
            && !c.Connection.IsBlockedFallback && !c.Connection.IsRouteFrozen);
        var paths = connections.Select(c => c.Connection.RoutedPath!).ToList();
        for (int i = 0; i < paths.Count; i++)
            for (int j = i + 1; j < paths.Count; j++)
                PathIntersectionDetector.Crosses(paths[i], paths[j]).ShouldBeFalse($"wires {i} and {j} cross");
        commands.UndoCount.ShouldBe(2);

        commands.Undo().ShouldBeTrue();
        connections.ShouldAllBe(c => c.Connection.Type == WaveguideType.Bend);
        for (int i = 0; i < connections.Count; i++)
            connections[i].Connection.RoutedPath!.TotalLengthMicrometers.ShouldBe(bendRoutes[i]!.TotalLengthMicrometers, 1e-6);
    }

    [Fact]
    public async Task EqualizedBundle_BackToAuto_KeepsItsCommonLength()
    {
        var (canvas, connections, commands) = await CreateSelectedFanInAsync(8);
        var equalize = Panel(canvas);
        connections[0].Connection.ReplaceRoutedPath(StretchedCopy(connections[0].Connection));
        await equalize.EqualizeCommand.ExecuteAsync(null);
        double target = connections[0].Connection.TargetLengthMicrometers!.Value;
        var routing = new ConnectionRoutingViewModel(canvas, commands);
        await SetStyleAsync(canvas, routing, connections, WaveguideType.Bend);

        await SetStyleAsync(canvas, routing, connections, WaveguideType.Auto);

        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == target);
        connections.ShouldAllBe(c => ConnectionLengthMatcher.MeetsTarget(c.Connection));
    }

    [Fact]
    public async Task ReleaseLengthLock_KeepsTheRoutes_DropsTheTarget_AndUndoRestoresIt()
    {
        var (canvas, connections, commands) = await CreateSelectedFanInAsync(6);
        var equalize = Panel(canvas);
        await equalize.EqualizeCommand.ExecuteAsync(null);
        var routes = connections.Select(c => c.Connection.RoutedPath!.TotalLengthMicrometers).ToList();
        double target = connections[0].Connection.TargetLengthMicrometers!.Value;

        equalize.ReleaseLengthLockCommand.Execute(null);

        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == null && c.Connection.IsRouteFrozen);
        for (int i = 0; i < connections.Count; i++)
            connections[i].Connection.PathLengthMicrometers.ShouldBe(routes[i], 1e-6);
        equalize.HasSuccessStatus.ShouldBeTrue(equalize.StatusMessage);

        commands.Undo().ShouldBeTrue();
        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == target);
    }

    [Fact]
    public async Task ReleaseLengthLock_WithNothingLocked_ChangesNothing()
    {
        var (_, _, commands, equalize) = await WithPanelAsync(3);

        equalize.ReleaseLengthLockCommand.Execute(null);

        commands.UndoCount.ShouldBe(0);
        equalize.StatusMessage.ShouldNotBeEmpty();
    }

    private static async Task<(DesignCanvasViewModel, List<WaveguideConnectionViewModel>, CommandManager, LengthEqualizationViewModel)>
        WithPanelAsync(int count)
    {
        var (canvas, connections, commands) = await CreateSelectedFanInAsync(count);
        return (canvas, connections, commands, Panel(canvas));
    }

    /// <summary>The panel as the app has it: created before the selection, so its radius field is filled.</summary>
    private static LengthEqualizationViewModel Panel(DesignCanvasViewModel canvas) =>
        new(canvas) { MaxRadiusText = "250" };

    /// <summary>The route with a long detour, so the equalized length is set by a tangled route.</summary>
    private static RoutedPath StretchedCopy(WaveguideConnection connection)
    {
        var result = new CAP_Core.Routing.LengthEqualization.LengthEqualizer().Prepare(new[] { connection }, new[] { connection },
            Array.Empty<CAP_Core.Components.Core.Component>(), connection.PathLengthMicrometers + 600, 10, 0.1);
        return CAP_Core.Routing.LengthEqualization.LengthEqualizer.Solve(result).Outcomes[0].Path ?? connection.RoutedPath!;
    }
}
