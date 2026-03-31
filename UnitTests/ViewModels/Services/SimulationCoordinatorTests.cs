using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP.Avalonia.Visualization;
using Shouldly;

namespace UnitTests.ViewModels.Services;

/// <summary>
/// Unit tests for SimulationCoordinator - simulation invalidation and power flow display.
/// </summary>
public class SimulationCoordinatorTests
{
    private readonly PowerFlowVisualizer _visualizer = new();
    private readonly SimulationCoordinator _coordinator;

    public SimulationCoordinatorTests()
    {
        _coordinator = new SimulationCoordinator(_visualizer);
    }

    [Fact]
    public void InvalidateSimulation_WhenOverlayActive_RequestsResimulation()
    {
        bool simulationRequested = false;
        _coordinator.SimulationRequested = () => simulationRequested = true;

        _coordinator.InvalidateSimulation(wasShowingOverlay: true);

        simulationRequested.ShouldBeTrue();
    }

    [Fact]
    public void InvalidateSimulation_WhenOverlayInactive_DoesNotRequestResimulation()
    {
        bool simulationRequested = false;
        _coordinator.SimulationRequested = () => simulationRequested = true;

        _coordinator.InvalidateSimulation(wasShowingOverlay: false);

        simulationRequested.ShouldBeFalse();
    }

    [Fact]
    public void InvalidateSimulation_ClearsVisualizerData()
    {
        // InvalidateSimulation calls visualizer.Clear() to remove data
        // and raises ShowPowerFlowChanged(false) to hide the overlay.
        bool? showPowerFlowValue = null;
        _coordinator.ShowPowerFlowChanged += (value, _) => showPowerFlowValue = value;

        _coordinator.InvalidateSimulation(wasShowingOverlay: false);

        showPowerFlowValue.ShouldBe(false);
    }

    [Fact]
    public void InvalidateSimulation_RaisesShowPowerFlowChanged_WithFalse()
    {
        bool? newValue = null;
        _coordinator.ShowPowerFlowChanged += (value, force) => newValue = value;

        _coordinator.InvalidateSimulation(wasShowingOverlay: true);

        newValue.ShouldBe(false);
    }

    [Fact]
    public void RequestResimulation_WhenShowPowerFlow_RequestsSimulation()
    {
        bool simulationRequested = false;
        _coordinator.SimulationRequested = () => simulationRequested = true;

        _coordinator.RequestResimulation(showPowerFlow: true);

        simulationRequested.ShouldBeTrue();
    }

    [Fact]
    public void RequestResimulation_WhenNotShowPowerFlow_DoesNothing()
    {
        bool simulationRequested = false;
        _coordinator.SimulationRequested = () => simulationRequested = true;

        _coordinator.RequestResimulation(showPowerFlow: false);

        simulationRequested.ShouldBeFalse();
    }

    [Fact]
    public void RefreshPowerFlowDisplay_EnablesVisualizer()
    {
        _visualizer.IsEnabled = false;

        _coordinator.RefreshPowerFlowDisplay(isAlreadyVisible: false);

        _visualizer.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void RefreshPowerFlowDisplay_WhenAlreadyVisible_ForceNotifies()
    {
        bool forceNotify = false;
        _coordinator.ShowPowerFlowChanged += (value, force) => forceNotify = force;

        _coordinator.RefreshPowerFlowDisplay(isAlreadyVisible: true);

        forceNotify.ShouldBeTrue();
    }

    [Fact]
    public void RefreshPowerFlowDisplay_WhenNotVisible_SetsShowPowerFlow()
    {
        bool? newValue = null;
        bool forceNotify = false;
        _coordinator.ShowPowerFlowChanged += (value, force) =>
        {
            newValue = value;
            forceNotify = force;
        };

        _coordinator.RefreshPowerFlowDisplay(isAlreadyVisible: false);

        newValue.ShouldBe(true);
        forceNotify.ShouldBeFalse();
    }
}
