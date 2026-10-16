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
    public async Task RerouteGroup_WaitsForPreviousWorkerBeforeReplacingTheCanvas(bool removeGroupWhileWaiting)
    {
        var canvas = new DesignCanvasViewModel();
        var (startA, endA) = AddComponentPair(canvas, 0);
        var (startB, endB) = AddComponentPair(canvas, 2000);
        ConnectFrozenImported(canvas, startA, endA);
        var edited = ConnectFrozenImported(canvas, startB, endB);
        edited.Connection.BendRadiusOverrides[0] = 25;
        var selected = canvas.Components.ToList();
        var (outsideStart, outsideEnd) = AddComponentPair(canvas, 4000);
        var outside = ConnectFrozenImported(canvas, outsideStart, outsideEnd).Connection;
        outside.IsRouteFrozen = false;
        outside.InvalidateRoute();
        using var parked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        canvas.Routing.BuildConnectionProcessFloorProvider = () => (_, _) =>
        {
            parked.Set();
            release.Wait();
            return null;
        };
        var commands = new CommandManager();
        var viewModel = new RerouteImportedRoutesViewModel(canvas, commands);
        commands.ExecuteCommand(new CreateGroupCommand(canvas, selected, "ImportedTopCell"));
        bool enteredGroup = false;
        canvas.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(canvas.IsInGroupEditMode) && canvas.IsInGroupEditMode)
                enteredGroup = true;
        };
        Task? reroute = null;
        try
        {
            parked.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            reroute = viewModel.RerouteAllCommand.ExecuteAsync(null);
            canvas.IsInGroupEditMode.ShouldBeFalse(
                "a live worker must finish before its connection list becomes a different canvas");
            reroute.IsCompleted.ShouldBeFalse();
            if (removeGroupWhileWaiting)
                canvas.Components.Clear();
        }
        finally
        {
            release.Set();
            if (reroute != null) await reroute;
            await canvas.RecalculateRoutesAsync();
        }
        if (removeGroupWhileWaiting)
        {
            commands.UndoCount.ShouldBe(1, "an abandoned reroute must not register stale history");
            enteredGroup.ShouldBeFalse("a removed group must never become the active canvas after the drain");
            canvas.Components.ShouldBeEmpty("an obsolete group must never restore its former canvas");
            canvas.IsInGroupEditMode.ShouldBeFalse();
            return;
        }
        var group = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();
        var kept = group.InternalPaths.Single(p => ReferenceEquals(p.StartPin, startB));
        kept.Path.Segments.Count.ShouldBe(DetourSegmentCount);
        kept.BendRadiusOverrides[0].ShouldBe(25);
    }

    [Fact]
    public async Task RerouteNestedGroup_FromOpenParent_PreservesScopeAndUndo()
    {
        var canvas = new DesignCanvasViewModel();
        var (start, end) = AddComponentPair(canvas, 0);
        ConnectFrozenImported(canvas, start, end);
        var commands = new CommandManager();
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Child"));
        await canvas.RecalculateRoutesAsync();
        var child = (ComponentGroup)canvas.Components.Single().Component;
        AddComponentPair(canvas, 2000); // A normal parent group requires at least two members.
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Parent"));
        await canvas.RecalculateRoutesAsync();
        var parent = (ComponentGroup)canvas.Components.Single().Component;
        canvas.EnterGroupEditMode(parent);
        bool enteredChild = false;
        canvas.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(canvas.CurrentEditGroup) && ReferenceEquals(canvas.CurrentEditGroup, child))
                enteredChild = true;
        };
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        await vm.RerouteAllCommand.ExecuteAsync(null);
        canvas.CurrentEditGroup.ShouldBeSameAs(parent);
        enteredChild.ShouldBeTrue("rerouting a nested target must not be refused inside its parent");
        commands.UndoCount.ShouldBe(3);
        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        child.InternalPaths.Single().Path.Segments.Count.ShouldBe(DetourSegmentCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RerouteAll_TargetRemovedDuringCanvasRouting_KeepsOnlyLiveHistory(bool replaceCanvas)
    {
        var canvas = new DesignCanvasViewModel();
        var (groupStart, groupEnd) = AddComponentPair(canvas, 2000);
        ConnectFrozenImported(canvas, groupStart, groupEnd);
        var commands = new CommandManager();
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Imported"));
        await canvas.RecalculateRoutesAsync();
        var groupVm = canvas.Components.Single();
        var (start, end) = AddComponentPair(canvas, 0);
        var route = ConnectFrozenImported(canvas, start, end).Connection;
        using var parked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        canvas.Routing.BuildConnectionProcessFloorProvider = () => (_, _) =>
        {
            parked.Set();
            release.Wait();
            return null;
        };
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        var reroute = vm.RerouteAllCommand.ExecuteAsync(null);
        try
        {
            parked.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            canvas.Components.Remove(groupVm);
            if (replaceCanvas)
            {
                canvas.Components.Clear();
                canvas.Connections.Clear();
                commands.ClearHistory();
            }
        }
        finally { release.Set(); await reroute; }
        if (replaceCanvas)
        {
            commands.UndoCount.ShouldBe(0, "old design snapshots must not enter new design history");
            canvas.Components.ShouldBeEmpty();
            canvas.Connections.ShouldBeEmpty();
        }
        else
        {
            commands.UndoCount.ShouldBe(2, "completed root routing must remain undoable");
            route.RoutedPath!.Segments.Count.ShouldBeLessThan(DetourSegmentCount);
            commands.Undo().ShouldBeTrue();
            await canvas.RecalculateRoutesAsync();
            route.RoutedPath!.Segments.Count.ShouldBe(DetourSegmentCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RerouteAll_OpeningAnotherScope_KeepsHistory(bool startInsideGroup)
    {
        var canvas = new DesignCanvasViewModel();
        var (groupStart, groupEnd) = AddComponentPair(canvas, 2000);
        ConnectFrozenImported(canvas, groupStart, groupEnd);
        var commands = new CommandManager();
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Imported"));
        await canvas.RecalculateRoutesAsync();
        var group = (ComponentGroup)canvas.Components.Single().Component;
        var (start, end) = AddComponentPair(canvas, 0);
        var route = ConnectFrozenImported(canvas, start, end).Connection;
        ComponentGroup? originalScope = null;
        if (startInsideGroup)
        {
            commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Parent"));
            await canvas.RecalculateRoutesAsync();
            originalScope = (ComponentGroup)canvas.Components.Single().Component;
            canvas.EnterGroupEditMode(originalScope);
            route = canvas.Connections.Single().Connection;
        }
        using var parked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        canvas.Routing.BuildConnectionProcessFloorProvider = () => (_, _) =>
        {
            parked.Set();
            release.Wait();
            return null;
        };
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        var reroute = vm.RerouteAllCommand.ExecuteAsync(null);
        try
        {
            parked.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            route.IsRouteFrozen.ShouldBeFalse("the root operation already changed this route");
            if (startInsideGroup) canvas.ExitGroupEditMode();
            else canvas.EnterGroupEditMode(group);
        }
        finally { release.Set(); await reroute; }
        canvas.CurrentEditGroup.ShouldBeSameAs(startInsideGroup ? null : group,
            "reroute must not steal the user's new scope");
        commands.UndoCount.ShouldBe(startInsideGroup ? 3 : 2,
            "surviving work must remain undoable in another scope");
        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        if (originalScope != null)
        {
            originalScope.InternalPaths.Single().IsRouteFrozen.ShouldBeTrue();
            originalScope.InternalPaths.Single().Path.Segments.Count.ShouldBe(DetourSegmentCount);
        }
        else
        {
            route.IsRouteFrozen.ShouldBeTrue();
            route.RoutedPath!.Segments.Count.ShouldBe(DetourSegmentCount);
        }
        canvas.ExitGroupEditMode();
        await canvas.RecalculateRoutesAsync();
    }
}
