using System.Collections.Specialized;
using System.Diagnostics;
using Xunit.Abstractions;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Workspaces;

[Collection("LocalizationSingleton")]
public class RepeatedExampleLoadTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    [Trait("Category", "Slow")]
    public async Task ReplacingLargeAdder_RebuildsHierarchyOnlyAtBoundaries_AndSwitchesKeepIt()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var window = UnitTests.UI.Showcase.ShowcaseCircuit.BootMainWindow(vm);
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate 4-Bit Adder.lun");
        int resets = 0;
        vm.LeftPanel.HierarchyPanel.RootNodes.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        try
        {
            for (int iteration = 0; iteration < 5; iteration++)
            {
                vm.FileOperations.MessageBoxService = null;
                resets = 0;
                var elapsed = Stopwatch.StartNew();
                (await vm.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue();
                await vm.FileOperations.PostLoadRouting;
                await vm.Canvas.Dies.PendingGridRebuild;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                // One reset clears the old design, the final rebuild refreshes load metadata.
                resets.ShouldBeLessThanOrEqualTo(2,
                    "loading each of hundreds of parts must not rebuild every previous subtree");
                output.WriteLine($"Load {iteration + 1}: {elapsed.Elapsed.TotalSeconds:F3}s; hierarchy resets={resets}");
                var roots = vm.LeftPanel.HierarchyPanel.RootNodes.ToArray();
                roots.Length.ShouldBe(vm.Canvas.Components.Count);
                roots.Length.ShouldBeGreaterThan(500, "exercise the shipped large adder");
                resets = 0;
                elapsed.Restart();
                for (int i = 0; i < 3; i++)
                {
                    vm.WorkspaceSwitch.Current = Workspace.System;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    vm.WorkspaceSwitch.Current = Workspace.Logic;
                    await vm.GateView.PendingRebuild;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }
                output.WriteLine($"Three System/Logic round trips: {elapsed.Elapsed.TotalSeconds:F3}s; hierarchy resets={resets}");
                resets.ShouldBe(0);
                vm.LeftPanel.HierarchyPanel.RootNodes.ShouldBe(roots);
            }
        }
        finally
        {
            vm.FileOperations.HasUnsavedChanges = false;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
