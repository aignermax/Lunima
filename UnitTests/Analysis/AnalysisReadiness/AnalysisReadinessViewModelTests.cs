using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.AnalysisReadiness;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.AnalysisReadiness;

/// <summary>Empty-state conditions of the optical dock tabs and their one-click fixes.</summary>
public class AnalysisReadinessViewModelTests
{
    private static (AnalysisReadinessViewModel Readiness, DesignCanvasViewModel Canvas, AnalysisOutputPanelViewModel Output) Make()
    {
        var canvas = new DesignCanvasViewModel();
        var output = new AnalysisOutputPanelViewModel();
        output.Configure(canvas);
        var readiness = new AnalysisReadinessViewModel(output);
        readiness.Configure(canvas);
        return (readiness, canvas, output);
    }

    [Fact]
    public void EmptyDesign_NeedsALightSource()
    {
        var (readiness, _, _) = Make();

        readiness.NeedsLightSource.ShouldBeTrue();
        readiness.NeedsOutputPick.ShouldBeFalse("the light source is the first thing to fix");
        readiness.IsReadyForTimeDomain.ShouldBeFalse();
    }

    [Fact]
    public void PlacingALightSource_ClearsTheHint_AndAsksForAnOutput()
    {
        var (readiness, canvas, _) = Make();
        var source = TestComponentFactory.CreateBasicComponent();
        source.IsUserMarkedLightSource = true;

        canvas.AddComponent(source);

        readiness.HasLightSource.ShouldBeTrue();
        readiness.NeedsLightSource.ShouldBeFalse();
        readiness.NeedsOutputPick.ShouldBeTrue();
    }

    [Fact]
    public void LightSourceInsideAGroup_Counts()
    {
        var (readiness, canvas, _) = Make();
        var group = TestComponentFactory.CreateComponentGroup("Die", addChildren: true);
        group.ChildComponents[0].IsUserMarkedLightSource = true;

        canvas.AddComponent(group);

        readiness.HasLightSource.ShouldBeTrue();
    }

    [Fact]
    public void RemovingTheLightSource_BringsTheHintBack()
    {
        var (readiness, canvas, _) = Make();
        var source = TestComponentFactory.CreateBasicComponent();
        source.IsUserMarkedLightSource = true;
        var vm = canvas.AddComponent(source);

        canvas.Components.Remove(vm);

        readiness.NeedsLightSource.ShouldBeTrue();
    }

    [Fact]
    public void PlaceLightSource_InvokesTheWiredFix()
    {
        var (readiness, _, _) = Make();
        int calls = 0;
        readiness.PlaceLightSourceRequested = () => calls++;

        readiness.PlaceLightSourceCommand.Execute(null);

        calls.ShouldBe(1);
    }

    [Fact]
    public void PickOutput_ArmsTheCanvasPicker()
    {
        var (readiness, _, output) = Make();
        int calls = 0;
        output.PickRequested = () => calls++;

        readiness.PickOutputCommand.Execute(null);

        calls.ShouldBe(1);
    }
}
