using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// The product owner's second round on dies, through the real window: in Layout a press on an
/// empty die area draws a marquee instead of dragging the die, a double-click there does
/// nothing, F frames the current die's parts; dies show as dies in the hierarchy; in System
/// dies are selected, moved and framed. An older single-chip file opens as "Die 1".
/// Screenshots land in <c>docs/pr-media/die-work-surface/</c> when <c>UI_SHOT_DIR</c> is set.
/// </summary>
[Trait("Category", "UiFlows")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowDieWorkspaceRulesTests
{
    private const string DemoPdk = "Demo PDK";
    private const string Mmi = "2x2 MMI Coupler";

    [AvaloniaFact]
    public void Layout_PressOnADie_DrawsAMarquee_SystemMovesTheDie()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var shots = DieFlowShots.Directory();
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.NewDie.Prepare();
        vm.NewDie.SelectedProcess = vm.NewDie.ProcessOptions.First(o => o.Selection.MemberPdkNames.Contains(DemoPdk));
        vm.NewDie.CreateCommand.Execute(null);
        UiInput.RunJobs();
        var die2 = vm.Canvas.Dies.RootDies[1];
        var o2 = DieAssembly.OutlineOf(die2);
        DieFlowShots.Fit(win, vm, vm.Canvas.Dies.RootDies.ToArray());
        var left = Place(win, vm, die2, o2.X + 300, o2.CenterY);
        var right = Place(win, vm, die2, o2.X + 650, o2.CenterY);

        // Press on the empty die area and drag across both MMIs: a marquee, the die stays put.
        vm.CanvasInteraction.CurrentMode = InteractionMode.Select;
        DieFlowShots.Fit(win, vm, die2);
        var from = DieFlowShots.CanvasPoint(win, vm, o2.X + 120, o2.CenterY - 250);
        var to = DieFlowShots.CanvasPoint(win, vm, o2.X + 850, o2.CenterY + 250);
        win.MouseMove(from);
        win.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        win.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        win.MouseMove(to, RawInputModifiers.LeftMouseButton);
        UiInput.RunJobs();
        vm.Canvas.Selection.IsBoxSelecting.ShouldBeTrue("a press on an empty die area starts a marquee");
        DieFlowShots.Capture(win, shots, "07-marquee-on-die.png");
        win.MouseUp(to, MouseButton.Left);
        UiInput.RunJobs();
        DieAssembly.OutlineOf(die2).X.ShouldBe(o2.X, 1e-6, "the die did not move");
        vm.Canvas.Selection.SelectedComponents.Select(c => c.Component).ShouldBe(new[] { left, right }, ignoreOrder: true);

        // Double-click on the empty die area: nothing special outside System.
        var empty = DieFlowShots.CanvasPoint(win, vm, o2.X + 80, o2.Bottom - 80);
        DoubleClick(win, empty);
        vm.Canvas.SelectedComponent.ShouldBeNull();
        vm.StatusText.ShouldNotContain("Zoomed to");

        // F frames the parts of the die of the selection, not the whole carrier.
        vm.Canvas.Selection.SelectSingle(vm.Canvas.Surface.ViewModelFor(left));
        vm.Canvas.SelectedComponent = vm.Canvas.Surface.ViewModelFor(left);
        double carrierZoom = vm.ViewportControl.ZoomLevel;
        UiInput.PressKey(win, Key.F);
        vm.ViewportControl.ZoomLevel.ShouldBeGreaterThan(carrierZoom * 2, "F frames the die's content");

        // Hierarchy: dies are dies.
        vm.LeftRail.ShowHierarchyCommand.Execute(null);
        DieFlowShots.Fit(win, vm, vm.Canvas.Dies.RootDies.ToArray());
        UiInput.RunJobs();
        vm.LeftPanel.HierarchyPanel!.RootNodes.Count(n => n.IsDie).ShouldBe(2);
        DieFlowShots.Capture(win, shots, "08-hierarchy-with-dies.png");

        // System: the die is the unit of work — a press on it drags it.
        vm.WorkspaceSwitch.Current = Workspace.System;
        var grab = DieFlowShots.CanvasPoint(win, vm, o2.X + 80, o2.Bottom - 80);
        UiInput.DragMouse(win, grab, grab + new Point(0, 120));
        DieAssembly.OutlineOf(die2).Y.ShouldBeGreaterThan(o2.Y + 50, "in System the die moves");
    }

    [AvaloniaFact]
    public async Task LegacySingleChipFile_OpensAsDie1()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), "Mach-Zehnder Interferometer.lun");
        (await vm.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue();
        UiInput.RunJobs();
        vm.WorkspaceSwitch.Current = Workspace.System;
        vm.Canvas.Dies.IsAssemblyRoot.ShouldBeFalse("the file is not changed into an assembly by opening it");
        var chip = vm.Canvas.ChipDie.Outline;
        ShowcaseFit(win, vm, chip);

        var point = DieFlowShots.CanvasPoint(win, vm, chip.X + 20, chip.Y + 20);
        UiInput.ClickAt(win, point);
        vm.Canvas.ChipDie.IsSelected.ShouldBeTrue("a click on the chip selects Die 1 in System");
        vm.DieProperties.Name.ShouldBe("Die 1");
        DieFlowShots.Capture(win, DieFlowShots.Directory(), "09-legacy-file-as-die-1.png");
    }

    private static void ShowcaseFit(MainWindow win, MainViewModel vm, DieOutline outline) =>
        UnitTests.UI.Showcase.ShowcaseCircuit.SetView(win, vm,
            (outline.X - 250, outline.Y - 250, outline.Width + 500, outline.Height + 500));

    private static Component Place(MainWindow win, MainViewModel vm, ComponentGroup die, double x, double y)
    {
        vm.CanvasInteraction.SelectedTemplate = vm.LeftPanel.AllTemplates.Single(t => t.PdkSource == DemoPdk && t.Name == Mmi);
        int before = die.ChildComponents.Count;
        UiInput.ClickAt(win, DieFlowShots.CanvasPoint(win, vm, x, y));
        UiInput.PressKey(win, Key.Escape);
        die.ChildComponents.Count.ShouldBe(before + 1, $"the MMI landed on {die.GroupName} ({vm.StatusText})");
        return die.ChildComponents[^1];
    }

    private static void DoubleClick(MainWindow win, Point point)
    {
        win.MouseMove(point);
        win.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        win.MouseUp(point, MouseButton.Left);
        win.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        win.MouseUp(point, MouseButton.Left);
        UiInput.RunJobs();
    }
}
