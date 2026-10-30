using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using System.Text.Json;
using UnitTests.Dies.DieWorkspaceRules;
using UnitTests.Dies.WorkSurface;
using Xunit;

namespace UnitTests.AgentBridge;

[Collection("LocalizationSingleton")]
public sealed class AgentUndoDieBoundsTests
{
    [AvaloniaFact]
    public async Task UndoTool_WiredDieMove_RebuildsRestoredCarrierAfterItsDocumentCommit()
    {
        var fixture = new DieWorkspaceFixture();
        await using var vm = fixture.Vm;
        vm.WorkspaceSwitch.Current = Workspace.System;
        var canvas = vm.Canvas;
        await canvas.RecalculateRoutesAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await canvas.Dies.PendingGridRebuild.WaitAsync(TimeSpan.FromSeconds(15));
        var wire = canvas.Connections.ShouldHaveSingleItem().Connection;
        var originalRoute = WorkSurfaceCanvas.Geometry(wire.RoutedPath);
        var die = fixture.DieVm(fixture.Die1);
        var startY = die.Y;
        fixture.Select(die);
        vm.CanvasInteraction.StartMoveComponent(die);
        canvas.MoveComponent(die, 0, 3000);
        vm.CanvasInteraction.EndMoveComponent();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        await canvas.Dies.PendingGridRebuild.WaitAsync(TimeSpan.FromSeconds(15));
        var movedBottom = canvas.Router.PathfindingGrid.ShouldNotBeNull().MaxY;

        var session = new AgentDesignSession(canvas, vm.CommandManager, () => vm.FileOperations, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()));
        var tool = new UndoTool(session);
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        try
        {
            var result = await catalog.InvokeAsync(tool.Name, JsonSerializer.SerializeToElement(new { }))
                .WaitAsync(TimeSpan.FromSeconds(15));
            result.IsError.ShouldBeFalse();
            await canvas.Dies.PendingGridRebuild.WaitAsync(TimeSpan.FromSeconds(15));

            die.Y.ShouldBe(startY, 1e-6);
            WorkSurfaceCanvas.Geometry(wire.RoutedPath).ShouldBe(originalRoute,
                "exact move undo must not search for a different route");
            var bounds = canvas.Dies.CurrentBounds();
            bounds.Bottom.ShouldBeLessThan(movedBottom);
            canvas.Router.PathfindingGrid.ShouldNotBeNull().MaxY.ShouldBe(bounds.Bottom, 1e-6);
        }
        finally { await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
    }
}
