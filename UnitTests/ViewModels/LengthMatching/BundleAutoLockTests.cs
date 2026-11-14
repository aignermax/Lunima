using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using Xunit;

namespace UnitTests.ViewModels.LengthMatching;

/// <summary>
/// Bend → Auto on a bundle that shares one length lock: the lock is kept when any layout meets it
/// (arches, or routes meandered to it), and when none does, the target stays, the routes show the
/// miss, a status line names the reason and the way out, and one Ctrl+Z restores the routes.
/// </summary>
public class BundleAutoLockTests
{
    private const int ChannelCount = 6;
    private const double Tolerance = 0.1;
    /// <summary>Far below the pin-to-pin distance of every fan-in channel: no layout can be this short.</summary>
    private const double UnreachableLockMicrometers = 100.0;
    /// <summary>The largest radius "Equalize lengths" stamps on a locked route as its meander radius.</summary>
    private const double StampedMeanderRadius = 250.0;
    private const double SameLengthMicrometers = 1e-6;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task BendThenAuto_LockNoLayoutMeets_KeepsTheTarget_PublishesTheReason_AndOneUndoRestoresTheRoutes()
    {
        var (canvas, connections, commands) = await CreateLockedFanInAsync(_ => UnreachableLockMicrometers);
        var statuses = new List<string>();
        var routing = new ConnectionRoutingViewModel(canvas, commands) { UpdateStatus = statuses.Add };
        await SetStyleAsync(canvas, routing, connections, WaveguideType.Bend);
        var bendLengths = connections.Select(c => c.Connection.PathLengthMicrometers).ToList();

        await SetStyleAsync(canvas, routing, connections, WaveguideType.Auto);

        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == UnreachableLockMicrometers, "the lock stays");
        connections.ShouldAllBe(c => c.Connection.RoutedPath != null && LengthLockActions.IsViolated(c), "the miss stays visible");
        string status = statuses.ShouldHaveSingleItem();
        status.ShouldContain(UnreachableLockMicrometers.ToString("F2", CultureInfo.CurrentCulture));
        commands.UndoCount.ShouldBe(2);

        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        connections.ShouldAllBe(c => c.Connection.Type == WaveguideType.Bend);
        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == UnreachableLockMicrometers);
        for (int i = 0; i < connections.Count; i++)
            connections[i].Connection.PathLengthMicrometers.ShouldBe(bendLengths[i], SameLengthMicrometers);
    }

    /// <summary>The lock "Equalize lengths" sets when meanders on the routes win: the arches cannot reach it.</summary>
    [Fact]
    public async Task BendThenAuto_LockAtTheLongestRoute_KeepsEveryRouteOnIt_WithoutAStatus()
    {
        var (canvas, connections, commands) = await CreateLockedFanInAsync(lengths => lengths.Max());
        double target = connections[0].Connection.TargetLengthMicrometers!.Value;
        var statuses = new List<string>();
        var routing = new ConnectionRoutingViewModel(canvas, commands) { UpdateStatus = statuses.Add };
        await SetStyleAsync(canvas, routing, connections, WaveguideType.Bend);

        routing.SelectedStyle = WaveguideType.Auto;
        connections.ShouldAllBe(c => c.Connection.Type == WaveguideType.Bend, "the layout runs off the calling thread");
        await WaitForStyleAsync(canvas, connections, WaveguideType.Auto);

        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == target);
        connections.ShouldAllBe(c => !LengthLockActions.IsViolated(c));
        connections.ShouldAllBe(c => c.Connection.Type == WaveguideType.Auto && c.Connection.IsRouteFrozen);
        statuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task BendThenAuto_LockBetweenTheShortestAndLongestRoute_KeepsTheReachableOnIt_AndReportsTheRest()
    {
        var (canvas, connections, commands) = await CreateLockedFanInAsync(lengths => (lengths.Min() + lengths.Max()) / 2);
        double target = connections[0].Connection.TargetLengthMicrometers!.Value;
        var statuses = new List<string>();
        var routing = new ConnectionRoutingViewModel(canvas, commands) { UpdateStatus = statuses.Add };
        await SetStyleAsync(canvas, routing, connections, WaveguideType.Bend);

        await SetStyleAsync(canvas, routing, connections, WaveguideType.Auto);

        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == target);
        connections.ShouldContain(c => !LengthLockActions.IsViolated(c), "the shorter routes are meandered onto the lock");
        connections.ShouldContain(c => LengthLockActions.IsViolated(c), "the longer routes cannot be shortened to it");
        statuses.ShouldHaveSingleItem().ShouldContain(target.ToString("F2", CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// A routed fan-in, all selected and locked to the length <paramref name="lockOf"/> picks from
    /// the routed lengths, each carrying the meander radius "Equalize lengths" stamps.
    /// </summary>
    private static async Task<(DesignCanvasViewModel, List<WaveguideConnectionViewModel>, CommandManager)>
        CreateLockedFanInAsync(Func<IReadOnlyList<double>, double> lockOf)
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(ChannelCount);
        var commands = new CommandManager();
        canvas.CommandManager = commands;
        double target = lockOf(connections.Select(c => c.Connection.PathLengthMicrometers).ToList());
        foreach (var connection in connections)
        {
            connection.Connection.TargetLengthMicrometers = target;
            connection.Connection.LengthToleranceMicrometers = Tolerance;
            connection.Connection.BendRadiusMicrometers = StampedMeanderRadius;
            canvas.Selection.SelectedConnections.Add(connection);
        }
        return (canvas, connections, commands);
    }

    private static Task SetStyleAsync(DesignCanvasViewModel canvas, ConnectionRoutingViewModel routing,
        List<WaveguideConnectionViewModel> connections, WaveguideType style)
    {
        routing.SelectedStyle = style;
        return WaitForStyleAsync(canvas, connections, style);
    }

    private static async Task WaitForStyleAsync(DesignCanvasViewModel canvas,
        List<WaveguideConnectionViewModel> connections, WaveguideType style)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline && connections.Any(c => c.Connection.Type != style))
            await Task.Delay(20);
        connections.ShouldAllBe(c => c.Connection.Type == style, $"the restyle to {style} must land within {Patience}");
        await canvas.RecalculateRoutesAsync();
    }
}
