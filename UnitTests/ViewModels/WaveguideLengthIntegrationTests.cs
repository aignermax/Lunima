using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for WaveguideLengthViewModel with core WaveguideConnection.
/// Tests the full vertical slice from core logic to ViewModel bindings.
/// </summary>
public class WaveguideLengthIntegrationTests
{
    [Fact]
    public void ViewModel_InitializesWithNoConnection()
    {
        var vm = new WaveguideLengthViewModel();

        vm.HasConnection.ShouldBeFalse();
        vm.SelectedConnection.ShouldBeNull();
        vm.ConnectionName.ShouldBe("No connection selected");
    }

    [Fact]
    public void ViewModel_UpdatesFromConnection()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        var vm = new WaveguideLengthViewModel();

        connection.TargetLengthMicrometers = 300.0;
        connection.IsTargetLengthEnabled = true;
        connection.LengthToleranceMicrometers = 2.5;

        vm.SelectedConnection = connVm;

        vm.HasConnection.ShouldBeTrue();
        vm.IsTargetLengthEnabled.ShouldBeTrue();
        vm.TargetLengthMicrometers.ShouldBe(300.0);
        vm.ToleranceMicrometers.ShouldBe(2.5);
    }

    [Fact]
    public void ViewModel_AppliesSettingsToConnection()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        var vm = new WaveguideLengthViewModel
        {
            SelectedConnection = connVm
        };

        vm.IsTargetLengthEnabled = true;
        vm.TargetLengthMicrometers = 500.0;
        vm.ToleranceMicrometers = 3.0;
        vm.ApplyTargetLengthCommand.Execute(null);

        connection.IsTargetLengthEnabled.ShouldBeTrue();
        connection.TargetLengthMicrometers.ShouldBe(500.0);
        connection.LengthToleranceMicrometers.ShouldBe(3.0);
    }

    [Fact]
    public void ViewModel_SetTargetToCurrentCommand_SetsCorrectValue()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        connection.RecalculateTransmission();

        var vm = new WaveguideLengthViewModel
        {
            SelectedConnection = connVm
        };
        vm.UpdateLengthStatus();

        var currentLength = connection.PathLengthMicrometers;

        vm.SetTargetToCurrentCommand.Execute(null);

        connection.TargetLengthMicrometers.HasValue.ShouldBeTrue();
        connection.TargetLengthMicrometers.Value.ShouldBe(currentLength, 0.01);
    }

    [Fact]
    public void ViewModel_UpdatesLengthStatus_WithMatchedLength()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        connection.RecalculateTransmission();

        var actualLength = connection.PathLengthMicrometers;
        connection.TargetLengthMicrometers = actualLength + 0.5;
        connection.IsTargetLengthEnabled = true;
        connection.LengthToleranceMicrometers = 1.0;

        var vm = new WaveguideLengthViewModel
        {
            SelectedConnection = connVm
        };
        vm.UpdateLengthStatus();

        vm.LengthStatusColor.ShouldBe("LightGreen");
        vm.LengthStatusText.ShouldContain("✓ Matched");
    }

    [Fact]
    public void ViewModel_UpdatesLengthStatus_WithTooShortPath()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        connection.RecalculateTransmission();

        var actualLength = connection.PathLengthMicrometers;
        connection.TargetLengthMicrometers = actualLength + 10.0;
        connection.IsTargetLengthEnabled = true;
        connection.LengthToleranceMicrometers = 1.0;

        var vm = new WaveguideLengthViewModel
        {
            SelectedConnection = connVm
        };
        vm.UpdateLengthStatus();

        vm.LengthStatusColor.ShouldBe("Orange");
        vm.LengthStatusText.ShouldContain("⚠ Too short");
    }

    [Fact]
    public void ViewModel_UpdatesLengthStatus_WithTooLongPath()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        connection.RecalculateTransmission();

        var actualLength = connection.PathLengthMicrometers;
        connection.TargetLengthMicrometers = actualLength - 10.0;
        connection.IsTargetLengthEnabled = true;
        connection.LengthToleranceMicrometers = 1.0;

        var vm = new WaveguideLengthViewModel
        {
            SelectedConnection = connVm
        };
        vm.UpdateLengthStatus();

        vm.LengthStatusColor.ShouldBe("Orange");
        vm.LengthStatusText.ShouldContain("⚠ Too long");
    }

    [Fact]
    public void ViewModel_ShowsCurrentLengthWhenTargetDisabled()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        connection.RecalculateTransmission();
        connection.IsTargetLengthEnabled = false;

        var vm = new WaveguideLengthViewModel
        {
            SelectedConnection = connVm
        };
        vm.UpdateLengthStatus();

        vm.LengthStatusColor.ShouldBe("White");
        vm.LengthStatusText.ShouldContain("Current:");
    }

    [Fact]
    public void ViewModel_DisableTargetLength_ClearsConstraint()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        connection.IsTargetLengthEnabled = true;
        connection.TargetLengthMicrometers = 300.0;

        var vm = new WaveguideLengthViewModel
        {
            SelectedConnection = connVm
        };

        vm.DisableTargetLengthCommand.Execute(null);

        connection.IsTargetLengthEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ViewModel_EnableTargetLength_ActivatesConstraint()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();
        connection.IsTargetLengthEnabled = false;
        connection.TargetLengthMicrometers = 300.0;

        var vm = new WaveguideLengthViewModel
        {
            SelectedConnection = connVm
        };

        vm.EnableTargetLengthCommand.Execute(null);

        connection.IsTargetLengthEnabled.ShouldBeTrue();
    }

    [Fact]
    public void WaveguideConnectionViewModel_ExposesTargetLengthProperties()
    {
        var (connection, connVm) = CreateTestConnectionAndViewModel();

        connection.TargetLengthMicrometers = 400.0;
        connection.IsTargetLengthEnabled = true;
        connection.LengthToleranceMicrometers = 2.0;
        connection.RecalculateTransmission();

        connVm.IsTargetLengthEnabled.ShouldBeTrue();
        connVm.TargetLengthMicrometers.ShouldBe(400.0);
        connVm.LengthDifference.ShouldNotBeNull();
        connVm.IsLengthMatched.ShouldNotBeNull();
    }

    /// <summary>
    /// Creates a test connection and its ViewModel wrapper.
    /// </summary>
    private (WaveguideConnection connection, WaveguideConnectionViewModel viewModel)
        CreateTestConnectionAndViewModel()
    {
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 0;

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        var viewModel = new WaveguideConnectionViewModel(connection);

        return (connection, viewModel);
    }
}
