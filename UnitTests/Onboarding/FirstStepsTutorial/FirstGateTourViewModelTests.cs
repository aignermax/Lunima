using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Home;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis.MakeGate;
using Xunit;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// The "Your first gate" tour engine: seven steps driven headlessly on the shipped
/// gate kit — read the parts, group them, open Make gate, simulate, pick NAND, save,
/// build in the Logic tab — each advancing exactly on the real app state.
/// </summary>
public class FirstGateTourViewModelTests
{
    [Fact]
    public void InitialState_IsInactive_WithSevenSteps()
    {
        var tour = new FirstGateTourViewModel(new DesignCanvasViewModel(), new MakeGateViewModel(), new LogicPanelViewModel());

        tour.IsActive.ShouldBeFalse();
        tour.Steps.Count.ShouldBe(7);
        tour.Steps.Select(s => s.TargetName).ShouldBe(new[]
        {
            null, null, "MakeGateButton", "MakeGateSimulateButton",
            "MakeGateFunctionPicker", "MakeGateSaveButton", "MakeGateOpenLogicButton",
        });
    }

    [Fact]
    public async Task FullWalkthrough_OnGateKit_CompletesTheTour()
    {
        var canvas = await GateKitFixture.LoadAsync(FirstGateTourViewModel.GateKitExampleFileName);
        var makeGate = new MakeGateViewModel();
        var logic = new LogicPanelViewModel();
        logic.Configure(canvas);
        var tour = new FirstGateTourViewModel(canvas, makeGate, logic);

        tour.Start();
        tour.CurrentStepIndex.ShouldBe(0, "the intro card waits for Next");
        tour.Next();
        tour.CurrentStepIndex.ShouldBe(1, "grouping is next");

        var groupVm = GateKitFixture.GroupEverything(canvas);
        tour.CurrentStepIndex.ShouldBe(2, "a group on the canvas completes the grouping step");

        canvas.Selection.SelectSingle(groupVm);
        makeGate.ConfigureForSelection(groupVm, canvas);
        makeGate.OpenCommand.Execute(null);
        tour.CurrentStepIndex.ShouldBe(3, "opening Make gate completes its step");

        await makeGate.SimulateCommand.ExecuteAsync(null);
        tour.CurrentStepIndex.ShouldBe(4, "a simulated table completes the simulate step");

        makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.Nand);
        tour.CurrentStepIndex.ShouldBe(5, "NAND with a clean threshold completes the function step");

        makeGate.SaveCommand.Execute(null);
        tour.CurrentStepIndex.ShouldBe(6, "saving completes the save step");

        await logic.BuildNetworkCommand.ExecuteAsync(null);
        tour.IsCompleted.ShouldBeTrue(logic.StatusText);
        tour.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task FunctionStep_DoesNotAdvanceOnAWrongFunction()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = new MakeGateViewModel();
        var tour = new FirstGateTourViewModel(canvas, makeGate, new LogicPanelViewModel());
        canvas.Selection.SelectSingle(groupVm);
        makeGate.ConfigureForSelection(groupVm, canvas);
        makeGate.OpenCommand.Execute(null);
        await makeGate.SimulateCommand.ExecuteAsync(null);
        tour.Start();
        tour.Next();
        tour.CurrentStepIndex.ShouldBe(4, "already-done steps are skipped");

        makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.And);

        tour.CurrentStepIndex.ShouldBe(4, "AND does not match the NAND kit");
    }

    [Fact]
    public void Skip_DeactivatesWithoutCompleting()
    {
        var tour = new FirstGateTourViewModel(new DesignCanvasViewModel(), new MakeGateViewModel(), new LogicPanelViewModel());
        tour.Start();

        tour.Skip();

        tour.IsActive.ShouldBeFalse();
        tour.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task HomeEntry_InvokesTheFirstGateCallback()
    {
        var preferencesPath = Path.Combine(Path.GetTempPath(), $"test-firstgate-prefs-{Guid.NewGuid():N}.json");
        var examplesBase = Path.Combine(Path.GetTempPath(), $"test-firstgate-noexamples-{Guid.NewGuid():N}");
        Directory.CreateDirectory(examplesBase);
        try
        {
            var home = new HomeViewModel(
                new RecentProjectsService(new UserPreferencesService(preferencesPath)),
                new UserPreferencesService(preferencesPath),
                new ExampleDesignsService(examplesBase));
            var invoked = 0;
            home.FirstGateTourRequested = () => { invoked++; return Task.CompletedTask; };

            await home.FirstGateTourCommand.ExecuteAsync(null);

            invoked.ShouldBe(1);
            home.TourEntries[1].NameKey.ShouldBe("Home.FirstGateTour", "the gate tour comes before 'Watch it compute'");
        }
        finally
        {
            if (File.Exists(preferencesPath))
                File.Delete(preferencesPath);
            Directory.Delete(examplesBase, recursive: true);
        }
    }

    [Fact]
    public async Task GateKitExample_ShipsLooseWiredParts()
    {
        var canvas = await GateKitFixture.LoadAsync(FirstGateTourViewModel.GateKitExampleFileName);

        canvas.Components.Any(c => c.Component is ComponentGroup).ShouldBeFalse("the kit is not grouped yet");
        canvas.Components.Select(c => c.Component.Identifier)
            .ShouldBe(new[] { "combine", "phase", "recombine", "output" }, ignoreOrder: true);
        canvas.Connections.Count.ShouldBe(3, "combiner → recombiner, bias → recombiner, recombiner → output");
    }
}
