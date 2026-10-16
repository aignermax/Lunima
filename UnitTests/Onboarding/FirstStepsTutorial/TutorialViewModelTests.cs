using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using Shouldly;

namespace UnitTests.Onboarding.FirstStepsTutorial;

public class TutorialViewModelTests
{
    private static ComponentTemplate Template(string name) => new()
    {
        Name = name, WidthMicrometers = 250, HeightMicrometers = 250,
        PinDefinitions = [new("in", 0, 125, 180), new("out", 250, 125, 0)]
    };

    private static Component Place(DesignCanvasViewModel canvas, string name, double x)
    {
        var part = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        part.HumanReadableName = name;
        part.WidthMicrometers = part.HeightMicrometers = 250;
        part.PhysicalX = x;
        part.PhysicalY = 100;
        canvas.AddComponent(part);
        return part;
    }

    [Fact]
    public async Task RealActions_SelectPlaceTwiceConnectAndRun_Complete()
    {
        var canvas = new DesignCanvasViewModel();
        var interaction = new CanvasInteractionViewModel(canvas, new CommandManager());
        var sourceTemplate = Template("Grating Coupler");
        var receiverTemplate = Template("Straight Waveguide");
        var tour = new TutorialViewModel(canvas);
        tour.AttachInteraction(interaction, new() { sourceTemplate, receiverTemplate });
        tour.Start();
        tour.Steps.Count.ShouldBe(7);
        tour.CurrentTargetItem.ShouldBeSameAs(sourceTemplate);
        tour.CurrentTitle.ShouldNotBe(tour.CurrentStep.TitleKey);
        tour.CurrentCue.ShouldNotContain("Tutorial.Action");

        interaction.SelectedTemplate = sourceTemplate;
        tour.CurrentStepIndex.ShouldBe(1);
        tour.CurrentCanvasPoint.ShouldNotBeNull();
        var source = Place(canvas, sourceTemplate.Name, 100);
        tour.CurrentStepIndex.ShouldBe(2, "one source is insufficient to connect");
        tour.CurrentTargetItem.ShouldBeSameAs(receiverTemplate);
        interaction.SelectedTemplate = receiverTemplate;
        tour.CurrentStepIndex.ShouldBe(3);
        var receiver = Place(canvas, receiverTemplate.Name, 600);
        tour.CurrentStepIndex.ShouldBe(4);
        interaction.CurrentMode = InteractionMode.Connect;
        tour.CurrentStepIndex.ShouldBe(5);
        tour.CurrentTargetPin!.ParentComponent.ShouldBeSameAs(source);
        tour.ConnectionTargetPin!.ParentComponent.ShouldBeSameAs(receiver);

        var connection = await canvas.ConnectPinsAsync(tour.CurrentTargetPin!, tour.ConnectionTargetPin!);
        connection.ShouldNotBeNull();
        tour.CurrentStepIndex.ShouldBe(6);
        canvas.ShowPowerFlow = true;
        tour.IsCompleted.ShouldBeTrue();
        tour.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void VisibleLibraryChanges_ResolveAndClearExactTarget()
    {
        var canvas = new DesignCanvasViewModel();
        var library = new ObservableCollection<ComponentTemplate>();
        var tour = new TutorialViewModel(canvas);
        tour.AttachInteraction(new(canvas, new CommandManager()), library);
        tour.Start();
        tour.CurrentTargetName.ShouldBeNull();
        var source = Template("Grating Coupler");
        library.Add(source);
        tour.CurrentTargetItem.ShouldBeSameAs(source);
        tour.CurrentTargetName.ShouldBe("ComponentLibraryList");
        library.Clear();
        tour.CurrentTargetItem.ShouldBeNull();
        tour.CurrentTargetName.ShouldBeNull();
    }

    [Fact]
    public void RefusedTemplate_IsNotDemonstrated()
    {
        var canvas = new DesignCanvasViewModel();
        var interaction = new CanvasInteractionViewModel(canvas, new CommandManager())
        { ComponentPlacementRefusal = _ => "Unavailable in this workspace" };
        var tour = new TutorialViewModel(canvas);
        tour.AttachInteraction(interaction, new() { Template("Grating Coupler") });
        tour.Start();
        tour.CurrentTargetName.ShouldBeNull();
    }

    [Fact]
    public void PlacementThatDoesNotFit_HasNoMisleadingCanvasTarget()
    {
        var canvas = new DesignCanvasViewModel { ChipMaxX = 100, ChipMaxY = 100 };
        var interaction = new CanvasInteractionViewModel(canvas, new CommandManager());
        var source = Template("Grating Coupler");
        var tour = new TutorialViewModel(canvas);
        tour.AttachInteraction(interaction, new() { source });
        tour.Start();
        interaction.SelectedTemplate = source;
        tour.CurrentStepIndex.ShouldBe(1);
        tour.CurrentCanvasPoint.ShouldBeNull();
        tour.CurrentTargetName.ShouldBeNull();
    }

    [Fact]
    public void MovingPart_UpdatesActualPinTarget()
    {
        var canvas = new DesignCanvasViewModel();
        var source = Place(canvas, "Grating Coupler", 100);
        Place(canvas, "Straight", 600);
        var tour = new TutorialViewModel(canvas);
        tour.Start();
        var before = tour.CurrentCanvasPoint!.Value;
        source.PhysicalX += 50;
        canvas.Components.Single(c => c.Component == source).X += 50;
        tour.CurrentCanvasPoint!.Value.X.ShouldBe(before.X + 50);
    }

    [Fact]
    public async Task UnrelatedConnection_DoesNotSatisfySourceConnection()
    {
        var canvas = new DesignCanvasViewModel();
        var first = Place(canvas, "Straight", 100);
        var second = Place(canvas, "Straight", 600);
        await canvas.ConnectPinsAsync(first.PhysicalPins.Single(p => p.Name == "out"),
            second.PhysicalPins.Single(p => p.Name == "in"));
        Place(canvas, "Grating Coupler", 1100);
        var tour = new TutorialViewModel(canvas);
        tour.Start();
        tour.CurrentStepIndex.ShouldBe(5);
        canvas.ShowPowerFlow = true;
        tour.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void Skip_DetachesAndRestartUsesCurrentState()
    {
        var canvas = new DesignCanvasViewModel();
        var tour = new TutorialViewModel(canvas);
        tour.Start();
        tour.Skip();
        Place(canvas, "Grating Coupler", 100);
        tour.CurrentStepIndex.ShouldBe(0);
        tour.IsCompleted.ShouldBeFalse();
        tour.Start();
        tour.CurrentStepIndex.ShouldBe(2);
        tour.ProgressText.ShouldContain("3/7");
    }

    [Fact]
    public void DeletingSecondPart_ClearsPinTargetsAndRewinds()
    {
        var canvas = new DesignCanvasViewModel();
        Place(canvas, "Grating Coupler", 100);
        var second = Place(canvas, "Straight", 600);
        var tour = new TutorialViewModel(canvas);
        tour.Start();
        tour.CurrentStepIndex.ShouldBe(5);
        canvas.Components.Remove(canvas.Components.Single(c => c.Component == second));
        tour.CurrentStepIndex.ShouldBe(2);
        tour.CurrentTargetPin.ShouldBeNull();
        tour.ConnectionTargetPin.ShouldBeNull();
        canvas.AddComponent(second);
        tour.CurrentStepIndex.ShouldBe(5, "undoing deletion restores prerequisites");
    }
}
