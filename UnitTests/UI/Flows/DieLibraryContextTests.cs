using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.Dies;
using Xunit;

namespace UnitTests.UI.Flows;

[Trait("Category", "UiFlows")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class DieLibraryContextTests
{
    [AvaloniaFact]
    public void ClickingEmptyDie_SwitchesLibrary_WithoutSelectingOrMovingTheDie()
    {
        using var host = CreateAssembly();
        var vm = host.Vm;
        var dies = vm.Canvas.Dies.RootDies;
        var first = dies[0];
        var second = dies[1];
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.CanvasInteraction.SetSelectModeCommand.Execute(null);
        DieFlowShots.Fit(host.Window, vm, first, second);
        ClickEmpty(host, first);
        vm.ScopeDie.ShouldBe(first);
        vm.LeftPanel.FilteredTemplates.ShouldContain(t => t.PdkSource == TwoDieExampleBuilder.DemoPdk);
        vm.LeftPanel.FilteredTemplates.ShouldNotContain(t => t.PdkSource == TwoDieExampleBuilder.SinPdk);

        var outline = DieAssembly.OutlineOf(second);
        host.Window.MouseMove(DieFlowShots.CanvasPoint(host.Window, vm, outline.CenterX, outline.CenterY));
        UiInput.RunJobs();
        vm.ScopeDie.ShouldBe(first, "hover must not change the active work surface");
        ClickEmpty(host, second);
        vm.ScopeDie.ShouldBe(second);
        vm.Canvas.SelectedComponent.ShouldBeNull("the die remains an inert background in Layout");
        DieAssembly.OutlineOf(second).ShouldBe(outline);
        vm.LeftPanel.FilteredTemplates.ShouldContain(t => t.PdkSource == TwoDieExampleBuilder.SinPdk);
        vm.LeftPanel.FilteredTemplates.ShouldNotContain(t => t.PdkSource == TwoDieExampleBuilder.DemoPdk);
    }

    [AvaloniaFact]
    public void ArmingAndPlacingComponent_PreservesSelectedDieLibrary()
    {
        using var host = CreateAssembly();
        var vm = host.Vm;
        var die = vm.Canvas.Dies.RootDies[1];
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.ZoomToDie(die);
        UiInput.RunJobs();
        var template = vm.LeftPanel.FilteredTemplates.First(t => t.PdkSource == TwoDieExampleBuilder.SinPdk);
        host.LibraryListBox.SelectedItem = template;
        UiInput.RunJobs();

        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.PlaceComponent);
        vm.ScopeDie.ShouldBe(die, "arming a template clears selection but must retain the process context");
        vm.CanvasInteraction.SelectedTemplate.ShouldBe(template);
        vm.LeftPanel.FilteredTemplates.ShouldNotContain(t => t.PdkSource == TwoDieExampleBuilder.DemoPdk);
        var outline = DieAssembly.OutlineOf(die);
        vm.CanvasInteraction.CanvasClicked(outline.CenterX, outline.CenterY);
        die.ChildComponents.Count.ShouldBe(1);
        vm.ScopeDie.ShouldBe(die);
        vm.CanvasInteraction.SelectedTemplate.ShouldBe(template, "the same component can be placed again");
    }

    private static UiFlowTestHost CreateAssembly()
    {
        var host = new UiFlowTestHost();
        var vm = host.Vm;
        vm.NewDie.Prepare();
        var demo = vm.NewDie.ProcessOptions.First(o => o.Selection.MemberPdkNames.Contains(TwoDieExampleBuilder.DemoPdk));
        vm.FileOperations.SetActiveProcess(demo.Selection, markDirty: false);
        vm.NewDie.SelectedProcess = vm.NewDie.ProcessOptions.First(o => o.Selection.MemberPdkNames.Contains(TwoDieExampleBuilder.SinPdk));
        vm.NewDie.CreateCommand.Execute(null);
        UiInput.RunJobs();
        return host;
    }

    private static void ClickEmpty(UiFlowTestHost host, CAP_Core.Components.Core.ComponentGroup die)
    {
        var outline = DieAssembly.OutlineOf(die);
        UiInput.ClickAt(host.Window, DieFlowShots.CanvasPoint(host.Window, host.Vm, outline.CenterX, outline.CenterY));
        UiInput.RunJobs();
    }
}
