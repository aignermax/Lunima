using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using CAP.Avalonia.ViewModels.Panels.DesignModels;
using CAP.Avalonia.ViewModels.Panels.NativeDesign;
using CAP.Avalonia.Commands;
using Avalonia.Headless.XUnit;

namespace UnitTests.ViewModels;

public sealed class DocumentScopeContinuityTests
{
    [AvaloniaFact]
    public async Task LoadingAnotherDocumentWhileInsideGroup_DropsOldScopeAndRootBackup()
    {
        var path = Path.Combine(Path.GetTempPath(), "scope-replacement-" + Guid.NewGuid() + ".lun");
        await using var main = MainViewModelTestHelper.CreateMainViewModel();
        try
        {
            (await main.FileOperations.SaveDesignToPathAsync(path)).ShouldBeTrue();
            var parent = new ComponentGroup("old document");
            parent.AddChild(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
            main.Canvas.AddComponent(parent);
            main.Canvas.EnterGroupEditMode(parent);
            main.FileOperations.HasUnsavedChanges = false;

            (await main.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue();

            main.Canvas.CurrentEditGroup.ShouldBeNull();
            main.Canvas.BreadcrumbPath.ShouldBeEmpty();
            main.Canvas.Groups.RootComponents.ShouldBeEmpty();
            main.Canvas.Components.ShouldBeEmpty();
        }
        finally
        {
            main.Canvas.Routing.CancelRouting();
            await main.Canvas.Routing.ExecuteHistoryChangeAsync(() => true, () => true);
            File.Delete(path);
        }
    }

    [Fact]
    public void RegroupInsideEditedGroup_KeepsParentOwnershipAcrossUndoRedo()
    {
        var (canvas, parent, child) = CreateNested();
        canvas.EnterGroupEditMode(parent);
        var selected = canvas.Components.ToList();
        var command = new CreateGroupCommand(canvas, selected, "new nested group");
        command.Execute();
        AssertOwner(command.CreatedGroup!);
        command.Undo();
        AssertOwner(parent);
        command.Execute();
        AssertOwner(command.CreatedGroup!);
        canvas.Routing.CancelRouting();
        canvas.DisposeDocumentBindings();

        void AssertOwner(ComponentGroup expected)
        {
            foreach (var view in selected)
            {
                view.Component.ParentGroup.ShouldBeSameAs(expected);
                if (view.Component is ComponentGroup nested) nested.ParentGroup.ShouldBeSameAs(expected);
            }
            if (!ReferenceEquals(expected, parent)) expected.ParentGroup.ShouldBeSameAs(parent);
        }
    }

    [Fact]
    public void CaptureWhileEditingNestedGroup_SavesWholeDocumentWithoutNavigating()
    {
        var (canvas, parent, child) = CreateNested();
        var outside = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        canvas.AddComponent(outside);
        canvas.EnterGroupEditMode(parent);
        canvas.EnterGroupEditMode(child);
        var added = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        canvas.AddComponent(added);
        var templates = new ComponentTemplateResolver(canvas.Groups.RootComponents, []);
        var capture = new NativeDesignCapture(canvas, new NativeComponentMapper(canvas, templates),
            new NativeGroupCapture(canvas, templates));

        var saved = capture.Capture(_ => null).Data;

        saved.Components.ShouldContain(component => component.Identifier == outside.Identifier);
        saved.Groups!.Count.ShouldBe(2);
        saved.Groups.SelectMany(group => group.ChildComponents)
            .ShouldContain(component => component.Identifier == added.Identifier);
        canvas.CurrentEditGroup.ShouldBeSameAs(child);
        canvas.BreadcrumbPath.ShouldBe(new[] { parent, child });
        canvas.Routing.CancelRouting();
    }

    [Fact]
    public void LeaveGroup_PreservesLogicalConnectionEvenBeforeRoutingCompletes()
    {
        var (canvas, parent, child) = CreateNested();
        canvas.EnterGroupEditMode(child);
        var first = canvas.Components.Single().Component;
        var second = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        canvas.AddComponent(second);
        var connection = canvas.ConnectPins(first.PhysicalPins[1], second.PhysicalPins[0]);
        connection.ShouldNotBeNull();
        connection.Connection.RoutedPath.ShouldBeNull();

        canvas.ExitToRoot();
        canvas.EnterGroupEditMode(child);

        canvas.Connections.ShouldContain(wire => ReferenceEquals(wire.Connection.StartPin, first.PhysicalPins[1])
            && ReferenceEquals(wire.Connection.EndPin, second.PhysicalPins[0]));
        canvas.Routing.CancelRouting();
    }

    [Fact]
    public void EnterNestedScope_PreservesPendingParentTopology()
    {
        var (canvas, parent, child) = CreateNested();
        canvas.EnterGroupEditMode(parent);
        var added = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        canvas.AddComponent(added);

        canvas.EnterGroupEditMode(child);
        canvas.ExitGroupEditMode();

        canvas.Components.ShouldContain(item => ReferenceEquals(item.Component, added));
        parent.ChildComponents.ShouldContain(added);
        canvas.CurrentEditGroup.ShouldBeSameAs(parent);
        canvas.Routing.CancelRouting();
    }

    [Fact]
    public void BreadcrumbNavigation_PreservesChildEditsAndLoadsRequestedScope()
    {
        var (canvas, parent, child) = CreateNested();
        canvas.EnterGroupEditMode(parent);
        canvas.EnterGroupEditMode(child);
        var added = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        canvas.AddComponent(added);

        canvas.NavigateToBreadcrumbLevel(parent);

        canvas.CurrentEditGroup.ShouldBeSameAs(parent);
        child.ChildComponents.ShouldContain(added);
        canvas.Components.Select(item => item.Component).ShouldBe(parent.ChildComponents);
        canvas.BreadcrumbPath.ShouldBe(new[] { parent });
        canvas.Routing.CancelRouting();
    }

    [Fact]
    public void RootDocumentRead_ContainsCurrentTopologyWithoutChangingVisibleScope()
    {
        var (canvas, parent, child) = CreateNested();
        canvas.EnterGroupEditMode(parent);
        var added = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        canvas.AddComponent(added);
        var scopeChanges = 0;
        canvas.Groups.EditStateChanged += () => scopeChanges++;

        var root = canvas.Groups.RootComponents.Single().Component.ShouldBeOfType<ComponentGroup>();

        root.ChildComponents.ShouldContain(added, "the root document must stay current while a scope is edited");
        root.ChildComponents.ShouldContain(child);
        canvas.CurrentEditGroup.ShouldBeSameAs(parent);
        scopeChanges.ShouldBe(0);
        canvas.Components.ShouldContain(item => ReferenceEquals(item.Component, added));
        canvas.Routing.CancelRouting();
    }

    private static (DesignCanvasViewModel Canvas, ComponentGroup Parent, ComponentGroup Child) CreateNested()
    {
        var canvas = new DesignCanvasViewModel();
        var parent = new ComponentGroup("parent");
        var child = new ComponentGroup("child");
        child.AddChild(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        parent.AddChild(child);
        parent.AddChild(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        canvas.AddComponent(parent);
        return (canvas, parent, child);
    }
}
