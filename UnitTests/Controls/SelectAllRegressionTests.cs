using Avalonia;
using Avalonia.Input;
using CAP.Avalonia.Controls.Handlers;
using CAP.Avalonia.Selection;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Controls;

/// <summary>Selection shortcuts and bulk notification regressions.</summary>
public class SelectAllRegressionTests
{
    [Fact]
    public void RepeatedBulkSelection_DoesNotToggleEveryHierarchyNode()
    {
        var canvas = new DesignCanvasViewModel();
        for (var i = 0; i < 100; i++) canvas.AddComponent(new ComponentGroup { Identifier = $"Part {i}" });
        var hierarchy = new HierarchyPanelViewModel(canvas);
        canvas.Selection.SelectAll(canvas.Components, [], []);
        var toggles = 0;
        foreach (var node in hierarchy.RootNodes)
            node.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(node.IsSelected)) toggles++; };

        canvas.Selection.SelectAll(canvas.Components, [], []);

        hierarchy.RootNodes.ShouldAllBe(node => node.IsSelected);
        toggles.ShouldBe(0);
    }

    [Theory]
    [InlineData(Workspace.Layout)]
    [InlineData(Workspace.Optics)]
    [InlineData(Workspace.Logic)]
    [InlineData(Workspace.System)]
    public void ControlA_SelectsEveryComponentWithoutRepeatingPanelUpdates(Workspace workspace)
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        main.WorkspaceSwitch.Current = workspace;
        for (var i = 0; i < 100; i++)
            main.Canvas.AddComponent(new ComponentGroup { Identifier = $"Part {i}" });
        var notifications = 0;
        main.Canvas.Selection.SelectedComponents.CollectionChanged += (_, _) => notifications++;
        var handler = new KeyboardHandler(() => main.Canvas, () => main, () => new Rect(0, 0, 800, 600));
        var key = new KeyEventArgs { Key = Key.A, KeyModifiers = KeyModifiers.Control };

        handler.OnKeyDown(key);

        key.Handled.ShouldBeTrue();
        main.Canvas.Selection.SelectedComponents.Count.ShouldBe(100);
        main.Canvas.Components.ShouldAllBe(component => component.IsSelected);
        notifications.ShouldBe(1);
    }

    [Fact]
    public void RectangleSelection_NotifiesPanelsOnceForLargeSelection()
    {
        var selection = new SelectionManager();
        var components = Enumerable.Range(0, 2000).Select(i => new ComponentViewModel(
            new ComponentGroup { Identifier = $"Part {i}", PhysicalX = i, WidthMicrometers = 1,
                HeightMicrometers = 1 })).ToArray();
        var notifications = 0;
        selection.SelectedComponents.CollectionChanged += (_, _) => notifications++;

        selection.SelectInRectangle(components, -1, -1, 3000, 10);

        selection.SelectedComponents.Count.ShouldBe(2000);
        notifications.ShouldBe(1);
        components.ShouldAllBe(component => component.IsSelected);
    }

    [Fact]
    public void SelectAll_DoesNotSelectChildrenOfClosedGroups()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        var group = new ComponentGroup { Identifier = "Closed group" };
        group.ChildComponents.Add(new ComponentGroup { Identifier = "Hidden child" });
        main.Canvas.AddComponent(group);

        main.SelectAllCommand.Execute(null);

        main.Canvas.Selection.SelectedComponents.Count.ShouldBe(1);
        main.Canvas.Selection.SelectedComponents[0].Component.ShouldBeSameAs(group);
    }

    [Fact]
    public void SelectAll_InsideGroupDoesNotSelectRootBackgroundPaths()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        var group = new ComponentGroup { Identifier = "Group" };
        var child = new ComponentGroup { Identifier = "Child" };
        group.ChildComponents.Add(child);
        main.Canvas.AddComponent(group);
        var rootPath = new CanvasFrozenPathViewModel(new FrozenWaveguidePath
            { Path = new CAP_Core.Routing.RoutedPath() });
        main.Canvas.CanvasFrozenPaths.Add(rootPath);
        main.Canvas.EnterGroupEditMode(group);

        main.SelectAllCommand.Execute(null);

        main.Canvas.Selection.SelectedComponents.Count.ShouldBe(1);
        main.Canvas.Selection.SelectedComponents[0].Component.ShouldBeSameAs(child);
        main.Canvas.Selection.SelectedFrozenPaths.ShouldBeEmpty();
        rootPath.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void RectangleSelection_AddAndSubtractKeepFlagsAndMembershipConsistent()
    {
        var selection = new SelectionManager();
        var first = new ComponentViewModel(new ComponentGroup { PhysicalX = 0, WidthMicrometers = 1, HeightMicrometers = 1 });
        var second = new ComponentViewModel(new ComponentGroup { PhysicalX = 10, WidthMicrometers = 1, HeightMicrometers = 1 });
        selection.SelectSingle(first);
        selection.SelectInRectangle([first, second], 9, -1, 12, 2, addToSelection: true);
        selection.SelectedComponents.Count.ShouldBe(2);

        selection.SelectInRectangle([first, second], -1, -1, 2, 2, removeFromSelection: true);

        selection.SelectedComponents.ShouldBe([second]);
        first.IsSelected.ShouldBeFalse();
        second.IsSelected.ShouldBeTrue();
    }
}
