using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using Xunit;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Engine tests for the "Connect two chiplets" guided tour (issue #1288, slice
/// 4 of #769) on an empty canvas: activation, manual advance, skip and the
/// move step's "do it for me" button visibility. The full fix loop against the
/// shipped example lives in <see cref="ConnectChipletsTourJourneyTests"/>.
/// </summary>
[Collection("LocalizationSingleton")]
public class ConnectChipletsTourViewModelTests : IDisposable
{
    private readonly string _previousLanguage;

    /// <summary>Pins English for the localization assertions.</summary>
    public ConnectChipletsTourViewModelTests()
    {
        _previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
    }

    /// <summary>Restores the language that was active before the test.</summary>
    public void Dispose() => LocalizationService.Instance.SetLanguage(_previousLanguage);

    private static (DesignCanvasViewModel canvas, DesignValidationViewModel validation,
        AnalysisDockViewModel dock, ConnectChipletsTourViewModel tour) CreateFixture()
    {
        var canvas = new DesignCanvasViewModel();
        var validation = new DesignValidationViewModel();
        var dock = MakeDock();
        return (canvas, validation, dock,
            new ConnectChipletsTourViewModel(canvas, validation, dock, new CommandManager()));
    }

    private static AnalysisDockViewModel MakeDock() =>
        new(new TimeDomainViewModel(), new EyeDiagramViewModel(),
            new WavelengthSpectrumViewModel(), new AnalysisOutputPanelViewModel(),
            new MonteCarloViewModel(), new CircuitOptimizationViewModel(new CommandManager()));

    [Fact]
    public void InitialState_IsInactive_AtFirstStep()
    {
        var (_, _, _, tour) = CreateFixture();

        tour.IsActive.ShouldBeFalse();
        tour.IsCompleted.ShouldBeFalse();
        tour.CurrentStepIndex.ShouldBe(0);
        tour.Steps.Count.ShouldBe(6);
        tour.ShowMoveForMeButton.ShouldBeFalse();
    }

    [Fact]
    public void Start_ActivatesTour_AtIntroStep_WithLocalizedCard()
    {
        var (_, _, _, tour) = CreateFixture();

        tour.Start();

        tour.IsActive.ShouldBeTrue();
        tour.CurrentStepIndex.ShouldBe(0);
        tour.ProgressText.ShouldContain("1/6");
        tour.CurrentTitle.ShouldNotBeNullOrWhiteSpace();
        tour.CurrentTitle.ShouldNotBe(tour.CurrentStep.TitleKey, "title must be localized, not the raw key");
        tour.CurrentBody.ShouldNotBe(tour.CurrentStep.BodyKey, "body must be localized, not the raw key");
        tour.CurrentTargetName.ShouldBe("DesignCanvasControl");
    }

    [Fact]
    public void SimulationRun_AdvancesPastRunStep()
    {
        var (canvas, _, _, tour) = CreateFixture();
        tour.Start();
        tour.NextCommand.Execute(null);
        tour.CurrentStepIndex.ShouldBe(1, "the intro step completes via Next");

        canvas.ShowPowerFlow = true;

        tour.CurrentStepIndex.ShouldBe(2, "a finished simulation must advance to the move step");
        tour.ShowMoveForMeButton.ShouldBeTrue("the move step offers the 'do it for me' button");
    }

    [Fact]
    public void ShowMoveForMeButton_OnlyOnMoveStep()
    {
        var (canvas, _, _, tour) = CreateFixture();
        tour.Start();

        tour.ShowMoveForMeButton.ShouldBeFalse();
        tour.NextCommand.Execute(null);
        tour.ShowMoveForMeButton.ShouldBeFalse();
        canvas.ShowPowerFlow = true;
        tour.ShowMoveForMeButton.ShouldBeTrue();
        tour.NextCommand.Execute(null);
        tour.ShowMoveForMeButton.ShouldBeFalse("leaving the move step hides the helper button");
    }

    [Fact]
    public void Next_AdvancesManually_ThroughAllSteps()
    {
        var (_, _, _, tour) = CreateFixture();
        tour.Start();

        for (var i = 0; i < 6; i++)
            tour.NextCommand.Execute(null);

        tour.IsCompleted.ShouldBeTrue();
        tour.IsActive.ShouldBeFalse("the overlay hides once the tour completes");
    }

    [Fact]
    public void Skip_ExitsTour_LaterChangesDoNotAdvance()
    {
        var (canvas, _, _, tour) = CreateFixture();
        tour.Start();
        tour.SkipCommand.Execute(null);

        tour.IsActive.ShouldBeFalse();
        tour.IsCompleted.ShouldBeFalse();

        canvas.ShowPowerFlow = true;

        tour.CurrentStepIndex.ShouldBe(0, "a skipped tour must not react to the canvas anymore");
    }

    [Fact]
    public void Restart_ResetsProgress()
    {
        var (_, _, _, tour) = CreateFixture();
        tour.Start();
        tour.NextCommand.Execute(null);
        tour.NextCommand.Execute(null);
        tour.SkipCommand.Execute(null);

        tour.Start();

        tour.IsActive.ShouldBeTrue();
        tour.IsCompleted.ShouldBeFalse();
        tour.CurrentStepIndex.ShouldBe(0);
    }
}
