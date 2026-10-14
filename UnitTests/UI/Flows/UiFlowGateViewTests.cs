using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

        var point = SymbolCenterOnScreen(host, "NAND4");
        UiInput.ClickAt(host.Window, point);
        UiInput.ClickAt(host.Window, point);

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
