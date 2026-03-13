using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for group edit mode functionality in DesignCanvasViewModel.
/// </summary>
public class GroupEditModeTests
{
    [Fact]
    public void EnterGroupEditMode_SetsCurrentEditGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);

        // Act
        canvas.EnterGroupEditMode(group);

        // Assert
        canvas.CurrentEditGroup.ShouldBe(group);
        canvas.IsInGroupEditMode.ShouldBeTrue();
    }

    [Fact]
    public void ExitGroupEditMode_ClearsCurrentEditGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);

        // Act
        canvas.ExitGroupEditMode();

        // Assert
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
    }

    [Fact]
    public void ExitToRoot_ClearsNestedEditStack()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child");
        parentGroup.AddChild(childGroup);
        canvas.AddComponent(parentGroup);

        canvas.EnterGroupEditMode(parentGroup);
        canvas.EnterGroupEditMode(childGroup);

        // Act
        canvas.ExitToRoot();

        // Assert
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
        canvas.BreadcrumbPath.Count.ShouldBe(0);
    }

    [Fact]
    public void EnterGroupEditMode_UpdatesBreadcrumbPath()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child");
        parentGroup.AddChild(childGroup);
        canvas.AddComponent(parentGroup);

        // Act
        canvas.EnterGroupEditMode(parentGroup);

        // Assert
        canvas.BreadcrumbPath.Count.ShouldBe(1);
        canvas.BreadcrumbPath[0].ShouldBe(parentGroup);
    }

    [Fact]
    public void EnterGroupEditMode_NestedGroup_UpdatesBreadcrumbPath()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var parentGroup = new ComponentGroup("Parent");
        var childGroup = new ComponentGroup("Child");
        parentGroup.AddChild(childGroup);
        canvas.AddComponent(parentGroup);

        // Act
        canvas.EnterGroupEditMode(parentGroup);
        canvas.EnterGroupEditMode(childGroup);

        // Assert
        canvas.BreadcrumbPath.Count.ShouldBe(2);
        canvas.BreadcrumbPath[0].ShouldBe(parentGroup);
        canvas.BreadcrumbPath[1].ShouldBe(childGroup);
    }

    [Fact]
    public void NavigateToBreadcrumbLevel_JumpsToSpecificLevel()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var level1 = new ComponentGroup("Level1");
        var level2 = new ComponentGroup("Level2");
        var level3 = new ComponentGroup("Level3");
        level1.AddChild(level2);
        level2.AddChild(level3);
        canvas.AddComponent(level1);

        canvas.EnterGroupEditMode(level1);
        canvas.EnterGroupEditMode(level2);
        canvas.EnterGroupEditMode(level3);

        // Act - Navigate back to level2
        canvas.NavigateToBreadcrumbLevel(level2);

        // Assert
        canvas.CurrentEditGroup.ShouldBe(level2);
        canvas.BreadcrumbPath.Count.ShouldBe(2);
    }

    [Fact]
    public void NavigateToBreadcrumbLevel_WithNull_ExitsToRoot()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        canvas.EnterGroupEditMode(group);

        // Act
        canvas.NavigateToBreadcrumbLevel(null);

        // Assert
        canvas.CurrentEditGroup.ShouldBeNull();
        canvas.IsInGroupEditMode.ShouldBeFalse();
    }

    [Fact]
    public void HierarchyPanel_SyncEditModeFromCanvas_HighlightsEditedGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        hierarchy.RebuildTree();

        // Act
        hierarchy.SyncEditModeFromCanvas(group);

        // Assert
        var groupNode = hierarchy.RootNodes[0];
        groupNode.IsInEditMode.ShouldBeTrue();
    }

    [Fact]
    public void HierarchyPanel_SyncEditModeFromCanvas_WithNull_ClearsAllFlags()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        var group = new ComponentGroup("TestGroup");
        canvas.AddComponent(group);
        hierarchy.RebuildTree();
        hierarchy.SyncEditModeFromCanvas(group);

        // Act
        hierarchy.SyncEditModeFromCanvas(null);

        // Assert
        var groupNode = hierarchy.RootNodes[0];
        groupNode.IsInEditMode.ShouldBeFalse();
    }

    [Fact]
    public void EnterGroupEditMode_ThrowsOnNullGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => canvas.EnterGroupEditMode(null!));
    }
}
