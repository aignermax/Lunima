using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Panels;

/// <summary>
/// Unit tests for RightPanelViewModel (properties, sweep, diagnostics).
/// </summary>
public class RightPanelViewModelTests
{
    [Fact]
    public void Constructor_InitializesAllSubViewModels()
    {
        // Arrange & Act
        var vm = new RightPanelViewModel();

        // Assert
        vm.Sweep.ShouldNotBeNull();
        vm.DesignValidation.ShouldNotBeNull();
        vm.RoutingDiagnostics.ShouldNotBeNull();
        vm.DimensionValidator.ShouldNotBeNull();
        vm.ExportValidation.ShouldNotBeNull();
        vm.SMatrixPerformance.ShouldNotBeNull();
        vm.CompressLayout.ShouldNotBeNull();
        vm.WaveguideLength.ShouldNotBeNull();
    }

    [Fact]
    public void SelectedComponent_DefaultsToNull()
    {
        // Arrange & Act
        var vm = new RightPanelViewModel();

        // Assert
        vm.SelectedComponent.ShouldBeNull();
    }

    [Fact]
    public void SelectedWaveguideConnection_DefaultsToNull()
    {
        // Arrange & Act
        var vm = new RightPanelViewModel();

        // Assert
        vm.SelectedWaveguideConnection.ShouldBeNull();
    }

    [Fact]
    public void WavelengthOptions_ReturnsAllOptions()
    {
        // Arrange & Act
        var vm = new RightPanelViewModel();

        // Assert
        vm.WavelengthOptions.ShouldNotBeNull();
        vm.WavelengthOptions.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void DimensionDiagnostics_InitiallyNull_UntilConfigured()
    {
        // Arrange & Act
        var vm = new RightPanelViewModel();

        // Assert
        // DimensionDiagnostics is set to null initially because it requires a Canvas reference
        vm.DimensionDiagnostics.ShouldBeNull();
    }
}
