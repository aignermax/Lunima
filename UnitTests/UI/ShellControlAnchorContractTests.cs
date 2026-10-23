using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Shell;
using CAP_Core.Components.Creation;
using Shouldly;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>Checks the real shell controls across the toolbar/library namescope boundaries.</summary>
[Collection("LocalizationSingleton")]
public class ShellControlAnchorContractTests
{
    [AvaloniaFact]
    public async Task TourSpotlight_ResolvesToolbarAndLibraryTargets_AndPopupKeepsItsButton()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            vm.LeftRail.ShowLibraryCommand.Execute(null);
            var root = (global::Avalonia.Controls.Grid)window.Content!;
            var overlay = new TourAnchorOverlay { Card = new TextBlock { Text = "Anchor contract" } };
            root.Children.Add(overlay);
            Dispatcher.UIThread.RunJobs();
            var overlayCanvas = overlay.FindControl<global::Avalonia.Controls.Canvas>("RootCanvas").ShouldNotBeNull();
            var spotlight = overlay.FindControl<Border>("Spotlight").ShouldNotBeNull();

            foreach (var name in new[] { "RunSimulationButton", "ConnectModeButton", "ComponentLibraryList" })
            {
                var target = Named<Control>(window, name);
                target.IsEffectivelyVisible.ShouldBeTrue(name);
                target.Bounds.Width.ShouldBeGreaterThan(0, name);
                overlay.TargetName = name;
                await WaitForAnchorLayoutAsync(window, target, overlayCanvas, spotlight);

                var origin = target.TranslatePoint(default, overlayCanvas).ShouldNotBeNull();
                spotlight.IsVisible.ShouldBeTrue(name);
                spotlight.Width.ShouldBe(target.Bounds.Width + 12, 0.01, name);
                spotlight.Height.ShouldBe(target.Bounds.Height + 12, 0.01, name);
                global::Avalonia.Controls.Canvas.GetLeft(spotlight).ShouldBe(origin.X - 6, 0.01, name);
                global::Avalonia.Controls.Canvas.GetTop(spotlight).ShouldBe(origin.Y - 6, 0.01, name);
            }

            var toolbar = Named<MainToolbarView>(window, "Toolbar");
            toolbar.FindControl<Popup>("RunShortcutHintPopup").ShouldNotBeNull()
                .PlacementTarget.ShouldBeSameAs(Named<Button>(window, "RunSimulationButton"));
            overlay.TargetName = "NoSuchShellControl";
            Dispatcher.UIThread.RunJobs();
            spotlight.IsVisible.ShouldBeFalse();
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void LibrarySelection_FollowsTheModel_AndStopsObservingAfterDetach()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var library = vm.LeftPanel.ComponentLibrary;
        var first = new GroupTemplateItemViewModel(new GroupTemplate { Name = "First" }, library);
        var second = new GroupTemplateItemViewModel(new GroupTemplate { Name = "Second" }, library);
        library.UserGroups.Add(first);
        library.UserGroups.Add(second);
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var pane = Named<WorkspaceLibraryPane>(window, "LibraryPane");
            var list = pane.FindControl<ListBox>("UserGroupsListBox").ShouldNotBeNull();
            vm.LeftPanel.SelectedGroupTemplate = first.Template;
            list.SelectedItem.ShouldBeSameAs(first);
            window.ClearAllGroupSelections();
            list.SelectedItem.ShouldBeNull();
            vm.LeftPanel.SelectedGroupTemplate.ShouldBeNull();

            vm.LeftPanel.SelectedGroupTemplate = first.Template;
            var border = window.FindControl<Border>("LeftPanelBorder").ShouldNotBeNull();
            pane.DataContext = vm; // Keep its binding source while testing visual-tree detachment.
            border.Child = null;
            Dispatcher.UIThread.RunJobs();
            vm.LeftPanel.SelectedGroupTemplate = second.Template;
            list.SelectedItem.ShouldBeSameAs(first, "a detached pane releases its model subscription");

            border.Child = pane;
            Dispatcher.UIThread.RunJobs();
            list.SelectedItem.ShouldBeSameAs(second, "reattachment synchronizes with the current model");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task WaitForAnchorLayoutAsync(MainWindow window, Control target,
        global::Avalonia.Controls.Canvas overlayCanvas, Border spotlight)
    {
        // Revealing a virtualized library target can realize more rows and change its
        // height after TargetName's immediate refresh. The production overlay observes
        // that settled geometry on its 150 ms poll; RunJobs alone does not advance time.
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(3))
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var origin = target.TranslatePoint(default, overlayCanvas);
            if (origin is { } point && spotlight.IsVisible &&
                Math.Abs(spotlight.Width - target.Bounds.Width - 12) < 0.01 &&
                Math.Abs(spotlight.Height - target.Bounds.Height - 12) < 0.01 &&
                Math.Abs(global::Avalonia.Controls.Canvas.GetLeft(spotlight) - point.X + 6) < 0.01 &&
                Math.Abs(global::Avalonia.Controls.Canvas.GetTop(spotlight) - point.Y + 6) < 0.01)
                return;
            await Task.Delay(20);
        }
        // The original exact geometry assertions below report any unresolved mismatch.
    }

    private static T Named<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
