using Avalonia;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// The user journey from the field report: simulation on in Optics, Connect mode, drag a new
/// wire from a lit waveguide to the next one — the new wire must light up once its route is
/// done instead of staying at "no signal". Runs on the real Avalonia dispatcher, so the live
/// re-simulation's debounce and UI-thread continuations are exercised as in the app.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow — CI only locally (the local runners exclude Category=Slow).
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowLiveSimulationConnectTests
{
    private const string DemoPdk = "Demo PDK";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [AvaloniaFact]
    public async Task ConnectDrag_WhileSimulationIsOn_LightsUpTheNewWire()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var coupler = Place(vm, "Grating Coupler", 30, 320);
        var first = Place(vm, "Straight Waveguide 100µm", 300, 330);
        var second = Place(vm, "Straight Waveguide 100µm", 600, 330);
        await vm.Canvas.ConnectPinsAsync(Pin(coupler, "waveguide"), Pin(first, "a0"));
        await vm.TogglePowerOverlayCommand.ExecuteAsync(null);
        vm.Canvas.ShowPowerFlow.ShouldBeTrue(vm.StatusText);

        vm.CanvasInteraction.SetConnectModeCommand.Execute(null);
        UiInput.RunJobs();
        var canvasControl = UiInput.Descendants<DesignCanvas>(win).First();
        UiInput.DragMouse(win,
            CanvasPoint(win, canvasControl, Pin(first, "b0").GetAbsolutePosition()),
            CanvasPoint(win, canvasControl, Pin(second, "a0").GetAbsolutePosition()));

        var added = vm.Canvas.Connections.Select(c => c.Connection)
            .Single(c => c.StartPin == Pin(first, "b0") || c.EndPin == Pin(first, "b0"));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while ((vm.Canvas.PowerFlowVisualizer.GetFlowForConnection(added.Id)?.AveragePower ?? 0) <= 0)
        {
            clock.Elapsed.ShouldBeLessThan(Timeout, $"the new wire never lit up (status: {vm.StatusText})");
            await Task.Delay(20);
            UiInput.RunJobs();
        }

        added.IsPathValid.ShouldBeTrue();
        vm.WorkspaceSwitch.IsSimulationOn.ShouldBeTrue("connecting never switches the simulation off");
    }

    private static Component Place(MainViewModel vm, string templateName, double x, double y)
    {
        var template = vm.LeftPanel.AllTemplates.First(t => t.PdkSource == DemoPdk && t.Name == templateName);
        var component = ComponentTemplates.CreateFromTemplate(template, x, y);
        vm.Canvas.AddComponent(component, template.Name, template.PdkSource);
        UiInput.RunJobs();
        return component;
    }

    private static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.First(p => p.Name == name);

    private static Point CanvasPoint(
        global::Avalonia.Controls.Window win, DesignCanvas canvasControl, (double X, double Y) world)
    {
        var vm = (MainViewModel)win.DataContext!;
        return canvasControl.TranslatePoint(
            new Point(world.X * canvasControl.Zoom + vm.Canvas.PanX,
                      world.Y * canvasControl.Zoom + vm.Canvas.PanY),
            win)!.Value;
    }
}
