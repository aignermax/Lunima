using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// User story: switch workspaces with Ctrl+1..4 and with one click on the toolbar's
/// segmented switch; the Run button's label and the dock's tabs follow, and the
/// CW/Transient choice is only on screen in Optics.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default runs.
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowWorkspaceSwitchTests
{
    [AvaloniaFact]
    public void CtrlDigit_SwitchesWorkspace()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;

        UiInput.PressKey(host.Window, Key.D2, RawInputModifiers.Control);
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Optics);
        UiInput.PressKey(host.Window, Key.D3, RawInputModifiers.Control);
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Logic);
        UiInput.PressKey(host.Window, Key.D4, RawInputModifiers.Control);
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.System);
        UiInput.PressKey(host.Window, Key.D1, RawInputModifiers.Control);
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
    }

    [AvaloniaFact]
    public void ClickingASegment_SwitchesWorkspace_RunLabelAndTabsFollow()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var bar = win.FindControl<Border>("WorkspaceSwitchBar").ShouldNotBeNull();
        var logicSegment = UiInput.Descendants<Button>(bar).First(b => Equals(b.Content, "Logic"));

        UiInput.Click(win, logicSegment);

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Logic);
        var run = win.FindControl<Button>("RunSimulationButton").ShouldNotBeNull();
        UiInput.Descendants<TextBlock>(run).Any(t => t.Text == "Build logic").ShouldBeTrue();
        vm.BottomPanel.Analysis.Tabs.IsShown(AnalysisDockTab.Logic).ShouldBeTrue();
        vm.BottomPanel.Analysis.Tabs.IsShown(AnalysisDockTab.Spectrum).ShouldBeFalse();
    }
}
