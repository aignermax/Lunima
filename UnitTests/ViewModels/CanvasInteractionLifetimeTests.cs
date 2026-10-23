using System.Runtime.CompilerServices;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class CanvasInteractionLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposalDuringDrag_ReleasesCanvasGestureStateWithoutAnUndoCommand(bool group)
    {
        var canvas = new DesignCanvasViewModel();
        var component = canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide(), "dragged");
        var commands = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commands);
        if (group) interaction.StartGroupMove(new[] { component });
        else interaction.StartMoveComponent(component);
        canvas.Placement.IsDragging.ShouldBeTrue();
        canvas.DragRouteSnapshot.ShouldNotBeNull();
        interaction.Dispose();
        canvas.Placement.IsDragging.ShouldBeFalse();
        canvas.DragRouteSnapshot.ShouldBeNull();
        commands.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void DisposalDuringConnectionGesture_ClearsPinHighlight()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins(), "component");
        var interaction = new CanvasInteractionViewModel(canvas, new CommandManager());
        interaction.CurrentMode = InteractionMode.Connect;
        var (x, y) = canvas.AllPins[0].Pin.GetAbsolutePosition();
        interaction.CanvasMouseMove(x, y);
        canvas.HighlightedPin.ShouldNotBeNull();
        interaction.Dispose();
        canvas.HighlightedPin.ShouldBeNull();
    }

    [Fact]
    public void RetiredInteraction_DoesNotFollowSelectionInReusedCanvas()
    {
        var canvas = new DesignCanvasViewModel();
        var first = canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide(), "first");
        var second = canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide(), "second");
        var retired = new CanvasInteractionViewModel(canvas, new CommandManager());
        canvas.SelectedComponent = first;
        retired.SelectedComponent.ShouldBeSameAs(first);
        retired.Dispose();
        retired.Dispose();

        using var current = new CanvasInteractionViewModel(canvas, new CommandManager());
        canvas.SelectedComponent = second;
        current.SelectedComponent.ShouldBeSameAs(second);
        retired.SelectedComponent.ShouldBeSameAs(first);
    }

    [Fact]
    public void LiveCanvas_DoesNotRetainRetiredInteractionThroughEvents()
    {
        var canvas = new DesignCanvasViewModel();
        var retired = CreateRetiredInteraction(canvas);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        retired.IsAlive.ShouldBeFalse();
        GC.KeepAlive(canvas);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRetiredInteraction(DesignCanvasViewModel canvas)
    {
        var interaction = new CanvasInteractionViewModel(canvas, new CommandManager());
        interaction.Dispose();
        return new WeakReference(interaction);
    }
}
