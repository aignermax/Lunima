using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using UnitTests.Integration;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Tests for the "Run a program on your chip" guided-tour engine (issue #1267,
/// slice 3 of #769): the tour observes the real signals — Logic network built,
/// playground window opened, multiply sample picked, photonic-ADD toggle on,
/// photonic steps taken — and advances exactly on those, never on arbitrary
/// clicks. Drives the engine against the shipped 4-bit adder network (shared
/// fixture, assembled once) published through the same
/// <see cref="BuiltLogicNetworkProvider"/> hand-off the app uses, headless.
/// </summary>
[Collection("LocalizationSingleton")]
public class RunProgramTourViewModelTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>, IDisposable
{
    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;
    private readonly string _previousLanguage;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once); pins English for status-text assertions.</summary>
    public RunProgramTourViewModelTests(LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture)
    {
        _fixture = fixture;
        _previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
    }

    /// <summary>Restores the language that was active before the test.</summary>
    public void Dispose() => LocalizationService.Instance.SetLanguage(_previousLanguage);

    private (LogicPanelViewModel logic, IsaPlaygroundViewModel playground, RunProgramTourViewModel tour) CreateFixture()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var logic = new LogicPanelViewModel(new FakeLogicRunClock(), provider);
        var playground = new IsaPlaygroundViewModel(provider);
        playground.IsPhotonicAddAvailable.ShouldBeTrue("the published 4-bit adder must enable the photonic toggle");
        return (logic, playground, new RunProgramTourViewModel(logic, MakeDock(), playground));
    }

    private static AnalysisDockViewModel MakeDock() =>
        new(new TimeDomainViewModel(), new EyeDiagramViewModel(),
            new WavelengthSpectrumViewModel(), new AnalysisOutputPanelViewModel(),
            new MonteCarloViewModel(), new CircuitOptimizationViewModel(new CommandManager()));

    [Fact]
    public void InitialState_IsInactive_AtFirstStep()
    {
        var (_, _, tour) = CreateFixture();

        tour.IsActive.ShouldBeFalse();
        tour.IsCompleted.ShouldBeFalse();
        tour.CurrentStepIndex.ShouldBe(0);
        tour.Steps.Count.ShouldBe(6);
    }

    [Fact]
    public void Start_OpensAnalysisDock_OnLogicTab()
    {
        var (logic, playground, _) = CreateFixture();
        var dock = MakeDock();
        var tour = new RunProgramTourViewModel(logic, dock, playground);

        tour.Start();

        dock.IsVisible.ShouldBeTrue("the Logic panel lives in the analysis dock (#1183)");
        dock.SelectedTabIndex.ShouldBe(8, "the Logic tab is the last tab of the analysis dock");
    }

    [Fact]
    public void Start_ActivatesTour_AtBuildStep_InMainWindow()
    {
        var (_, _, tour) = CreateFixture();

        tour.Start();

        tour.IsActive.ShouldBeTrue();
        tour.CurrentStepIndex.ShouldBe(0);
        tour.ProgressText.ShouldContain("1/6");
        tour.CurrentTitle.ShouldNotBeNullOrWhiteSpace();
        tour.CurrentTitle.ShouldNotBe(tour.CurrentStep.TitleKey, "title must be localized, not the raw key");
        tour.CurrentBody.ShouldNotBe(tour.CurrentStep.BodyKey, "body must be localized, not the raw key");
        tour.ShowCardInMainWindow.ShouldBeTrue("the build step anchors in the main window");
        tour.ShowCardInPlayground.ShouldBeFalse();
    }

    [Fact]
    public void FullTour_BuildOpenPickToggleStep_Completes()
    {
        var (logic, playground, tour) = CreateFixture();
        logic.HasNetwork = true;
        tour.Start();

        tour.CurrentStepIndex.ShouldBe(1,
            "the 4-bit adder example is already built when the tour starts, so the build step is satisfied immediately");

        // Step 2 — open the ISA playground window.
        tour.NotifyPlaygroundOpened();

        tour.CurrentStepIndex.ShouldBe(2, "opening the playground must advance to the sample step");

        // Step 3 — pick "Multiply 3 by 4".
        playground.SelectedSample = playground.Samples.Single(
            s => s.FileName == RunProgramTourViewModel.MultiplySampleFileName);

        tour.CurrentStepIndex.ShouldBe(3, "picking the multiply sample must advance to the photonic-toggle step");
        playground.IsAssembled.ShouldBeTrue(playground.ErrorText);

        // Step 4 — turn on "Compute ADD on the photonic chip".
        playground.UsePhotonicAdder = true;
        playground.UsePhotonicAdder.ShouldBeTrue();

        tour.CurrentStepIndex.ShouldBe(4, "the photonic toggle must advance to the stepping step");
        tour.ShowCardInPlayground.ShouldBeTrue("the playground steps anchor in the playground window");
        tour.ShowCardInMainWindow.ShouldBeFalse();

        // Step 5 — step through a few photonic ADDs (the multiply loop interleaves
        // LOAD/STORE/JZ between its ADDs, so step until three ADDs landed).
        var stepGuard = 0;
        while (tour.CurrentStepIndex == 4 && stepGuard++ < 40)
            playground.StepCommand.Execute(null);

        tour.CurrentStepIndex.ShouldBe(5, "three photonic ADDs must advance to the closing step");
        tour.IsCompleted.ShouldBeFalse("the closing words step completes via Next");

        // Step 6 — run to the end: ACC = 12; the closing words stay until Next.
        var guard = 0;
        while (playground.MachineStatusText != "HALTED" && guard++ < 60)
            playground.StepCommand.Execute(null);
        playground.Accumulator.ShouldBe(12, "3 x 4 by repeated addition halts with ACC = 12");

        tour.CurrentStepIndex.ShouldBe(5, "reaching ACC = 12 must not skip the closing words");
        tour.IsActive.ShouldBeTrue();

        tour.NextCommand.Execute(null);

        tour.IsCompleted.ShouldBeTrue();
        tour.IsActive.ShouldBeFalse("the overlay hides once the tour completes");
    }

    [Fact]
    public void GoldenModelSteps_DoNotCount_AsPhotonicSteps()
    {
        var (logic, playground, tour) = CreateFixture();
        logic.HasNetwork = true;
        tour.Start();
        tour.NotifyPlaygroundOpened();
        playground.SelectedSample = playground.Samples.Single(
            s => s.FileName == RunProgramTourViewModel.MultiplySampleFileName);
        playground.UsePhotonicAdder = true;
        playground.UsePhotonicAdder = false;
        tour.CurrentStepIndex.ShouldBe(4);

        // Golden-model stepping leaves no photonic-ADD status line — the tour
        // must honestly keep waiting for photonic steps.
        for (var i = 0; i < RunProgramTourViewModel.PhotonicStepsToWatch; i++)
            playground.StepCommand.Execute(null);

        playground.PhotonicStatusText.ShouldBeEmpty();
        tour.CurrentStepIndex.ShouldBe(4, "golden-model steps must not count as photonic steps");

        playground.UsePhotonicAdder = true;
        var stepGuard = 0;
        while (tour.CurrentStepIndex == 4 && stepGuard++ < 40)
            playground.StepCommand.Execute(null);

        tour.CurrentStepIndex.ShouldBe(5, "photonic steps after the toggle must advance the tour");
    }

    [Fact]
    public void OpenedPlayground_BeforeStart_StillCompletesTheOpenStep()
    {
        var (logic, playground, tour) = CreateFixture();
        logic.HasNetwork = true;
        tour.NotifyPlaygroundOpened();

        tour.Start();

        tour.CurrentStepIndex.ShouldBe(2,
            "a playground window that is already open when the tour starts satisfies the open step");
    }

    [Fact]
    public void Skip_ExitsTour_LaterChangesDoNotAdvance()
    {
        var (logic, playground, tour) = CreateFixture();
        logic.HasNetwork = true;
        tour.Start();
        tour.SkipCommand.Execute(null);

        tour.IsActive.ShouldBeFalse();
        tour.IsCompleted.ShouldBeFalse();

        tour.NotifyPlaygroundOpened();
        playground.SelectedSample = playground.Samples.Single(
            s => s.FileName == RunProgramTourViewModel.MultiplySampleFileName);

        tour.CurrentStepIndex.ShouldBe(1, "a skipped tour must not react to the playground anymore");
        tour.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void Next_AdvancesManually_ThroughAllSteps()
    {
        var (_, _, tour) = CreateFixture();
        tour.Start();

        for (var i = 0; i < 6; i++)
            tour.NextCommand.Execute(null);

        tour.IsCompleted.ShouldBeTrue();
        tour.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void CompletedTour_DetachesFromPlayground_LaterChangesDoNotThrow()
    {
        var (logic, playground, tour) = CreateFixture();
        logic.HasNetwork = true;
        tour.Start();
        for (var i = 0; i < 6; i++)
            tour.NextCommand.Execute(null);

        tour.NotifyPlaygroundOpened();
        playground.SelectedSample = playground.Samples.Single(
            s => s.FileName == RunProgramTourViewModel.MultiplySampleFileName);
        playground.UsePhotonicAdder = true;
        playground.StepCommand.Execute(null);

        tour.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public void Restart_ResetsProgress_AndReevaluates()
    {
        var (logic, playground, tour) = CreateFixture();
        logic.HasNetwork = true;
        tour.Start();
        tour.NotifyPlaygroundOpened();
        playground.SelectedSample = playground.Samples.Single(
            s => s.FileName == RunProgramTourViewModel.MultiplySampleFileName);
        tour.SkipCommand.Execute(null);

        tour.Start();

        tour.IsActive.ShouldBeTrue();
        tour.IsCompleted.ShouldBeFalse();
        tour.CurrentStepIndex.ShouldBe(3,
            "the open playground and the picked sample still satisfy the first playground steps");
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
