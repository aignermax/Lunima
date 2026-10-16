using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP_Core.Components.Core;
using Shouldly;

namespace UnitTests.Onboarding.FirstStepsTutorial;

public class OpticalTutorialActionTests
{
    [Fact]
    public void Next_CannotClaimActionsThatWereNotPerformed()
    {
        var tour = new TutorialViewModel(new DesignCanvasViewModel());
        tour.Start();
        for (var i = 0; i < 10; i++) tour.NextCommand.Execute(null);
        tour.CurrentStepIndex.ShouldBe(0);
        tour.IsCompleted.ShouldBeFalse();
        tour.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void DieAndElectricalOrPassiveParts_DoNotReplaceAnOpticalSource()
    {
        var canvas = new DesignCanvasViewModel();
        var tour = new TutorialViewModel(canvas);
        tour.Start();
        canvas.AddComponent(new ComponentGroup("Die") { Die = new CAP_Core.Components.Dies.DieDefinition(1000, 1000) });
        canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        tour.CurrentStepIndex.ShouldBe(0);
        tour.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void DeletingSource_ReturnsToSourcePrerequisite()
    {
        var canvas = new DesignCanvasViewModel();
        var source = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        source.HumanReadableName = "Grating Coupler";
        canvas.AddComponent(source);
        canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        var tour = new TutorialViewModel(canvas);
        tour.Start();

        canvas.Components.Remove(canvas.Components.Single(c => c.Component == source));

        tour.CurrentStepIndex.ShouldBe(0);
        tour.IsCompleted.ShouldBeFalse();
    }
}
