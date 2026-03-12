using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Integration tests for ComponentGroup core logic + ViewModel.
/// Tests the interaction between ComponentGroup and ComponentGroupViewModel.
/// </summary>
public class ComponentGroupIntegrationTests
{
    [Fact]
    public void ViewModel_WithNoCanvas_ShouldDisableGroupCreation()
    {
        // Arrange
        var viewModel = new ComponentGroupViewModel();

        // Act
        viewModel.ConfigureForCanvas(null);

        // Assert
        viewModel.CanCreateGroup.ShouldBeFalse();
        viewModel.SelectedComponentCount.ShouldBe(0);
    }

    [Fact]
    public void ViewModel_WithTwoSelectedComponents_ShouldEnableGroupCreation()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var viewModel = new ComponentGroupViewModel();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        var compVm1 = new ComponentViewModel(comp1);
        var compVm2 = new ComponentViewModel(comp2);

        canvas.Components.Add(compVm1);
        canvas.Components.Add(compVm2);

        compVm1.IsSelected = true;
        compVm2.IsSelected = true;

        // Act
        viewModel.ConfigureForCanvas(canvas);
        viewModel.UpdateCanCreateGroup();

        // Assert
        viewModel.CanCreateGroup.ShouldBeTrue();
        viewModel.SelectedComponentCount.ShouldBe(2);
        viewModel.StatusText.ShouldContain("2 components selected");
    }

    [Fact]
    public void ViewModel_WithOneSelectedComponent_ShouldDisableGroupCreation()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var viewModel = new ComponentGroupViewModel();

        var comp = TestComponentFactory.CreateBasicComponent();
        var compVm = new ComponentViewModel(comp);

        canvas.Components.Add(compVm);
        compVm.IsSelected = true;

        // Act
        viewModel.ConfigureForCanvas(canvas);
        viewModel.UpdateCanCreateGroup();

        // Assert
        viewModel.CanCreateGroup.ShouldBeFalse();
        viewModel.SelectedComponentCount.ShouldBe(1);
        viewModel.StatusText.ShouldContain("at least 2 components");
    }

    [Fact]
    public void CreateGroupCommand_WithValidSelection_ShouldCreateGroup()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var viewModel = new ComponentGroupViewModel();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 100;
        comp2.PhysicalY = 100;

        var compVm1 = new ComponentViewModel(comp1);
        var compVm2 = new ComponentViewModel(comp2);

        canvas.Components.Add(compVm1);
        canvas.Components.Add(compVm2);

        compVm1.IsSelected = true;
        compVm2.IsSelected = true;

        viewModel.ConfigureForCanvas(canvas);
        viewModel.UpdateCanCreateGroup();
        viewModel.GroupName = "Test Integration Group";
        viewModel.GroupDescription = "Integration test group";

        // Act
        viewModel.CreateGroupCommand.Execute(null);

        // Assert
        viewModel.StatusText.ShouldContain("Created group");
        viewModel.StatusText.ShouldContain("Test Integration Group");
        viewModel.GroupInfoText.ShouldContain("Group: Test Integration Group");
        viewModel.GroupInfoText.ShouldContain("Children: 2");
    }

    [Fact]
    public void CreateGroupCommand_SetsParentGroupOnComponents()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var viewModel = new ComponentGroupViewModel();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        var compVm1 = new ComponentViewModel(comp1);
        var compVm2 = new ComponentViewModel(comp2);

        canvas.Components.Add(compVm1);
        canvas.Components.Add(compVm2);

        compVm1.IsSelected = true;
        compVm2.IsSelected = true;

        viewModel.ConfigureForCanvas(canvas);
        viewModel.UpdateCanCreateGroup();

        // Act
        viewModel.CreateGroupCommand.Execute(null);

        // Assert
        comp1.ParentGroup.ShouldNotBeNull();
        comp2.ParentGroup.ShouldNotBeNull();
        comp1.ParentGroup.ShouldBe(comp2.ParentGroup);
    }

    [Fact]
    public void CreateGroupCommand_WithConnectedComponents_ShouldCreateFrozenPaths()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var viewModel = new ComponentGroupViewModel();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        var compVm1 = new ComponentViewModel(comp1);
        var compVm2 = new ComponentViewModel(comp2);

        canvas.Components.Add(compVm1);
        canvas.Components.Add(compVm2);

        // Create a connection with a routed path
        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        connection.RecalculateTransmission(); // This populates the RoutedPath
        var connVm = new WaveguideConnectionViewModel(connection);
        canvas.Connections.Add(connVm);

        compVm1.IsSelected = true;
        compVm2.IsSelected = true;

        viewModel.ConfigureForCanvas(canvas);
        viewModel.UpdateCanCreateGroup();

        // Act
        viewModel.CreateGroupCommand.Execute(null);

        // Assert
        viewModel.GroupInfoText.ShouldContain("Internal Paths: 1");
    }

    [Fact]
    public void TestMoveGroupCommand_WithCreatedGroup_ShouldUpdateInfo()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var viewModel = new ComponentGroupViewModel();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        var compVm1 = new ComponentViewModel(comp1);
        var compVm2 = new ComponentViewModel(comp2);

        canvas.Components.Add(compVm1);
        canvas.Components.Add(compVm2);

        compVm1.IsSelected = true;
        compVm2.IsSelected = true;

        viewModel.ConfigureForCanvas(canvas);
        viewModel.UpdateCanCreateGroup();
        viewModel.CreateGroupCommand.Execute(null);

        // Act
        viewModel.TestMoveGroupCommand.Execute(null);

        // Assert
        viewModel.StatusText.ShouldContain("Moved group");
        viewModel.StatusText.ShouldContain("frozen paths translated");
    }

    [Fact]
    public void TestRotateGroupCommand_WithCreatedGroup_ShouldUpdateInfo()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var viewModel = new ComponentGroupViewModel();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        var compVm1 = new ComponentViewModel(comp1);
        var compVm2 = new ComponentViewModel(comp2);

        canvas.Components.Add(compVm1);
        canvas.Components.Add(compVm2);

        compVm1.IsSelected = true;
        compVm2.IsSelected = true;

        viewModel.ConfigureForCanvas(canvas);
        viewModel.UpdateCanCreateGroup();
        viewModel.CreateGroupCommand.Execute(null);

        // Act
        viewModel.TestRotateGroupCommand.Execute(null);

        // Assert
        viewModel.StatusText.ShouldContain("Rotated group");
        viewModel.StatusText.ShouldContain("90°");
    }

    [Fact]
    public void ClearGroupCommand_ShouldResetGroupInfo()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var viewModel = new ComponentGroupViewModel();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        var compVm1 = new ComponentViewModel(comp1);
        var compVm2 = new ComponentViewModel(comp2);

        canvas.Components.Add(compVm1);
        canvas.Components.Add(compVm2);

        compVm1.IsSelected = true;
        compVm2.IsSelected = true;

        viewModel.ConfigureForCanvas(canvas);
        viewModel.UpdateCanCreateGroup();
        viewModel.CreateGroupCommand.Execute(null);

        // Act
        viewModel.ClearGroupCommand.Execute(null);

        // Assert
        viewModel.GroupInfoText.ShouldBe("No group selected");
        viewModel.StatusText.ShouldBeEmpty();
    }
}
