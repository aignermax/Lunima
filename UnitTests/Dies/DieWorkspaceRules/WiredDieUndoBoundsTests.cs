using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using Xunit;

namespace UnitTests.Dies.DieWorkspaceRules;

[Collection("LocalizationSingleton")]
public sealed class WiredDieUndoBoundsTests
{
    [AvaloniaFact]
    public async Task UndoOfWiredDieMove_PublishesGridForRestoredCarrierBounds()
    {
        var fixture = new DieWorkspaceFixture();
        await using var vm = fixture.Vm;
        vm.WorkspaceSwitch.Current = Workspace.System;
        var canvas = vm.Canvas;
        await canvas.RecalculateRoutesAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await canvas.Dies.PendingGridRebuild.WaitAsync(TimeSpan.FromSeconds(15));
        canvas.Connections.ShouldNotBeEmpty("route restoration must publish real wire changes during undo");
        var dieVm = fixture.DieVm(fixture.Die1);
        var startY = dieVm.Y;

        fixture.Select(dieVm);
        vm.CanvasInteraction.StartMoveComponent(dieVm);
        canvas.MoveComponent(dieVm, 0, 3000);
        vm.CanvasInteraction.EndMoveComponent();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        await canvas.Dies.PendingGridRebuild.WaitAsync(TimeSpan.FromSeconds(15));
        var movedBottom = canvas.Router.PathfindingGrid.ShouldNotBeNull().MaxY;

        await vm.UndoCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(15));
        await canvas.Dies.PendingGridRebuild.WaitAsync(TimeSpan.FromSeconds(15));

        dieVm.Y.ShouldBe(startY, 1e-6);
        var bounds = canvas.Dies.CurrentBounds();
        bounds.Bottom.ShouldBeLessThan(movedBottom, "undo contracts the carrier instead of retaining the moved bounds");
        var grid = canvas.Router.PathfindingGrid.ShouldNotBeNull();
        grid.MinX.ShouldBe(bounds.X, 1e-6);
        grid.MinY.ShouldBe(bounds.Y, 1e-6);
        grid.MaxX.ShouldBe(bounds.Right, 1e-6);
        grid.MaxY.ShouldBe(bounds.Bottom, 1e-6,
            "wire restoration must finish before admitting the grid rebuild for the completed undo");
    }
}
