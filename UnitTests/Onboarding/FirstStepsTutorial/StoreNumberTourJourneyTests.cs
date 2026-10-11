using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Home;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using CommunityToolkit.Mvvm.Input;
using Shouldly;
using UnitTests.Integration.RamScale;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Acceptance for the "Store a number in light" guided tour (issue #1422, slice 5
/// of #769): clicking the Home card's tour entry opens the shipped RAM 2x4
/// example (same callback wiring MainViewModel.StartStoreNumberTourAsync uses),
/// then walking the steps with the real commands — Build, pick D0+D2
/// (= 5), LOAD + one clock edge, LOAD off, address away and back — ends on the
/// closing step with the Q bus reading back exactly the word that was written.
/// </summary>
public class StoreNumberTourJourneyTests
{
    [Fact]
    public async Task HomeEntry_OpensExample_DriveLikeAUser_QReadsBackTheStoredWord()
    {
        var preferences = new UserPreferencesService(
            Path.Combine(Path.GetTempPath(), $"test-storenumber-journey-prefs-{Guid.NewGuid():N}.json"));
        var home = new HomeViewModel(
            new RecentProjectsService(preferences),
            preferences,
            new ExampleDesignsService());

        var entry = home.TourEntries.FirstOrDefault(
            e => ReferenceEquals(e.Command, home.StoreNumberTourCommand));
        entry.ShouldNotBeNull("the Learn-Lunima list must carry the Store-a-number tour command");

        // Wire the callback exactly like MainViewModel.StartStoreNumberTourAsync:
        // find the shipped example, open it as an untitled copy, start the tour —
        // but against a panel configured on the canvas this test drives.
        var canvas = new DesignCanvasViewModel();
        var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
        fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, w, h);
        var logic = new LogicPanelViewModel(new FakeLogicRunClock());
        logic.Configure(canvas);
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        home.StoreNumberTourRequested = async () =>
        {
            var ramPath = home.Examples
                .FirstOrDefault(example => Path.GetFileName(example.FilePath)
                    == StoreNumberTourViewModel.RamExampleFileName)
                ?.FilePath;
            if (ramPath == null || !await fileOps.OpenDesignAsCopyAsync(ramPath))
                return;
            await fileOps.PostLoadRouting;
            tour.Start();
        };

        await ((IAsyncRelayCommand)entry.Command).ExecuteAsync(null);

        tour.IsActive.ShouldBeTrue("the example opened, so the tour started on the build step");
        tour.CurrentStepIndex.ShouldBe(0);

        // Step 1: build the logic network.
        await logic.BuildNetworkCommand.ExecuteAsync(null);
        logic.HasNetwork.ShouldBeTrue(logic.StatusText);
        tour.CurrentStepIndex.ShouldBe(1, "the finished build must advance to the data-bits step");

        // Step 2: pick the number 5 (D0 + D2) — the address A stays off (word 0).
        Set(logic, "D0", true);
        Set(logic, "D2", true);
        tour.CurrentStepIndex.ShouldBe(2, "a chosen data bit must advance to the store step");

        // Step 3: LOAD on, one clock edge commits the word.
        Set(logic, "LOAD", true);
        logic.StepClockCommand.Execute(null);
        ReadQWord(logic).ShouldBe(5, "Q shows the committed word right after the clock edge");
        tour.CurrentStepIndex.ShouldBe(3, "the committed clock step must advance to the read-back step");

        // Step 4: LOAD off, switch the address away and back — the cell held 5.
        Set(logic, "LOAD", false);
        Set(logic, "A", true);
        tour.CurrentStepIndex.ShouldBe(3, "word 1 reads 0 — the stored word is not on Q yet");
        Set(logic, "A", false);
        tour.CurrentStepIndex.ShouldBe(4, "back at the stored address with Q == stored, the tour reaches the closing step");

        ReadQWord(logic).ShouldBe(5, "the Q bus must read back exactly the word that was written");
        tour.IsCompleted.ShouldBeFalse("step 5 is the wrap-up — it completes via Next");

        tour.NextCommand.Execute(null);
        tour.IsCompleted.ShouldBeTrue();
        tour.IsActive.ShouldBeFalse();
    }

    /// <summary>Toggles one network input exactly like the panel's toggle row.</summary>
    private static void Set(LogicPanelViewModel logic, string pinName, bool on) =>
        logic.Inputs.Single(i => i.PinName == pinName).IsOn = on;

    /// <summary>Reads the Q bus (Q0–Q3) as a decimal word.</summary>
    private static int ReadQWord(LogicPanelViewModel logic)
    {
        var word = 0;
        for (var bit = 0; bit < 4; bit++)
            if (logic.Outputs.FirstOrDefault(o => o.PinName == "Q" + bit)?.IsOne == true)
                word |= 1 << bit;
        return word;
    }

    private static AnalysisDockViewModel MakeDock() =>
        new(new TimeDomainViewModel(), new EyeDiagramViewModel(),
            new WavelengthSpectrumViewModel(), new AnalysisOutputPanelViewModel(),
            new MonteCarloViewModel(), new CircuitOptimizationViewModel(new CommandManager()));

    /// <summary>Manually fired clock — the panel requires an instance, the tour never fires ticks.</summary>
    private sealed class FakeLogicRunClock : ILogicRunClock
    {
        public event EventHandler? Tick;

        public void Start(TimeSpan interval)
        {
        }

        public void Stop()
        {
        }

        public void FireTick() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
