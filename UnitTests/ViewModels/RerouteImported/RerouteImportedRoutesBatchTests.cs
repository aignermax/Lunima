using UnitTests.ViewModels.Canvas;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.RerouteImported;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.RerouteImported;

public partial class RerouteImportedRoutesViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RerouteGroup_StopOrScopeSwitch_DoesNotStartTheNextGroup(bool switchScope)
    {
        var canvas = ReportedRoutingWorker.CreateCanvas();
        var commands = new CommandManager();
        var groups = new List<ComponentGroup>();
        for (int i = 0; i < 2; i++)
        {
            var previous = canvas.Components.ToHashSet();
            var (start, end) = AddComponentPair(canvas, i * 2000);
            ConnectFrozenImported(canvas, start, end);
            commands.ExecuteCommand(new CreateGroupCommand(canvas,
                canvas.Components.Where(c => !previous.Contains(c)).ToList(), "Imported"));
            await canvas.RecalculateRoutesAsync();
            groups.Add((ComponentGroup)canvas.Components.Last().Component);
        }
        var routing = (ReportedRoutingWorker)canvas.Routing;
        routing.BlockWorker = true;
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        var reroute = vm.RerouteAllCommand.ExecuteAsync(null);
        try
        {
            await routing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            canvas.CurrentEditGroup.ShouldBeSameAs(groups[0]);
            if (switchScope)
            {
                canvas.ExitGroupEditMode();
                canvas.EnterGroupEditMode(groups[1]);
            }
            else canvas.StopRouting();
        }
        finally { routing.Release.TrySetResult(); await reroute.WaitAsync(TimeSpan.FromSeconds(30)); }
        canvas.CurrentEditGroup.ShouldBeSameAs(switchScope ? groups[1] : null);
        groups[1].InternalPaths.Single().IsRouteFrozen.ShouldBeTrue();
        groups[1].InternalPaths.Single().Path.Segments.Count.ShouldBe(DetourSegmentCount);
        commands.UndoCount.ShouldBe(3);
        if (switchScope) canvas.ExitGroupEditMode();
        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        groups[0].InternalPaths.Single().IsRouteFrozen.ShouldBeTrue();
        groups[0].InternalPaths.Single().Path.Segments.Count.ShouldBe(DetourSegmentCount);
    }
}
