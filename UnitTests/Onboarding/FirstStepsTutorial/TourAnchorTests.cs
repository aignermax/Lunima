using Avalonia;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using Shouldly;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Tests for the anchored guided-tour overlay (issue #1167): the card-placement
/// geometry helper and the per-step anchor targets both tours expose, so every
/// actionable step spotlights a real named control instead of only describing it.
/// </summary>
public class TourAnchorTests
{
    private static readonly Size Viewport = new(1200, 800);
    private static readonly Size Card = new(340, 160);

    [Fact]
    public void Place_NoTarget_FallsBackToBottomCenter()
    {
        var pos = TourCardPlacement.Place(Viewport, default, Card, out var side);

        side.ShouldBe(TourCardSide.FloatingBottomCenter);
        pos.X.ShouldBe((Viewport.Width - Card.Width) / 2);
        pos.Y.ShouldBeLessThan(Viewport.Height - Card.Height);
    }

    [Fact]
    public void Place_TargetMidScreen_CardGoesBelow()
    {
        var target = new Rect(400, 300, 100, 40);

        var pos = TourCardPlacement.Place(Viewport, target, Card, out var side);

        side.ShouldBe(TourCardSide.Bottom);
        pos.Y.ShouldBe(target.Bottom + TourCardPlacement.CardGap);
        pos.X.ShouldBe(target.Center.X - Card.Width / 2);
    }

    [Fact]
    public void Place_TargetNearBottom_CardGoesAbove()
    {
        var target = new Rect(400, 720, 100, 40);

        var pos = TourCardPlacement.Place(Viewport, target, Card, out var side);

        side.ShouldBe(TourCardSide.Top);
        pos.Y.ShouldBe(target.Top - TourCardPlacement.CardGap - Card.Height);
    }

    [Fact]
    public void Place_TargetFillsHeight_CardGoesRight()
    {
        // Tall target: no room below or above, room on the right.
        var target = new Rect(100, 20, 200, 760);

        var pos = TourCardPlacement.Place(Viewport, target, Card, out var side);

        side.ShouldBe(TourCardSide.Right);
        pos.X.ShouldBe(target.Right + TourCardPlacement.CardGap);
    }

    [Fact]
    public void Place_TargetAtRightEdge_CardGoesLeft()
    {
        var target = new Rect(1000, 20, 180, 760);

        var pos = TourCardPlacement.Place(Viewport, target, Card, out var side);

        side.ShouldBe(TourCardSide.Left);
        pos.X.ShouldBe(target.Left - TourCardPlacement.CardGap - Card.Width);
    }

    [Fact]
    public void Place_TargetNearRightEdge_CardStaysInsideViewport()
    {
        var target = new Rect(900, 300, 100, 40);

        var pos = TourCardPlacement.Place(Viewport, target, Card, out _);

        (pos.X + Card.Width).ShouldBeLessThanOrEqualTo(Viewport.Width - TourCardPlacement.ViewportMargin);
        pos.X.ShouldBeGreaterThanOrEqualTo(TourCardPlacement.ViewportMargin);
    }

    [Fact]
    public void LearnTour_EveryStep_AnchorsToANamedControl()
    {
        var tutorial = new TutorialViewModel(new DesignCanvasViewModel());

        tutorial.Steps.ShouldAllBe(s => !string.IsNullOrEmpty(s.TargetName));
    }

    [Fact]
    public void LearnTour_StepTargets_MatchTheControlsTheStepTextNames()
    {
        var tutorial = new TutorialViewModel(new DesignCanvasViewModel());

        tutorial.Steps[0].TargetName.ShouldBe("ComponentLibraryList");
        tutorial.Steps[1].TargetName.ShouldBe("DesignCanvasControl");
        tutorial.Steps[2].TargetName.ShouldBe("RunSimulationButton");
    }

    [Fact]
    public void LearnTour_CurrentTargetName_FollowsStepAdvance()
    {
        var canvas = new DesignCanvasViewModel();
        var tutorial = new TutorialViewModel(canvas);
        tutorial.Start();

        tutorial.CurrentTargetName.ShouldBe("ComponentLibraryList");

        canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide());

        tutorial.CurrentTargetName.ShouldBe("DesignCanvasControl");
    }

    [Fact]
    public void WatchTour_ActionSteps_AnchorToLogicPanelButtons_ClosingStepFloats()
    {
        var steps = WatchTourSteps();

        steps[0].TargetName.ShouldBe("LogicBuildButton");
        steps[1].TargetName.ShouldBe("LogicStepClockButton");
        steps[2].TargetName.ShouldBe("LogicRunStopButton");
        steps[3].TargetName.ShouldBe("LogicRunStopButton");
        steps[4].TargetName.ShouldBeNull("the closing words have no control to click");
    }

    private static IReadOnlyList<TutorialStep> WatchTourSteps()
    {
        var logic = new CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.LogicPanelViewModel(
            new FakeLogicRunClock());
        var dock = new CAP.Avalonia.ViewModels.Panels.AnalysisDockViewModel(
            new CAP.Avalonia.ViewModels.Analysis.TimeDomainViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.EyeDiagram.EyeDiagramViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum.WavelengthSpectrumViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.AnalysisOutput.AnalysisOutputPanelViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis.MonteCarloViewModel(),
            new CAP.Avalonia.ViewModels.Analysis.CircuitOptimization.CircuitOptimizationViewModel(
                new CAP.Avalonia.Commands.CommandManager()));
        return new WatchComputeTourViewModel(logic, dock).Steps;
    }

    /// <summary>Manually fired clock — the panel requires an instance, the tour never fires ticks.</summary>
    private sealed class FakeLogicRunClock : CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.ILogicRunClock
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
