using CAP.Avalonia.ViewModels;
using Shouldly;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for PDK management (PdkManagerViewModel + MainViewModel filtering).
/// </summary>
public class PdkManagementIntegrationTests
{
    [Fact]
    public void PdkManager_FilteringIntegration_DisablingPdkHidesComponents()
    {
        // Arrange
        var pdkManager = new PdkManagerViewModel();
        var filteredComponents = new List<string>();
        var mockComponents = new List<MockComponentTemplate>
        {
            new("Component A", "PDK1"),
            new("Component B", "PDK1"),
            new("Component C", "PDK2"),
            new("Component D", "PDK2")
        };

        pdkManager.RegisterPdk("PDK1", null, true, 2);
        pdkManager.RegisterPdk("PDK2", null, true, 2);

        // Act: Disable PDK2
        pdkManager.LoadedPdks[1].IsEnabled = false;
        var enabledPdks = pdkManager.GetEnabledPdkNames();

        // Filter components
        filteredComponents = mockComponents
            .Where(c => enabledPdks.Contains(c.PdkSource))
            .Select(c => c.Name)
            .ToList();

        // Assert
        filteredComponents.Count.ShouldBe(2);
        filteredComponents.ShouldContain("Component A");
        filteredComponents.ShouldContain("Component B");
        filteredComponents.ShouldNotContain("Component C");
        filteredComponents.ShouldNotContain("Component D");
    }

    [Fact]
    public void PdkManager_EnableAll_ShowsAllComponents()
    {
        // Arrange
        var pdkManager = new PdkManagerViewModel();
        var mockComponents = new List<MockComponentTemplate>
        {
            new("Component A", "PDK1"),
            new("Component B", "PDK2"),
            new("Component C", "PDK3")
        };

        pdkManager.RegisterPdk("PDK1", null, true, 1);
        pdkManager.RegisterPdk("PDK2", null, true, 1);
        pdkManager.RegisterPdk("PDK3", null, true, 1);

        // Disable all
        pdkManager.LoadedPdks[0].IsEnabled = false;
        pdkManager.LoadedPdks[1].IsEnabled = false;
        pdkManager.LoadedPdks[2].IsEnabled = false;

        // Act: Enable all
        pdkManager.EnableAllCommand.Execute(null);
        var enabledPdks = pdkManager.GetEnabledPdkNames();

        var filteredComponents = mockComponents
            .Where(c => enabledPdks.Contains(c.PdkSource))
            .ToList();

        // Assert
        filteredComponents.Count.ShouldBe(3);
        enabledPdks.Count.ShouldBe(3);
    }

    [Fact]
    public void PdkManager_DisableAll_HidesAllComponents()
    {
        // Arrange
        var pdkManager = new PdkManagerViewModel();
        var mockComponents = new List<MockComponentTemplate>
        {
            new("Component A", "PDK1"),
            new("Component B", "PDK2")
        };

        pdkManager.RegisterPdk("PDK1", null, true, 1);
        pdkManager.RegisterPdk("PDK2", null, true, 1);

        // Act: Disable all
        pdkManager.DisableAllCommand.Execute(null);
        var enabledPdks = pdkManager.GetEnabledPdkNames();

        var filteredComponents = mockComponents
            .Where(c => enabledPdks.Contains(c.PdkSource))
            .ToList();

        // Assert
        filteredComponents.Count.ShouldBe(0);
        enabledPdks.Count.ShouldBe(0);
    }

    [Fact]
    public void PdkManager_SelectiveFiltering_ShowsOnlySelectedPdks()
    {
        // Arrange
        var pdkManager = new PdkManagerViewModel();
        var mockComponents = new List<MockComponentTemplate>
        {
            new("Coupler", "Built-in Components"),
            new("Waveguide", "Built-in Components"),
            new("MMI", "SiEPIC PDK"),
            new("Modulator", "Custom PDK")
        };

        pdkManager.RegisterPdk("Built-in Components", null, true, 2);
        pdkManager.RegisterPdk("SiEPIC PDK", "/siepic.json", true, 1);
        pdkManager.RegisterPdk("Custom PDK", "/custom.json", false, 1);

        // Act: Disable built-in and custom, keep SiEPIC
        pdkManager.LoadedPdks[0].IsEnabled = false;
        pdkManager.LoadedPdks[2].IsEnabled = false;

        var enabledPdks = pdkManager.GetEnabledPdkNames();
        var filteredComponents = mockComponents
            .Where(c => enabledPdks.Contains(c.PdkSource))
            .Select(c => c.Name)
            .ToList();

        // Assert
        filteredComponents.Count.ShouldBe(1);
        filteredComponents.ShouldContain("MMI");
    }

    [Fact]
    public void PdkManager_DuplicateDetection_PreventsSameFileLoad()
    {
        var pdkManager = new PdkManagerViewModel();
        var testPath = Path.GetFullPath("/test/pdk.json");

        pdkManager.RegisterPdk("Test PDK", testPath, false, 5);

        var isDuplicate = pdkManager.IsPdkLoaded(testPath);

        isDuplicate.ShouldBeTrue();
    }

    [Fact]
    public void PdkManager_NameDuplicateDetection_PreventsSameNameLoad()
    {
        var pdkManager = new PdkManagerViewModel();

        pdkManager.RegisterPdk("Demo PDK", null, true, 5);

        var isDuplicate = pdkManager.IsPdkNameLoaded("Demo PDK", null);

        isDuplicate.ShouldBeTrue();
    }

    /// <summary>
    /// Mock component template for testing filtering.
    /// </summary>
    private record MockComponentTemplate(string Name, string PdkSource);
}
