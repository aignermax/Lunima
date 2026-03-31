using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Creation;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for the complete flow from viewport component selection
/// to properties panel display via CanvasInteractionViewModel.
/// </summary>
public class SelectionPropertiesPanelIntegrationTests
{
    private static (DesignCanvasViewModel canvas, CanvasInteractionViewModel interaction) CreateSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var libraryManager = new GroupLibraryManager();
        var library = new ComponentLibraryViewModel(libraryManager);
        var interaction = new CanvasInteractionViewModel(canvas, commandManager, library);
        return (canvas, interaction);
    }

    /// <summary>
    /// Scenario 1: Selecting a component in the viewport populates the properties panel
    /// with correct name, position, and dimensions.
    /// </summary>
    [Fact]
    public void SelectComponent_PropertiesPanelShowsComponentData()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        var component = TestComponentFactory.CreateBasicComponent();
        component.HumanReadableName = "MMI 1x2";
        component.PhysicalX = 50;
        component.PhysicalY = 80;
        component.WidthMicrometers = 250;
        component.HeightMicrometers = 250;

        var componentVm = canvas.AddComponent(component, "TestTemplate");
        componentVm.X = component.PhysicalX;
        componentVm.Y = component.PhysicalY;

        // Act: simulate viewport click at center of component
        interaction.CanvasClicked(component.PhysicalX + 10, component.PhysicalY + 10);

        // Assert
        interaction.SelectedComponent.ShouldNotBeNull();
        interaction.SelectedComponent.DisplayName.ShouldBe("MMI 1x2");
        interaction.SelectedComponent.X.ShouldBe(component.PhysicalX);
        interaction.SelectedComponent.Y.ShouldBe(component.PhysicalY);
        interaction.SelectedComponent.Width.ShouldBe(250);
        interaction.SelectedComponent.Height.ShouldBe(250);
        componentVm.IsSelected.ShouldBeTrue();
    }

    /// <summary>
    /// Scenario 2: Selecting a ComponentGroup shows the group name in the properties panel.
    /// </summary>
    [Fact]
    public void SelectComponentGroup_PropertiesPanelShowsGroupName()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        var group = TestComponentFactory.CreateComponentGroup("Mach-Zehnder Interferometer");
        group.HumanReadableName = "Mach-Zehnder Interferometer";
        group.PhysicalX = 100;
        group.PhysicalY = 100;
        group.WidthMicrometers = 200;
        group.HeightMicrometers = 100;

        var groupVm = canvas.AddComponent(group, "MZI");
        groupVm.X = group.PhysicalX;
        groupVm.Y = group.PhysicalY;

        // Act: simulate viewport click inside group bounds
        interaction.CanvasClicked(group.PhysicalX + 10, group.PhysicalY + 10);

        // Assert
        interaction.SelectedComponent.ShouldNotBeNull();
        interaction.SelectedComponent.DisplayName.ShouldBe("Mach-Zehnder Interferometer");
        interaction.SelectedComponent.IsComponentGroup.ShouldBeTrue();
        groupVm.IsSelected.ShouldBeTrue();
    }

    /// <summary>
    /// Scenario 3: Clicking an empty area clears the selected component in the properties panel.
    /// </summary>
    [Fact]
    public void ClickEmptyArea_PropertiesPanelClears()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = 100;
        component.PhysicalY = 100;
        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;

        var componentVm = canvas.AddComponent(component);
        componentVm.X = component.PhysicalX;
        componentVm.Y = component.PhysicalY;

        // Select it first
        interaction.CanvasClicked(component.PhysicalX + 10, component.PhysicalY + 10);
        interaction.SelectedComponent.ShouldNotBeNull();

        // Act: click empty area far from any component
        interaction.CanvasClicked(500, 500);

        // Assert
        interaction.SelectedComponent.ShouldBeNull();
    }

    /// <summary>
    /// Scenario 4: Selecting a different component updates the properties panel
    /// and deselects the previously selected component.
    /// </summary>
    [Fact]
    public void SelectDifferentComponent_PropertiesPanelUpdates()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.HumanReadableName = "First Component";
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp1.WidthMicrometers = 50;
        comp1.HeightMicrometers = 50;

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.HumanReadableName = "Second Component";
        comp2.PhysicalX = 200;
        comp2.PhysicalY = 0;
        comp2.WidthMicrometers = 50;
        comp2.HeightMicrometers = 50;

        var vm1 = canvas.AddComponent(comp1);
        vm1.X = comp1.PhysicalX;
        vm1.Y = comp1.PhysicalY;

        var vm2 = canvas.AddComponent(comp2);
        vm2.X = comp2.PhysicalX;
        vm2.Y = comp2.PhysicalY;

        // Select first component
        interaction.CanvasClicked(comp1.PhysicalX + 10, comp1.PhysicalY + 10);
        interaction.SelectedComponent?.DisplayName.ShouldBe("First Component");

        // Act: select second component
        interaction.CanvasClicked(comp2.PhysicalX + 10, comp2.PhysicalY + 10);

        // Assert
        vm1.IsSelected.ShouldBeFalse();
        vm2.IsSelected.ShouldBeTrue();
        interaction.SelectedComponent.ShouldNotBeNull();
        interaction.SelectedComponent.DisplayName.ShouldBe("Second Component");
    }

    /// <summary>
    /// Scenario 5: A component without HumanReadableName falls back to Name/Identifier
    /// for the properties panel display.
    /// </summary>
    [Fact]
    public void SelectComponent_WithoutHumanReadableName_FallsBackToName()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        var component = TestComponentFactory.CreateStraightWaveGuide();
        // Do NOT set HumanReadableName — leave it null
        component.PhysicalX = 0;
        component.PhysicalY = 0;
        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;

        var componentVm = canvas.AddComponent(component);
        componentVm.X = component.PhysicalX;
        componentVm.Y = component.PhysicalY;

        // Act
        interaction.CanvasClicked(10, 10);

        // Assert: DisplayName falls back to Component.Name
        interaction.SelectedComponent.ShouldNotBeNull();
        interaction.SelectedComponent.DisplayName.ShouldBe(component.Name);
        interaction.SelectedComponent.DisplayName.ShouldNotBeNullOrEmpty();
    }

    /// <summary>
    /// Scenario 6: ComponentTypeName returns the Nazca function name when set.
    /// </summary>
    [Fact]
    public void SelectComponent_ComponentTypeNameShowsNazcaFunctionName()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        var component = TestComponentFactory.CreateBasicComponent();
        component.NazcaFunctionName = "ebeam_gc_te1550";
        component.PhysicalX = 0;
        component.PhysicalY = 0;
        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;

        var componentVm = canvas.AddComponent(component, "Grating Coupler");
        componentVm.X = component.PhysicalX;
        componentVm.Y = component.PhysicalY;

        // Act
        interaction.CanvasClicked(10, 10);

        // Assert: Nazca function name takes priority
        interaction.SelectedComponent.ShouldNotBeNull();
        interaction.SelectedComponent.ComponentTypeName.ShouldBe("ebeam_gc_te1550");
    }

    /// <summary>
    /// Scenario 7: ComponentTypeName falls back to TemplateName when NazcaFunctionName is empty.
    /// </summary>
    [Fact]
    public void SelectComponent_ComponentTypeNameFallsBackToTemplateName()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        var component = TestComponentFactory.CreateBasicComponent();
        component.NazcaFunctionName = "";
        component.PhysicalX = 0;
        component.PhysicalY = 0;
        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;

        var componentVm = canvas.AddComponent(component, "Phase Shifter");
        componentVm.X = component.PhysicalX;
        componentVm.Y = component.PhysicalY;

        // Act
        interaction.CanvasClicked(10, 10);

        // Assert: falls back to template name
        interaction.SelectedComponent.ShouldNotBeNull();
        interaction.SelectedComponent.ComponentTypeName.ShouldBe("Phase Shifter");
    }

    /// <summary>
    /// Scenario 8: ComponentTypeName is null for ComponentGroups.
    /// </summary>
    [Fact]
    public void SelectComponentGroup_ComponentTypeNameIsNull()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        var group = TestComponentFactory.CreateComponentGroup("MZI");
        group.PhysicalX = 0;
        group.PhysicalY = 0;
        group.WidthMicrometers = 200;
        group.HeightMicrometers = 100;

        var groupVm = canvas.AddComponent(group, "MZI");
        groupVm.X = group.PhysicalX;
        groupVm.Y = group.PhysicalY;

        // Act
        interaction.CanvasClicked(10, 10);

        // Assert: groups have no type label
        interaction.SelectedComponent.ShouldNotBeNull();
        interaction.SelectedComponent.ComponentTypeName.ShouldBeNull();
    }

    /// <summary>
    /// Verifies OnSelectionChanged callback is invoked when a component is selected.
    /// </summary>
    [Fact]
    public void SelectComponent_InvokesOnSelectionChangedCallback()
    {
        // Arrange
        var (canvas, interaction) = CreateSetup();

        ComponentViewModel? callbackArg = null;
        interaction.OnSelectionChanged = vm => callbackArg = vm;

        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = 0;
        component.PhysicalY = 0;
        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;

        var componentVm = canvas.AddComponent(component);
        componentVm.X = component.PhysicalX;
        componentVm.Y = component.PhysicalY;

        // Act
        interaction.CanvasClicked(10, 10);

        // Assert
        callbackArg.ShouldNotBeNull();
        callbackArg.ShouldBe(componentVm);
    }
}
