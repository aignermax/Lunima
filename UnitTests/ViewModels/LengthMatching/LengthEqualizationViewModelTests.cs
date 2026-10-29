using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using Xunit;

namespace UnitTests.ViewModels.LengthMatching;

/// <summary>
/// "Equalize lengths" for a multi-selection: shown for two or more selected connections, gives
/// them all the longest one's length as ONE undo step, runs off the UI thread, and refuses a
/// common length shorter than the longest route.
/// </summary>
public class LengthEqualizationViewModelTests
{
    private const double Tolerance = 0.1;

    private static async Task<(DesignCanvasViewModel Canvas, List<WaveguideConnectionViewModel> Connections,
        CommandManager Commands, LengthEqualizationViewModel Vm)> CreateAsync(int count)
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(count);
        var commands = new CommandManager();
        canvas.CommandManager = commands;
        var vm = new LengthEqualizationViewModel(canvas);
        foreach (var connection in connections)
            canvas.Selection.SelectedConnections.Add(connection);
        return (canvas, connections, commands, vm);
    }

    [Fact]
    public async Task Section_IsShownOnlyForSeveralConnections()
    {
        var (canvas, connections, _, vm) = await CreateAsync(3);
        vm.HasSeveralConnections.ShouldBeTrue();
        vm.SummaryText.ShouldContain("3");

        canvas.Selection.SelectedConnections.Clear();
        canvas.Selection.SelectedConnections.Add(connections[0]);

        vm.HasSeveralConnections.ShouldBeFalse();
    }

    [Fact]
    public async Task Equalize_GivesEverySelectedRouteTheLongestLength_AsOneUndoStep()
    {
        var (_, connections, commands, vm) = await CreateAsync(6);
        vm.RerouteAsBundle = false;
        var before = connections.Select(c => c.Connection.PathLengthMicrometers).ToList();
        double longest = before.Max();

        await vm.EqualizeCommand.ExecuteAsync(null);

        vm.HasErrorStatus.ShouldBeFalse(vm.StatusMessage);
        connections.ShouldAllBe(c => Math.Abs(c.Connection.PathLengthMicrometers - longest) <= Tolerance);
        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == longest && c.Connection.IsRouteFrozen);
        commands.UndoCount.ShouldBe(1);

        commands.Undo().ShouldBeTrue();
        for (int i = 0; i < connections.Count; i++)
            connections[i].Connection.PathLengthMicrometers.ShouldBe(before[i], 1e-6);
        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == null);

        commands.Redo().ShouldBeTrue();
        connections.ShouldAllBe(c => Math.Abs(c.Connection.PathLengthMicrometers - longest) <= Tolerance);
    }

    [Fact]
    public async Task Equalize_UnifiesTheWidth_AndUndoRestoresWidthAndRoutes()
    {
        var (_, connections, commands, vm) = await CreateAsync(6);
        double common = connections[1].Connection.WidthMicrometers;
        connections[0].Connection.WidthMicrometers = common + 1.5;
        var before = connections.Select(c => c.Connection.PathLengthMicrometers).ToList();

        await vm.EqualizeCommand.ExecuteAsync(null);

        vm.HasSuccessStatus.ShouldBeTrue(vm.StatusMessage);
        double length = connections[0].Connection.PathLengthMicrometers;
        connections.ShouldAllBe(c => Math.Abs(c.Connection.PathLengthMicrometers - length) <= Tolerance);
        connections.ShouldAllBe(c => c.Connection.WidthMicrometers == common);
        commands.UndoCount.ShouldBe(1);

        commands.Undo().ShouldBeTrue();
        connections[0].Connection.WidthMicrometers.ShouldBe(common + 1.5);
        for (int i = 0; i < connections.Count; i++)
            connections[i].Connection.PathLengthMicrometers.ShouldBe(before[i], 1e-6);
    }

    [Fact]
    public async Task Equalize_TypedLengthBelowTheLongest_IsRefused()
    {
        var (_, connections, commands, vm) = await CreateAsync(3);
        vm.TargetLengthText = "10";

        await vm.EqualizeCommand.ExecuteAsync(null);

        vm.HasErrorStatus.ShouldBeTrue();
        commands.UndoCount.ShouldBe(0);
        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == null);
    }

    [Fact]
    public async Task Equalize_InvalidRadius_IsRefused()
    {
        var (_, _, commands, vm) = await CreateAsync(2);
        vm.MaxRadiusText = "abc";

        await vm.EqualizeCommand.ExecuteAsync(null);

        vm.HasErrorStatus.ShouldBeTrue();
        commands.UndoCount.ShouldBe(0);
    }

    [Fact]
    public async Task Equalize_RunsOffTheCallingThread()
    {
        var (_, _, _, vm) = await CreateAsync(6);

        var running = vm.EqualizeCommand.ExecuteAsync(null);
        bool busyWhileRunning = vm.IsBusy;
        await running;

        busyWhileRunning.ShouldBeTrue("the search must not block the caller until it is done");
        vm.IsBusy.ShouldBeFalse();
    }
}
