using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class CanvasDocumentRevisionTests
{
    [Theory]
    [InlineData("add")]
    [InlineData("move")]
    [InlineData("remove")]
    [InlineData("parameters")]
    public void EditorMutation_RevokesCapturedResult(string mutation)
    {
        var canvas = new DesignCanvasViewModel();
        var component = canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        using var operation = canvas.Document.CaptureOperation();
        switch (mutation)
        {
            case "add": canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins()); break;
            case "move": component.X += 10; break;
            case "remove": canvas.Components.Remove(component); break;
            default: canvas.RequestResimulation(); break;
        }
        operation.IsCurrent.ShouldBeFalse();
        canvas.DisposeDocumentBindings();
    }

    [Fact]
    public void SelectionAndViewport_DoNotRevokeCapturedResult()
    {
        var canvas = new DesignCanvasViewModel();
        var component = canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        using var operation = canvas.Document.CaptureOperation();
        canvas.SelectedComponent = component;
        component.IsSelected = true;
        canvas.PanX = 40;
        canvas.PanY = 25;
        canvas.ViewZoom = 0.5;
        operation.IsCurrent.ShouldBeTrue();
        canvas.DisposeDocumentBindings();
    }

    [Fact]
    public void GroupProjection_RevokesScopeResultWithoutChangingDocumentRevision()
    {
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("scope");
        group.AddChild(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        canvas.AddComponent(group);
        using var scoped = canvas.Document.CaptureOperation();
        using var document = canvas.Document.CaptureOperation(scopeBound: false);
        canvas.EnterGroupEditMode(group);
        scoped.IsCurrent.ShouldBeFalse();
        document.IsCurrent.ShouldBeTrue("projecting an existing scope is not a document edit");
        canvas.DisposeDocumentBindings();
    }

    [Fact]
    public void RemovedComponent_NoLongerInvalidatesDocumentThroughOldSubscriptions()
    {
        var canvas = new DesignCanvasViewModel();
        var removed = canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        canvas.Components.Remove(removed);
        using var operation = canvas.Document.CaptureOperation();
        removed.X += 1;
        operation.IsCurrent.ShouldBeTrue();
        canvas.DisposeDocumentBindings();
    }

    [Fact]
    public async Task LeavingUnchangedGroup_DoesNotInvalidateDocumentWideSave()
    {
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("scope");
        group.AddChild(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        canvas.AddComponent(group);
        using var save = canvas.Document.CaptureOperation(scopeBound: false);
        canvas.EnterGroupEditMode(group);
        canvas.ExitToRoot();
        await canvas.Routing.ExecuteHistoryChangeAsync(() => true, () => true);
        save.IsCurrent.ShouldBeTrue("pure navigation must not masquerade as a document edit");
        canvas.DisposeDocumentBindings();
    }
}
