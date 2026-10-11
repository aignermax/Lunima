using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using UnitTests.Integration;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Tests for the "Store a number in light" guided-tour engine (issue #1422, slice
/// 5 of #769): the tour observes the real Logic panel signals of the shipped RAM
/// 2x4 example — network built, a data bit picked, the word committed on a clock
/// edge, the address switched away and back with the stored word reappearing on
/// Q — and advances exactly on those, never on arbitrary clicks. Drives the
/// engine against a real <see cref="LogicPanelViewModel"/> built over the shared
/// loaded example (no mocks of the logic engine), headless.
/// </summary>
[Collection("LocalizationSingleton")]
public class StoreNumberTourViewModelTests
    : IClassFixture<LogicGateRam2x4ExampleTests.Ram2x4Fixture>, IDisposable
{
    private readonly LogicGateRam2x4ExampleTests.Ram2x4Fixture _fixture;
    private readonly string _previousLanguage;

    /// <summary>Attaches the shared loaded RAM 2x4 example; pins English for text assertions.</summary>
    public StoreNumberTourViewModelTests(LogicGateRam2x4ExampleTests.Ram2x4Fixture fixture)
    {
        _fixture = fixture;
        _previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
    }

    /// <summary>Restores the language that was active before the test.</summary>
    public void Dispose() => LocalizationService.Instance.SetLanguage(_previousLanguage);

    /// <summary>A panel over the shared example canvas with the network already built.</summary>
    private async Task<LogicPanelViewModel> CreateBuiltPanel()
    {
        var logic = CreateUnbuiltPanel();
        await logic.BuildNetworkCommand.ExecuteAsync(null);
        logic.HasNetwork.ShouldBeTrue(logic.StatusText);
        logic.HasRegisters.ShouldBeTrue("the RAM 2x4 has eight register bits, so Step is enabled");
        return logic;
    }

    /// <summary>A panel configured against the shared example canvas, not yet built.</summary>
    private LogicPanelViewModel CreateUnbuiltPanel()
    {
        var logic = new LogicPanelViewModel(new FakeLogicRunClock());
        logic.Configure(_fixture.Canvas);
        return logic;
    }

    private static AnalysisDockViewModel MakeDock() =>
        new(new TimeDomainViewModel(), new EyeDiagramViewModel(),
            new WavelengthSpectrumViewModel(), new AnalysisOutputPanelViewModel(),
            new MonteCarloViewModel(), new CircuitOptimizationViewModel(new CommandManager()));

    /// <summary>Toggles one network input exactly like the panel's toggle row.</summary>
    private static void Set(LogicPanelViewModel logic, string pinName, bool on) =>
        logic.Inputs.Single(i => i.PinName == pinName).IsOn = on;

    /// <summary>Reads the Q bus (Q0–Q3) as a decimal word, like the tour's predicate.</summary>
    private static int ReadQWord(LogicPanelViewModel logic)
    {
        var word = 0;
        for (var bit = 0; bit < 4; bit++)
            if (logic.Outputs.FirstOrDefault(o => o.PinName == "Q" + bit)?.IsOne == true)
                word |= 1 << bit;
        return word;
    }

    [Fact]
    public void InitialState_IsInactive_AtFirstStep()
    {
        var tour = new StoreNumberTourViewModel(CreateUnbuiltPanel(), MakeDock());

        tour.IsActive.ShouldBeFalse();
        tour.IsCompleted.ShouldBeFalse();
        tour.CurrentStepIndex.ShouldBe(0);
        tour.Steps.Count.ShouldBe(5);
    }

    [Fact]
    public void Start_OpensAnalysisDock_OnLogicTab()
    {
        var dock = MakeDock();
        var tour = new StoreNumberTourViewModel(CreateUnbuiltPanel(), dock);

        tour.Start();

        dock.IsVisible.ShouldBeTrue("the Logic panel lives in the analysis dock (#1183)");
        dock.SelectedTabIndex.ShouldBe(8, "the Logic tab is the last tab of the analysis dock");
    }

    [Fact]
    public void Start_ActivatesTour_AtBuildStep()
    {
        var tour = new StoreNumberTourViewModel(CreateUnbuiltPanel(), MakeDock());

        tour.Start();

        tour.IsActive.ShouldBeTrue();
        tour.CurrentStepIndex.ShouldBe(0);
        tour.ProgressText.ShouldContain("1/5");
        tour.CurrentTitle.ShouldNotBeNullOrWhiteSpace();
        tour.CurrentTitle.ShouldNotBe(tour.CurrentStep.TitleKey, "title must be localized, not the raw key");
        tour.CurrentBody.ShouldNotBe(tour.CurrentStep.BodyKey, "body must be localized, not the raw key");
    }

    [Fact]
    public async Task Start_OnUnbuiltPanel_WaitsAtBuildStep_UntilTheBuildLands()
    {
        var logic = CreateUnbuiltPanel();
        logic.HasNetwork.ShouldBeFalse("nothing was built yet");
        var tour = new StoreNumberTourViewModel(logic, MakeDock());

        tour.Start();

        tour.CurrentStepIndex.ShouldBe(0, "an un-built panel cannot satisfy the build step");

        await logic.BuildNetworkCommand.ExecuteAsync(null);

        logic.HasNetwork.ShouldBeTrue(logic.StatusText);
        tour.CurrentStepIndex.ShouldBe(1, "the finished build must advance to the data-bits step");
    }

    [Fact]
    public async Task DataBitStep_NoDataBitOn_Waits_FirstDataBitAdvances()
    {
        var logic = await CreateBuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        tour.CurrentStepIndex.ShouldBe(1, "the built network satisfies the build step immediately");

        foreach (var bit in new[] { "D0", "D1", "D2", "D3" })
            logic.Inputs.Single(i => i.PinName == bit).IsOn.ShouldBeFalse();
        tour.CurrentStepIndex.ShouldBe(1, "with every data bit off the tour keeps waiting");

        Set(logic, "D0", true);
        tour.CurrentStepIndex.ShouldBe(2, "the first data bit advances to the store step");

        // A second data bit changes nothing about the step the user is on.
        Set(logic, "D2", true);
        tour.CurrentStepIndex.ShouldBe(2);
    }

    [Fact]
    public async Task StoreStep_LoadWithoutClock_Waits_ClockCommitAdvances()
    {
        var logic = await CreateBuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        Set(logic, "D0", true);
        Set(logic, "D2", true);
        tour.CurrentStepIndex.ShouldBe(2);

        Set(logic, "LOAD", true);
        tour.CurrentStepIndex.ShouldBe(2, "LOAD alone stores nothing — the clock edge commits");

        logic.StepClockCommand.Execute(null);

        logic.ClockStepCount.ShouldBe(1);
        ReadQWord(logic).ShouldBe(5, "Q combinationally reads the word just committed (D0+D2 = 5)");
        tour.CurrentStepIndex.ShouldBe(3, "the committed clock step must advance to the read-back step");
    }

    [Fact]
    public async Task StoreStep_QuietClock_DoesNotAdvance()
    {
        var logic = await CreateBuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        Set(logic, "D0", true);
        tour.CurrentStepIndex.ShouldBe(2);

        // LOAD off: the clock commits nothing new — the tour honestly waits.
        logic.StepClockCommand.Execute(null);

        tour.CurrentStepIndex.ShouldBe(2, "a clock edge that stores nothing must not count");
    }

    [Fact]
    public async Task ReadBackStep_WaitsUntilLoadOff_AddressLeft_AndAddressBack()
    {
        var logic = await CreateBuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        Set(logic, "D0", true);
        Set(logic, "D2", true);
        Set(logic, "LOAD", true);
        logic.StepClockCommand.Execute(null);
        tour.CurrentStepIndex.ShouldBe(3);

        // LOAD still on: the word is not yet "held" — the step waits.
        tour.CurrentStepIndex.ShouldBe(3);

        Set(logic, "LOAD", false);
        tour.CurrentStepIndex.ShouldBe(3, "the address has not left the stored word yet");

        Set(logic, "A", true);
        tour.CurrentStepIndex.ShouldBe(3, "the address moved away — word 1 reads 0, not the stored word");
        ReadQWord(logic).ShouldBe(0, "word 1 was never written");

        Set(logic, "A", false);
        ReadQWord(logic).ShouldBe(5, "back at word 0 the cell answers with the stored number");
        tour.CurrentStepIndex.ShouldBe(4, "LOAD off + address away and back + Q == stored advances to the closing step");
        tour.IsCompleted.ShouldBeFalse("the closing words step completes via Next");
    }

    [Fact]
    public async Task Next_OnClosingStep_CompletesTour()
    {
        var logic = await CreateBuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        Set(logic, "D0", true);
        Set(logic, "D2", true);
        Set(logic, "LOAD", true);
        logic.StepClockCommand.Execute(null);
        Set(logic, "LOAD", false);
        Set(logic, "A", true);
        Set(logic, "A", false);
        tour.CurrentStepIndex.ShouldBe(4);

        tour.NextCommand.Execute(null);

        tour.IsCompleted.ShouldBeTrue();
        tour.IsActive.ShouldBeFalse("the overlay hides once the tour completes");
    }

    [Fact]
    public async Task FullTour_BuildPickStoreReadBack_AdvancesAutomatically()
    {
        var logic = CreateUnbuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        tour.CurrentStepIndex.ShouldBe(0);

        await logic.BuildNetworkCommand.ExecuteAsync(null);
        tour.CurrentStepIndex.ShouldBe(1);

        Set(logic, "D0", true);
        Set(logic, "D2", true);
        tour.CurrentStepIndex.ShouldBe(2);

        Set(logic, "LOAD", true);
        logic.StepClockCommand.Execute(null);
        tour.CurrentStepIndex.ShouldBe(3);

        Set(logic, "LOAD", false);
        Set(logic, "A", true);
        Set(logic, "A", false);
        tour.CurrentStepIndex.ShouldBe(4);
        tour.IsActive.ShouldBeTrue("step 5 is the wrap-up — it waits for Next");

        ReadQWord(logic).ShouldBe(5, "the read-back shows exactly the word that was written");
    }

    [Fact]
    public async Task Skip_ExitsTour_LaterPanelChangesDoNotAdvance()
    {
        var logic = await CreateBuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        tour.SkipCommand.Execute(null);

        tour.IsActive.ShouldBeFalse();
        tour.IsCompleted.ShouldBeFalse();

        Set(logic, "D0", true);
        Set(logic, "LOAD", true);
        logic.StepClockCommand.Execute(null);

        tour.CurrentStepIndex.ShouldBe(1, "a skipped tour must not react to the panel anymore");
        tour.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task CompletedTour_DetachesFromPanel_LaterChangesDoNotThrow()
    {
        var logic = await CreateBuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        for (var i = 0; i < 5; i++)
            tour.NextCommand.Execute(null);

        Set(logic, "D0", true);
        Set(logic, "LOAD", true);
        logic.StepClockCommand.Execute(null);

        tour.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Restart_ResetsProgress_AndReevaluates()
    {
        var logic = await CreateBuiltPanel();
        var tour = new StoreNumberTourViewModel(logic, MakeDock());
        tour.Start();
        Set(logic, "D0", true);
        tour.SkipCommand.Execute(null);

        tour.Start();

        tour.IsActive.ShouldBeTrue();
        tour.IsCompleted.ShouldBeFalse();
        tour.CurrentStepIndex.ShouldBe(2,
            "the built network and the set data bit still satisfy the first two steps");
    }

    /// <summary>
    /// Manually fired <see cref="ILogicRunClock"/> — mirrors the fake in the Logic
    /// panel's run-mode tests; the tour never fires ticks itself, but the panel
    /// requires a clock instance.
    /// </summary>
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
