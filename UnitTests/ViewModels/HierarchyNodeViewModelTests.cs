using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using UnitTests;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for HierarchyNodeViewModel.
/// </summary>
public class HierarchyNodeViewModelTests
{
    [Fact]
    public void Constructor_SetsComponentAndDisplayName()
    {
        // Arrange
        var component = CreateTestComponent("Test Component");

        // Act
        var node = new HierarchyNodeViewModel(component);

        // Assert
        node.Component.ShouldBe(component);
        node.DisplayName.ShouldBe("Straight"); // Component.Identifier from factory
        node.IconGlyph.ShouldBe("📦");
    }

    [Fact]
    public void IsGroup_ReturnsFalse_WhenNoChildren()
    {
        // Arrange
        var component = CreateTestComponent("Single Component");
        var node = new HierarchyNodeViewModel(component);

        // Act & Assert
        node.IsGroup.ShouldBeFalse();
    }

    [Fact]
    public void IsGroup_ReturnsTrue_WhenHasChildren()
    {
        // Arrange
        var parentComponent = CreateTestComponent("Parent");
        var childComponent = CreateTestComponent("Child");
        var parentNode = new HierarchyNodeViewModel(parentComponent);
        var childNode = new HierarchyNodeViewModel(childComponent);

        // Act
        parentNode.AddChild(childNode);

        // Assert
        parentNode.IsGroup.ShouldBeTrue();
        parentNode.Children.Count.ShouldBe(1);
    }

    [Fact]
    public void AddChild_UpdatesDisplayName()
    {
        // Arrange
        var parentComponent = CreateTestComponent("Parent Group");
        var childComponent = CreateTestComponent("Child");
        var parentNode = new HierarchyNodeViewModel(parentComponent);
        var childNode = new HierarchyNodeViewModel(childComponent);

        // Act
        parentNode.AddChild(childNode);

        // Assert
        parentNode.DisplayName.ShouldBe("Straight (1 component)");
        parentNode.IconGlyph.ShouldBe("📁"); // Closed folder when not expanded
    }

    [Fact]
    public void AddChild_UpdatesDisplayName_WithMultipleChildren()
    {
        // Arrange
        var parentComponent = CreateTestComponent("Parent Group");
        var parentNode = new HierarchyNodeViewModel(parentComponent);

        // Act
        parentNode.AddChild(new HierarchyNodeViewModel(CreateTestComponent("Child 1")));
        parentNode.AddChild(new HierarchyNodeViewModel(CreateTestComponent("Child 2")));
        parentNode.AddChild(new HierarchyNodeViewModel(CreateTestComponent("Child 3")));

        // Assert
        parentNode.DisplayName.ShouldBe("Straight (3 components)");
    }

    [Fact]
    public void RemoveChild_UpdatesDisplayName()
    {
        // Arrange
        var parentComponent = CreateTestComponent("Parent Group");
        var childComponent = CreateTestComponent("Child");
        var parentNode = new HierarchyNodeViewModel(parentComponent);
        var childNode = new HierarchyNodeViewModel(childComponent);
        parentNode.AddChild(childNode);

        // Act
        parentNode.RemoveChild(childNode);

        // Assert
        parentNode.Children.Count.ShouldBe(0);
        parentNode.IsGroup.ShouldBeFalse();
        parentNode.DisplayName.ShouldBe("Straight");
        parentNode.IconGlyph.ShouldBe("📦"); // Back to component icon
    }

    [Fact]
    public void IsExpanded_ChangesIconGlyph()
    {
        // Arrange
        var parentComponent = CreateTestComponent("Parent Group");
        var childComponent = CreateTestComponent("Child");
        var parentNode = new HierarchyNodeViewModel(parentComponent);
        var childNode = new HierarchyNodeViewModel(childComponent);
        parentNode.AddChild(childNode);

        // Act - Expand
        parentNode.IsExpanded = true;

        // Assert
        parentNode.IconGlyph.ShouldBe("📂"); // Open folder

        // Act - Collapse
        parentNode.IsExpanded = false;

        // Assert
        parentNode.IconGlyph.ShouldBe("📁"); // Closed folder
    }

    [Fact]
    public void ToggleExpandedCommand_TogglesExpandedState()
    {
        // Arrange
        var component = CreateTestComponent("Group");
        var node = new HierarchyNodeViewModel(component);
        node.AddChild(new HierarchyNodeViewModel(CreateTestComponent("Child")));
        var initialState = node.IsExpanded;

        // Act
        node.ToggleExpandedCommand.Execute(null);

        // Assert
        node.IsExpanded.ShouldNotBe(initialState);

        // Act again
        node.ToggleExpandedCommand.Execute(null);

        // Assert
        node.IsExpanded.ShouldBe(initialState);
    }

    [Fact]
    public void FocusComponentCommand_InvokesFocusCallback()
    {
        // Arrange
        var component = CreateTestComponent("Test Component");
        var node = new HierarchyNodeViewModel(component);
        ComponentViewModel? focusedComponent = null;
        node.OnFocusRequested = c => focusedComponent = c;

        // Act
        node.FocusComponentCommand.Execute(null);

        // Assert
        focusedComponent.ShouldBe(component);
    }

    private static ComponentViewModel CreateTestComponent(string templateName)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        return new ComponentViewModel(component, templateName);
    }
}
