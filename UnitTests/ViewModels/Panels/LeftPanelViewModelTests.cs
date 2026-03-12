using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Panels;

/// <summary>
/// Unit tests for LeftPanelViewModel (component library, search, PDK management).
/// </summary>
public class LeftPanelViewModelTests
{
    [Fact]
    public void Constructor_InitializesCollections()
    {
        // Arrange & Act
        var vm = new LeftPanelViewModel();

        // Assert
        vm.ComponentLibrary.ShouldNotBeNull();
        vm.FilteredComponentLibrary.ShouldNotBeNull();
        vm.Categories.ShouldNotBeNull();
        vm.PdkManager.ShouldNotBeNull();
        vm.ElementLock.ShouldNotBeNull();
    }

    [Fact]
    public void SearchText_DefaultsToEmpty()
    {
        // Arrange & Act
        var vm = new LeftPanelViewModel();

        // Assert
        vm.SearchText.ShouldBe("");
    }

    [Fact]
    public void SelectedTemplate_DefaultsToNull()
    {
        // Arrange & Act
        var vm = new LeftPanelViewModel();

        // Assert
        vm.SelectedTemplate.ShouldBeNull();
    }

    [Fact]
    public void FilterComponents_WithEmptyLibrary_ProducesEmptyFilteredList()
    {
        // Arrange
        var vm = new LeftPanelViewModel();
        var enabledPdks = new HashSet<string> { "Built-in Components" };

        // Act
        vm.FilterComponents(enabledPdks);

        // Assert
        vm.FilteredComponentLibrary.ShouldBeEmpty();
    }

    [Fact]
    public void FilterComponents_WithComponents_FiltersBasedOnEnabledPdks()
    {
        // Arrange
        var vm = new LeftPanelViewModel();

        var template1 = new ComponentTemplate
        {
            Name = "Component1",
            Category = "Couplers",
            PdkSource = "PDK-A"
        };

        var template2 = new ComponentTemplate
        {
            Name = "Component2",
            Category = "Modulators",
            PdkSource = "PDK-B"
        };

        vm.ComponentLibrary.Add(template1);
        vm.ComponentLibrary.Add(template2);

        // Act - Only enable PDK-A
        var enabledPdks = new HashSet<string> { "PDK-A" };
        vm.FilterComponents(enabledPdks);

        // Assert
        vm.FilteredComponentLibrary.Count.ShouldBe(1);
        vm.FilteredComponentLibrary[0].Name.ShouldBe("Component1");
    }

    [Fact]
    public void FilterComponents_WithSearchText_FiltersBasedOnNameAndCategory()
    {
        // Arrange
        var vm = new LeftPanelViewModel();

        var template1 = new ComponentTemplate
        {
            Name = "MZI",
            Category = "Modulators",
            PdkSource = "PDK-A"
        };

        var template2 = new ComponentTemplate
        {
            Name = "Grating",
            Category = "Couplers",
            PdkSource = "PDK-A"
        };

        vm.ComponentLibrary.Add(template1);
        vm.ComponentLibrary.Add(template2);
        vm.SearchText = "mzi";

        // Act
        var enabledPdks = new HashSet<string> { "PDK-A" };
        vm.FilterComponents(enabledPdks);

        // Assert
        vm.FilteredComponentLibrary.Count.ShouldBe(1);
        vm.FilteredComponentLibrary[0].Name.ShouldBe("MZI");
    }

    [Fact]
    public void FilterComponents_WithSearchText_IsCaseInsensitive()
    {
        // Arrange
        var vm = new LeftPanelViewModel();

        var template = new ComponentTemplate
        {
            Name = "GratingCoupler",
            Category = "Couplers",
            PdkSource = "PDK-A"
        };

        vm.ComponentLibrary.Add(template);
        vm.SearchText = "GRATING";

        // Act
        var enabledPdks = new HashSet<string> { "PDK-A" };
        vm.FilterComponents(enabledPdks);

        // Assert
        vm.FilteredComponentLibrary.Count.ShouldBe(1);
        vm.FilteredComponentLibrary[0].Name.ShouldBe("GratingCoupler");
    }

    [Fact]
    public void OnSearchTextChanged_InvokesOnFilterChanged()
    {
        // Arrange
        var vm = new LeftPanelViewModel();
        bool callbackInvoked = false;
        vm.OnFilterChanged = () => callbackInvoked = true;

        // Act
        vm.SearchText = "test";

        // Assert
        callbackInvoked.ShouldBeTrue();
    }
}
