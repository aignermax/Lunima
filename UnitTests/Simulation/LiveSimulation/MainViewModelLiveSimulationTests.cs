using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Simulation.LiveSimulation;

/// <summary>
/// With the CW simulation switched on, every design change re-runs it once routing is done:
/// a new connection carries light instead of "No signal", loading another design keeps the
/// simulation on and re-simulates it, and File → New / a design without a light source turn
/// it off.
/// </summary>
[Collection("LocalizationSingleton")]
public class MainViewModelLiveSimulationTests
{
    private const string DemoPdk = "Demo PDK";
    private const string Coupler = "Grating Coupler";
    private const string Straight = "Straight Waveguide 100µm";
    private const double MinVisiblePower = 1e-6;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task NewConnection_WhileSimulationIsOn_CarriesLightAfterRouting()
    {
        var vm = CreateVm();
        var (coupler, first, second) = PlaceLightPath(vm);
        await vm.Canvas.ConnectPinsAsync(Pin(coupler, "waveguide"), Pin(first, "a0"));
        await TurnSimulationOnAsync(vm);

        vm.CommandManager.ExecuteCommand(new CreateConnectionCommand(vm.Canvas, Pin(first, "b0"), Pin(second, "a0")));
        var added = ConnectionBetween(vm, Pin(first, "b0"));

        await WaitUntilAsync(vm, () => PowerOf(vm, added) > MinVisiblePower);
        added.IsPathValid.ShouldBeTrue("the overlay is computed on the finished route");
        vm.Canvas.ShowPowerFlow.ShouldBeTrue();
        vm.WorkspaceSwitch.IsSimulationOn.ShouldBeTrue("the edit never switches the simulation off");
    }

    [Fact]
    public async Task TwoRapidEdits_EndInOneConsistentResult()
    {
        var vm = CreateVm();
        var (coupler, first, second) = PlaceLightPath(vm);
        var third = Place(vm, Straight, 900, 330);
        await vm.Canvas.ConnectPinsAsync(Pin(coupler, "waveguide"), Pin(first, "a0"));
        await TurnSimulationOnAsync(vm);

        vm.CommandManager.ExecuteCommand(new CreateConnectionCommand(vm.Canvas, Pin(first, "b0"), Pin(second, "a0")));
        vm.CommandManager.ExecuteCommand(new CreateConnectionCommand(vm.Canvas, Pin(second, "b0"), Pin(third, "a0")));
        var middle = ConnectionBetween(vm, Pin(first, "b0"));
        var last = ConnectionBetween(vm, Pin(second, "b0"));

        await WaitUntilAsync(vm, () => PowerOf(vm, last) > MinVisiblePower);
        PowerOf(vm, middle).ShouldBeGreaterThan(MinVisiblePower, "the final run includes both edits");
        PowerOf(vm, middle).ShouldBeGreaterThanOrEqualTo(PowerOf(vm, last), "light only loses power downstream");
    }

    [Fact]
    public async Task Undo_WhileSimulationIsOn_ReSimulates()
    {
        var vm = CreateVm();
        var (coupler, first, second) = PlaceLightPath(vm);
        await vm.Canvas.ConnectPinsAsync(Pin(coupler, "waveguide"), Pin(first, "a0"));
        await TurnSimulationOnAsync(vm);
        vm.CommandManager.ExecuteCommand(new CreateConnectionCommand(vm.Canvas, Pin(first, "b0"), Pin(second, "a0")));
        await SettleAsync(vm);
        var runsBefore = vm.LiveSimulation.RunCount;

        await vm.UndoCommand.ExecuteAsync(null);
        await SettleAsync(vm);

        vm.LiveSimulation.RunCount.ShouldBeGreaterThan(runsBefore);
        vm.Canvas.ShowPowerFlow.ShouldBeTrue(vm.StatusText);
    }

    [Fact]
    public async Task FinishedRoutingPass_WhileSimulationIsOn_ReSimulatesOnTheFinalRoutes()
    {
        var vm = CreateVm();
        var (coupler, first, _) = PlaceLightPath(vm);
        await vm.Canvas.ConnectPinsAsync(Pin(coupler, "waveguide"), Pin(first, "a0"));
        await TurnSimulationOnAsync(vm);
        var runsBefore = vm.LiveSimulation.RunCount;

        await vm.Canvas.RecalculateRoutesAsync();
        await SettleAsync(vm);

        vm.LiveSimulation.RunCount.ShouldBe(runsBefore + 1, "a routing pass can change every wire's loss");
    }

