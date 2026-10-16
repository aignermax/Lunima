using System.Diagnostics;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Diagnostics;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Dies.DieWorkspaceRules;

/// <summary>
/// Releasing a dragged die must not stall the UI thread: the carrier's routing grid (a second
/// or more on a full-adder sized design) is rebuilt in the background, the drop itself stays
/// within the 100 ms interaction budget.
/// </summary>
[Collection("LocalizationSingleton")]
public class DieDropResponsivenessTests
{
    private const string ExampleFileName = "Logic Gate Full Adder.lun";
    private const double DragMicrometers = 3000;

    [AvaloniaFact]
    public async Task DroppingAMovedDie_StaysUnderTheBudget_AndRebuildsTheGridInTheBackground()
    {
        var vm = await LoadAssemblyAsync();
        var dieVm = vm.Canvas.Components.Last(c => c.Component is ComponentGroup { IsDie: true });
        var die = (ComponentGroup)dieVm.Component;

        long best = long.MaxValue;
        for (int round = 0; round < 2; round++)
        {
            vm.Canvas.Selection.SelectSingle(dieVm);
            vm.Canvas.SelectedComponent = dieVm;
            vm.CanvasInteraction.StartMoveComponent(dieVm);
            vm.Canvas.MoveComponent(dieVm, 0, DragMicrometers);
            var watch = Stopwatch.StartNew();
            vm.CanvasInteraction.EndMoveComponent();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            watch.Stop();
            best = Math.Min(best, watch.ElapsedMilliseconds);
            await vm.Canvas.Dies.PendingGridRebuild;
        }

        best.ShouldBeLessThan(UiThreadWatchdog.BudgetMs, "the die drop must not block the UI thread");
        var grid = vm.Canvas.Router.PathfindingGrid.ShouldNotBeNull();
        grid.MaxY.ShouldBeGreaterThanOrEqualTo(DieAssembly.OutlineOf(die).Bottom,
            "the background rebuild covers the carrier around the moved die");
    }

    [AvaloniaFact]
    public async Task UndoOfADieMove_StaysUnderTheBudget()
    {
        var vm = await LoadAssemblyAsync();
        var dieVm = vm.Canvas.Components.Last(c => c.Component is ComponentGroup { IsDie: true });
        double startY = dieVm.Y;
        vm.CanvasInteraction.StartMoveComponent(dieVm);
        vm.Canvas.MoveComponent(dieVm, 0, DragMicrometers);
        vm.CanvasInteraction.EndMoveComponent();
        await vm.Canvas.Dies.PendingGridRebuild;

        long ms = UiThreadWatchdog.Measure("Undo die move", () => vm.UndoCommand.Execute(null));
        await vm.UndoCommand.ExecutionTask!;

        dieVm.Y.ShouldBe(startY, 1e-6);
        ms.ShouldBeLessThan(UiThreadWatchdog.BudgetMs, "undo re-bounds the carrier in the background too");
        await vm.Canvas.Dies.PendingGridRebuild;
    }

    private static async Task<MainViewModel> LoadAssemblyAsync()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        (await vm.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue();
        vm.NewDie.Prepare();
        vm.NewDie.CreateCommand.Execute(null);
        vm.WorkspaceSwitch.Current = Workspace.System;
        vm.Canvas.Dies.IsAssemblyRoot.ShouldBeTrue();
        return vm;
    }
}
