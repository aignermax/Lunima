using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services;
using CAP.Avalonia.Views;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The real MainWindow across the four workspaces: the accent line and Run take the
/// workspace hue, the Probe button shows only in Layout and Optics, and the Gates and
/// Chiplets rail panes render their rows (the item templates bind back to the window's view
/// model, so a broken binding throws here and not only in the slow screenshot runs).
/// </summary>
[Collection("LocalizationSingleton")]
public class WorkspaceShellRenderTests
{
    [AvaloniaFact]
    public void Shell_FollowsTheWorkspace_HueProbeAndRailPanes()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Home.Examples.Add(new ExampleDesign("Logic Gate NOT-NAND", Path.Combine("x", "Logic Gate NOT-NAND.lun")));
        var chiplet = LogicGateFixtureFactory.CreateCombinerGroup();
        chiplet.GroupName = "Die A";
        vm.Canvas.AddComponent(chiplet);
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            foreach (var workspace in Enum.GetValues<Workspace>())
            {
                vm.WorkspaceSwitch.Current = workspace;
                Dispatcher.UIThread.RunJobs();

                AccentOf(window).ShouldBe(TokenColor(window, workspace), $"{workspace}: accent line hue");
                Named<Button>(window, "ProbeToolButton").IsVisible
                    .ShouldBe(workspace is Workspace.Layout or Workspace.Optics, $"{workspace}: probe visibility");
            }

            vm.WorkspaceSwitch.Current = Workspace.Logic;
            Dispatcher.UIThread.RunJobs();
            Named<Button>(window, "GatesMakeGateButton").IsEffectivelyVisible.ShouldBeTrue("Logic opens on Gates");
            window.GetVisualDescendants().OfType<TextBlock>()
                .ShouldContain(t => t.Text == "Logic Gate NOT-NAND", "the starter gate example row renders");

            vm.WorkspaceSwitch.Current = Workspace.System;
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<TextBlock>()
                .ShouldContain(t => t.Text == "Die A" && t.IsEffectivelyVisible, "System opens on Chiplets");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static T Named<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static Color AccentOf(MainWindow window) =>
        ((ISolidColorBrush)Named<Border>(window, "WorkspaceAccentLine").Background!).Color;

    private static Color TokenColor(MainWindow window, Workspace workspace)
    {
        window.TryFindResource($"Brush.Workspace.{workspace}.Accent", window.ActualThemeVariant, out var brush).ShouldBeTrue();
        return ((ISolidColorBrush)brush!).Color;
    }
}