    [Fact]
    public async Task RemovingTheOnlyLightSource_TurnsTheSimulationOff_WithTheHint()
    {
        var vm = CreateVm();
        var (coupler, first, second) = PlaceLightPath(vm);
        await vm.Canvas.ConnectPinsAsync(Pin(coupler, "waveguide"), Pin(first, "a0"));
        await vm.Canvas.ConnectPinsAsync(Pin(first, "b0"), Pin(second, "a0"));
        await TurnSimulationOnAsync(vm);

        vm.Canvas.RemoveComponent(vm.Canvas.Components.Single(c => c.Component == coupler));

        await WaitUntilAsync(vm, () => !vm.Canvas.ShowPowerFlow);
        vm.StatusText.ShouldContain("No light sources found");
        vm.WorkspaceSwitch.IsSimulationOn.ShouldBeFalse();
    }

    [Fact]
    public async Task OpeningAnotherExample_KeepsTheSimulationOn_AndReSimulatesIt()
    {
        var vm = CreateVm();
        await OpenExampleAsync(vm, "Mach-Zehnder Interferometer.lun");
        await TurnSimulationOnAsync(vm);

        await OpenExampleAsync(vm, "EBeam Add-Drop Ring.lun");
        var loaded = vm.Canvas.Connections.Select(c => c.Connection).ToList();

        await WaitUntilAsync(vm, () => loaded.Any(c => PowerOf(vm, c) > MinVisiblePower));
        vm.Canvas.ShowPowerFlow.ShouldBeTrue(vm.StatusText);
        vm.WorkspaceSwitch.IsSimulationOn.ShouldBeTrue();
    }

    [Fact]
    public async Task FileNew_TurnsTheSimulationOff()
    {
        var vm = CreateVm();
        var (coupler, first, _) = PlaceLightPath(vm);
        await vm.Canvas.ConnectPinsAsync(Pin(coupler, "waveguide"), Pin(first, "a0"));
        await TurnSimulationOnAsync(vm);
        vm.FileOperations.HasUnsavedChanges = false;

        await vm.NewProjectCommand.ExecuteAsync(null);
        await vm.LiveSimulation.WhenSettledAsync();

        vm.Canvas.ShowPowerFlow.ShouldBeFalse();
        vm.WorkspaceSwitch.IsSimulationOn.ShouldBeFalse();
        vm.LiveSimulation.RunCount.ShouldBe(0, "nothing re-runs on the empty design");
    }

    private static MainViewModel CreateVm()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);
        return vm;
    }

    private static (Component Coupler, Component First, Component Second) PlaceLightPath(MainViewModel vm) =>
        (Place(vm, Coupler, 30, 320), Place(vm, Straight, 300, 330), Place(vm, Straight, 600, 330));

    private static Component Place(MainViewModel vm, string templateName, double x, double y)
    {
        var template = vm.LeftPanel.AllTemplates.First(t => t.PdkSource == DemoPdk && t.Name == templateName);
        var component = ComponentTemplates.CreateFromTemplate(template, x, y);
        vm.Canvas.AddComponent(component, template.Name, template.PdkSource);
        return component;
    }

    private static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.First(p => p.Name == name);

    private static WaveguideConnection ConnectionBetween(MainViewModel vm, PhysicalPin pin) =>
        vm.Canvas.Connections.Select(c => c.Connection).Single(c => c.StartPin == pin || c.EndPin == pin);

    private static double PowerOf(MainViewModel vm, WaveguideConnection connection)
    {
        var flow = vm.Canvas.PowerFlowVisualizer.GetFlowForConnection(connection.Id);
        return flow?.AveragePower ?? 0;
    }

    private static async Task TurnSimulationOnAsync(MainViewModel vm)
    {
        await SettleAsync(vm);
        await vm.TogglePowerOverlayCommand.ExecuteAsync(null);
        vm.Canvas.ShowPowerFlow.ShouldBeTrue(vm.StatusText);
    }

    private static async Task OpenExampleAsync(MainViewModel vm, string fileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), fileName);
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue(vm.StatusText);
        await vm.FileOperations.PostLoadRouting;
    }

    private static async Task SettleAsync(MainViewModel vm)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (vm.Canvas.IsRouting || vm.LiveSimulation.IsWaitingForRouting || vm.IsSimulating)
        {
            clock.Elapsed.ShouldBeLessThan(SettleTimeout, "routing / live simulation never settled");
            await Task.Delay(20);
        }
        await vm.LiveSimulation.WhenSettledAsync();
    }

    private static async Task WaitUntilAsync(MainViewModel vm, Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            clock.Elapsed.ShouldBeLessThan(SettleTimeout, $"live simulation never caught up (status: {vm.StatusText})");
            await Task.Delay(20);
        }
    }
}
