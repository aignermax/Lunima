using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas.ViewSettings;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// User story on the shipped MZI: simulate in Optics (L), click over to Layout, and back.
/// Optics shows the light, recedes the geometry, keeps Cut under "More tools" and opens the
/// dock on the optical tabs; Layout hides the light, marks the design checks, puts Cut back
/// in the toolbar and opens Design Checks. The X shortcut reaches Cut in both.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default runs.
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowWorkspaceLayoutOpticsTests
{
    private const string MziExample = "EBeam Mach-Zehnder Interferometer.lun";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [AvaloniaFact]
    public async Task SwitchingLayoutAndOptics_OnTheMzi_ChangesOverlaysToolsAndDockTab()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), MziExample);
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        vm.Home.IsHomeVisible = false;
        UiInput.RunJobs();
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Optics, "the MZI has a light source");
        FocusCanvas(win);

        UiInput.PressKey(win, Key.L);
        await WaitUntil(() => vm.Canvas.ShowPowerFlow, () => vm.StatusText);
        AssertOptics(win, vm);

        UiInput.Click(win, Segment(win, "Layout"));
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
        AssertLayout(win, vm);
        FocusCanvas(win);
        UiInput.PressKey(win, Key.L);
        await WaitUntil(() => vm.Canvas.ViewSettings.HasDesignCheckFindings, () => vm.StatusText);
        UiInput.PressKey(win, Key.X);
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.Cut, "X reaches Cut in Layout");
        UiInput.PressKey(win, Key.Escape);

        UiInput.Click(win, Segment(win, "Optics"));
        AssertOptics(win, vm);
        vm.Canvas.ShowPowerFlow.ShouldBeTrue("Optics restores the light the user left on");
        FocusCanvas(win);
        UiInput.PressKey(win, Key.X);
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.Cut, "X still reaches Cut in Optics");
    }

    private static void AssertOptics(Window win, MainViewModel vm)
    {
        vm.Canvas.ViewSettings.Emphasis.ShouldBe(CanvasEmphasis.Light);
        vm.Canvas.ViewSettings.ShowDesignCheckMarkers.ShouldBeFalse();
        Named<Button>(win, "CutToolButton").IsVisible.ShouldBeFalse("Cut moves under More tools in Optics");
        Named<Button>(win, "MoreToolsButton").IsVisible.ShouldBeTrue();
        Named<Button>(win, "ProbeToolButton").IsVisible.ShouldBeTrue("the probe is the Optics instrument");
        var tab = (AnalysisDockTab)vm.BottomPanel.Analysis.SelectedTabIndex;
        new[] { AnalysisDockTab.Transient, AnalysisDockTab.Sweep, AnalysisDockTab.EyeBer }
            .ShouldContain(tab, "the dock opens on the optical tabs");
    }

    private static void AssertLayout(Window win, MainViewModel vm)
    {
        vm.Canvas.ShowPowerFlow.ShouldBeFalse("Layout hides the light");
        vm.Canvas.ViewSettings.Emphasis.ShouldBe(CanvasEmphasis.Geometry);
        vm.Canvas.ViewSettings.ShowDesignCheckMarkers.ShouldBeTrue();
        Named<Button>(win, "CutToolButton").IsVisible.ShouldBeTrue();
        Named<Button>(win, "MoreToolsButton").IsVisible.ShouldBeFalse("nothing is tucked away in Layout");
        vm.BottomPanel.Analysis.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Checks);
    }

    private static Button Segment(Window win, string label)
    {
        var bar = win.FindControl<Border>("WorkspaceSwitchBar").ShouldNotBeNull();
        return UiInput.Descendants<Button>(bar).First(b => Equals(b.Content, label));
    }

    private static T Named<T>(Window win, string name) where T : Control =>
        win.FindControl<T>(name).ShouldNotBeNull($"{name} must exist");

    private static void FocusCanvas(Window win)
    {
        var canvas = UiInput.Descendants<DesignCanvas>(win).First();
        canvas.Focus();
        UiInput.RunJobs();
    }

    private static async Task WaitUntil(Func<bool> condition, Func<string> context)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            clock.Elapsed.ShouldBeLessThan(Timeout, context());
            await Task.Delay(20);
            UiInput.RunJobs();
        }
    }
}
