using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class GroupDocumentRouteContinuityTests
{
    [AvaloniaFact]
    public async Task NavigationIntoAndOutOfExistingGroup_DoesNotDirtyTheDocument()
    {
        await using var main = MainViewModelTestHelper.CreateMainViewModel();
        var group = CreateGroup();
        main.Canvas.AddComponent(group);
        main.FileOperations.HasUnsavedChanges = false;

        main.Canvas.EnterGroupEditMode(group);
        main.FileOperations.HasUnsavedChanges.ShouldBeFalse("entering a group is navigation");
        main.Canvas.ExitToRoot();
        main.FileOperations.HasUnsavedChanges.ShouldBeFalse("returning to the root is navigation");
    }

    [Fact]
    public void AddingAndRemovingUnroutedWire_ImmediatelyUpdatesRootDocument()
    {
        var canvas = new DesignCanvasViewModel();
        var group = CreateGroup();
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);
        try
        {
            var wire = Connect(canvas, group);
            var stored = group.InternalPaths.Single();
            stored.PathId.ShouldBe(wire.Connection.Id);
            stored.StartPin.ShouldBeSameAs(wire.Connection.StartPin);
            stored.EndPin.ShouldBeSameAs(wire.Connection.EndPin);
            stored.Path.Segments.ShouldBeEmpty();

            canvas.Connections.Remove(wire);
            group.InternalPaths.ShouldBeEmpty();
            canvas.CurrentEditGroup.ShouldBeSameAs(group);
        }
        finally { canvas.Routing.CancelRouting(); canvas.DisposeDocumentBindings(); }
    }

    [Fact]
    public void RouteEdit_PublishesDetachedGeometryAndSettingsWithoutRecapturingOtherWires()
    {
        var canvas = new DesignCanvasViewModel();
        var group = CreateGroup();
        var background = new FrozenWaveguidePath { Path = Line(70) };
        group.AddInternalPath(background);
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);
        try
        {
            var wire = Connect(canvas, group);
            wire.Connection.RestoreCachedPath(Line(20));
            wire.Connection.WidthMicrometers = 1.25;
            wire.Connection.IsRouteFrozen = true;
            wire.NotifyPathChanged();

            var stored = group.InternalPaths.Single(path => path.PathId == wire.Connection.Id);
            stored.Path.Segments.Single().StartPoint.ShouldBe((10.0, 20.0));
            stored.WidthMicrometers.ShouldBe(1.25);
            stored.IsRouteFrozen.ShouldBeTrue();
            group.InternalPaths.ShouldContain(background);
            wire.Connection.RoutedPath!.Segments[0].StartPoint = (10, 40);
            stored.Path.Segments[0].StartPoint.ShouldBe((10.0, 20.0), "uncommitted handle edits must not alias document geometry");
            wire.NotifyPathChanged();
            group.InternalPaths.Single(path => path.PathId == wire.Connection.Id)
                .Path.Segments[0].StartPoint.ShouldBe((10.0, 40.0));
        }
        finally { canvas.Routing.CancelRouting(); canvas.DisposeDocumentBindings(); }
    }

    [Fact]
    public void ClearWires_KeepsImportedPinlessGeometryAndDetachesRemovedWireCallbacks()
    {
        var canvas = new DesignCanvasViewModel();
        var group = CreateGroup();
        var background = new FrozenWaveguidePath { Path = Line(70) };
        group.AddInternalPath(background);
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);
        try
        {
            var wire = Connect(canvas, group);
            group.InternalPaths.Count.ShouldBe(2);
            canvas.Connections.Clear();
            wire.Connection.RestoreCachedPath(Line(90));
            wire.NotifyPathChanged();
            group.InternalPaths.ShouldBe(new[] { background });
        }
        finally { canvas.Routing.CancelRouting(); canvas.DisposeDocumentBindings(); }
    }

    private static ComponentGroup CreateGroup()
    {
        var group = new ComponentGroup("editing group");
        group.AddChild(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        group.AddChild(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        return group;
    }

    private static WaveguideConnectionViewModel Connect(DesignCanvasViewModel canvas, ComponentGroup group) =>
        canvas.ConnectPins(group.ChildComponents[0].PhysicalPins[1], group.ChildComponents[1].PhysicalPins[0])!;

    private static RoutedPath Line(double y) => new() { Segments = { new StraightSegment(10, y, 90, y, 0) } };
}
