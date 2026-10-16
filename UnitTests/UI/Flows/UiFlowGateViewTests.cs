using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// User story: open the Half Adder, land in Logic on the gate view, click the NOT gate's
/// symbol, switch to Layout — the same gate group is selected there. A double-click on a
/// gate jumps to Layout directly, and V / the toolbar switch flip gate ↔ physical view.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default runs.
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowGateViewTests
{
    [AvaloniaFact]
    public async Task ClickGateInGateView_LayoutShowsTheSameSelection()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        await OpenHalfAdderInGateView(host);

        var gatesButton = win.FindControl<Button>("GateViewGatesButton").ShouldNotBeNull();
        gatesButton.IsEffectivelyVisible.ShouldBeTrue("the toggle is on the toolbar in Logic");
        gatesButton.Classes.ShouldContain("active");

        UiInput.ClickAt(win, SymbolCenterOnScreen(host, "NOT1"));
        var not1 = vm.Canvas.Components.Single(c => c.Component is ComponentGroup { GroupName: "NOT1" });
        vm.Canvas.Selection.SelectedComponents.ShouldBe([not1]);

        UiInput.PressKey(win, Key.D1, RawInputModifiers.Control);

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
        vm.GateView.IsShown.ShouldBeFalse("Layout draws the physical design");
        vm.Canvas.Selection.SelectedComponents.ShouldBe([not1], "Layout shows the gate selected in the gate view");
        gatesButton.IsEffectivelyVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task DoubleClickOnAGate_JumpsToLayout()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        await OpenHalfAdderInGateView(host);

        // Headless input normally timestamps events after rendering. Model a real
        // double-click's event times independently of a slow CI render/selection.
        var platform = host.Window.PlatformImpl!;
        var inputProperty = typeof(Avalonia.Platform.ITopLevelImpl).GetProperty("Input", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
        var input = (Action<RawInputEventArgs>)inputProperty.GetValue(platform)!;
        var timestampProperty = typeof(RawInputEventArgs).GetProperty("Timestamp")!;
        var typeProperty = typeof(RawPointerEventArgs).GetProperty("Type")!;
        ulong timestamp = 0;
        var presses = new List<ulong>();
        Action<RawInputEventArgs> timedInput = e =>
        {
            if (timestamp == 0) timestamp = (ulong)timestampProperty.GetValue(e)!;
            timestampProperty.SetValue(e, timestamp += 10);
            if (e is RawPointerEventArgs && (RawPointerEventType)typeProperty.GetValue(e)! == RawPointerEventType.LeftButtonDown) presses.Add(timestamp);
            input(e);
        };
        inputProperty.SetValue(platform, timedInput);
        try
        {
            UiInput.ClickAt(host.Window, SymbolCenterOnScreen(host, "NAND4"));
            vm.Canvas.SelectedComponent!.Component.ShouldBeOfType<ComponentGroup>().GroupName.ShouldBe("NAND4");
            await Task.Delay(CanvasInteractionState.DoubleClickMilliseconds + 50);
            // Selection can resize the inspector; resolve the displayed symbol again.
            UiInput.ClickAt(host.Window, SymbolCenterOnScreen(host, "NAND4"));
            presses.Count.ShouldBe(2);
            (presses[1] - presses[0]).ShouldBeLessThan((ulong)CanvasInteractionState.DoubleClickMilliseconds);
        }
        finally { inputProperty.SetValue(platform, input); }

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
        vm.Canvas.SelectedComponent!.Component.ShouldBeOfType<ComponentGroup>().GroupName.ShouldBe("NAND4");
        vm.Canvas.IsInGroupEditMode.ShouldBeFalse("the gate view's double-click opens the layout, not the group internals");
    }

    [AvaloniaFact]
    public async Task VKeyAndToolbar_FlipBetweenGateAndPhysicalView()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        await OpenHalfAdderInGateView(host);

        UiInput.PressKey(win, Key.V);
        vm.GateView.IsPhysicalShown.ShouldBeTrue();

        UiInput.Click(win, win.FindControl<Button>("GateViewGatesButton").ShouldNotBeNull());
        vm.GateView.IsShown.ShouldBeTrue();
    }

    private static async Task OpenHalfAdderInGateView(UiFlowTestHost host)
    {
        var vm = host.Vm;
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Half Adder.lun");
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        UiInput.RunJobs();
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Logic, "a gate example opens in Logic");
        vm.GateView.IsShown.ShouldBeTrue("Logic opens on the gate view");
        await vm.GateView.RebuildNowAsync();
        UiInput.PressKey(host.Window, Key.F);
        UiInput.RunJobs();
    }

    private static Point SymbolCenterOnScreen(UiFlowTestHost host, string gateId)
    {
        var node = host.Vm.GateView.Graph.NodesById[gateId];
        var center = GateSymbolGeometry.Place(node, ComponentGroupRenderer.CalculateGroupBounds(node.Group)).Body.Center;
        var canvas = UiInput.Descendants<DesignCanvas>(host.Window).First();
        var local = new Point(center.X * canvas.Zoom + host.Vm.Canvas.PanX, center.Y * canvas.Zoom + host.Vm.Canvas.PanY);
        return canvas.TranslatePoint(local, host.Window)!.Value;
    }
}
