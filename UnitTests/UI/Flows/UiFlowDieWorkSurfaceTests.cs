using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// The product owner's die scenario through the real window, Layout workspace, no die ever
/// entered: New die next to the chip → place two 2×2 MMIs on Die 2 → wire them → drag Die 2
/// (the route moves rigidly, nothing re-routes, no red outline) → drag a wire from a non-coupler
/// pin on Die 2 to Die 1 (refused with the hint) → System dims and locks the die content →
/// double-click frames a die. Screenshots land in <c>docs/pr-media/die-work-surface/</c>.
/// </summary>
[Trait("Category", "UiFlows")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowDieWorkSurfaceTests
{
    private const string DemoPdk = "Demo PDK";
    private const string Mmi = "2x2 MMI Coupler";

    [AvaloniaFact]
    public async Task PlaceWireMoveRefuse_OnDies_WithoutEnteringThem()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var shots = DieFlowShots.Directory();
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        UiInput.RunJobs();

        vm.NewDie.Prepare();
        vm.NewDie.SelectedProcess = vm.NewDie.ProcessOptions.First(o => o.Selection.MemberPdkNames.Contains(DemoPdk));
        vm.NewDie.CreateCommand.Execute(null);
        UiInput.RunJobs();
        var die1 = vm.Canvas.Dies.RootDies[0];
        var die2 = vm.Canvas.Dies.RootDies[1];
        DieFlowShots.Fit(win, vm, die1, die2);

        // Place two MMIs on Die 2 and one on Die 1 — straight from the assembly, no Open die.
        var o2 = DieAssembly.OutlineOf(die2);
        var mmiLeft = Place(win, vm, die2, o2.X + 250, o2.CenterY);
        var mmiRight = Place(win, vm, die2, o2.X + 650, o2.CenterY + 120);
        var o1 = DieAssembly.OutlineOf(die1);
        var onDie1 = Place(win, vm, die1, o1.Right - 400, o1.CenterY);
        vm.Canvas.IsInGroupEditMode.ShouldBeFalse();
        vm.Canvas.CanPlaceComponent(mmiLeft.PhysicalX, mmiLeft.PhysicalY + 200, mmiLeft.WidthMicrometers, mmiLeft.HeightMicrometers)
            .ShouldBeTrue("a free spot on the die previews valid — no red outline");

        // Wire MMI out1 → MMI in1 on Die 2 by dragging in Connect mode.
        vm.CanvasInteraction.CurrentMode = InteractionMode.Connect;
        DragWire(win, vm, Pin(mmiLeft, "out1"), Pin(mmiRight, "in1"));
        await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
        UiInput.RunJobs();
        var wire = vm.Canvas.Connections.ShouldHaveSingleItem().Connection;
        ConnectionOwnership.OwnerOf(wire).ShouldBe(die2, "the wire belongs to Die 2");
        wire.RoutedPath.ShouldNotBeNull();
        var routeBefore = Segments(wire);
        DieFlowShots.Fit(win, vm, die2);
        DieFlowShots.Capture(win, shots, "01-parts-on-die-at-root.png");
        DieFlowShots.Fit(win, vm, die1, die2);

        // Drag Die 2 by an empty spot: everything on it moves rigidly, nothing re-routes.
        vm.CanvasInteraction.CurrentMode = InteractionMode.Select;
        var grab = new Point(o2.X + 80, o2.Bottom - 80);
        UiInput.DragMouse(win, DieFlowShots.CanvasPoint(win, vm, grab.X, grab.Y), DieFlowShots.CanvasPoint(win, vm, grab.X + 300, grab.Y + 200));
        await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
        double dx = DieAssembly.OutlineOf(die2).X - o2.X, dy = DieAssembly.OutlineOf(die2).Y - o2.Y;
        (Math.Abs(dx) + Math.Abs(dy)).ShouldBeGreaterThan(50, "the die was dragged");
        var routeAfter = Segments(wire);
        routeAfter.Count.ShouldBe(routeBefore.Count, "the route on the moved die is identical, only translated");
        for (int i = 0; i < routeBefore.Count; i++)
        {
            routeAfter[i].Item1.ShouldBe(routeBefore[i].Item1 + dx, 1e-6);
            routeAfter[i].Item2.ShouldBe(routeBefore[i].Item2 + dy, 1e-6);
            routeAfter[i].Item3.ShouldBe(routeBefore[i].Item3 + dx, 1e-6);
            routeAfter[i].Item4.ShouldBe(routeBefore[i].Item4 + dy, 1e-6);
        }
        new DieAssemblyChecker().Check(vm.Canvas.Components.Select(c => c.Component), vm.Canvas.Connections.Select(c => c.Connection))
            .ShouldNotContain(i => i.Type == DesignIssueType.ComponentOffDie, "parts on the die are on their die");

        // A wire from an MMI pin on Die 2 to the part on Die 1 is refused with the hint.
        vm.CanvasInteraction.CurrentMode = InteractionMode.Connect;
        DragWire(win, vm, Pin(mmiRight, "out2"), Pin(onDie1, "in2"));
        vm.Canvas.Connections.Count.ShouldBe(1, "no waveguide crosses a die edge");
        vm.StatusText.ShouldContain("edge couplers");
        var moved = DieAssembly.OutlineOf(die2);
        double top = Math.Min(moved.Y, onDie1.PhysicalY) - 200, bottom = Math.Max(moved.Bottom, onDie1.PhysicalY) + 200;
        ShowcaseCircuit.SetView(win, vm, (onDie1.PhysicalX - 300, top, moved.Right - onDie1.PhysicalX + 600, bottom - top));
        DieFlowShots.Capture(win, shots, "02-cross-die-refused.png");

        vm.WorkspaceSwitch.Current = Workspace.System;
        UiInput.RunJobs();
        vm.Canvas.Surface.IsContentLocked.ShouldBeTrue();
        DieFlowShots.Capture(win, shots, "03-system-dies-locked.png");

        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.CanvasInteraction.CurrentMode = InteractionMode.Select;
        var empty = DieAssembly.OutlineOf(die2);
        var point = DieFlowShots.CanvasPoint(win, vm, empty.X + 80, empty.Bottom - 80);
        UiInput.ClickAt(win, point);
        DoubleClick(win, point);
        vm.Canvas.IsInGroupEditMode.ShouldBeFalse("double-click frames a die, it never enters it");
        vm.StatusText.ShouldContain("Zoomed to Die 2", Case.Insensitive, "double-click zooms onto the die");
        vm.Canvas.SelectedComponent!.Component.ShouldBe(die2);
        DieFlowShots.Capture(win, shots, "04-zoom-to-die.png");
    }

    [AvaloniaFact]
    public async Task WireBetweenK1AndK2_DrawnOutsideM_IsStoredInM_AndShownWhenEnteringM()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var shots = DieFlowShots.Directory();
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        ShowcaseCircuit.SetView(win, vm, (300, 300, 1600, 1000));
        var a = PlaceOnChip(win, vm, 600, 500);
        var b = PlaceOnChip(win, vm, 600, 800);
        var c = PlaceOnChip(win, vm, 1300, 500);
        var d = PlaceOnChip(win, vm, 1300, 800);
        var k1 = Group(vm, a, b);
        var k2 = Group(vm, c, d);
        var m = Group(vm, k1, k2);

        vm.CanvasInteraction.CurrentMode = InteractionMode.Connect;
        DragWire(win, vm, Pin(a, "out1"), Pin(c, "in1"));
        for (int i = 0; i < 100 && m.InternalPaths.Count == 0; i++)
        {
            await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
            await Task.Delay(20);
            UiInput.RunJobs();
        }

        m.InternalPaths.ShouldHaveSingleItem($"a wire between K1 and K2 belongs to M, wherever it is drawn (status: {vm.StatusText}; live wires: {vm.Canvas.Connections.Count}; owner: {vm.Canvas.Connections.Select(w => CAP_Core.Components.Connections.Ownership.ConnectionOwnership.OwnerOf(w.Connection)?.GroupName).FirstOrDefault()}; M parent: {m.ParentGroup?.GroupName})");
        vm.Canvas.Connections.ShouldBeEmpty();
        DieFlowShots.Capture(win, shots, "05-k1-k2-wire-stored-in-m.png");

        vm.CanvasInteraction.CurrentMode = InteractionMode.Select;
        var inside = DieFlowShots.CanvasPoint(win, vm, b.PhysicalX + b.WidthMicrometers / 2, b.PhysicalY + b.HeightMicrometers / 2);
        DoubleClick(win, inside);
        vm.Canvas.CurrentEditGroup.ShouldBe(m, "double-click enters M");
        vm.Canvas.Connections.ShouldHaveSingleItem("the K1-K2 wire is visible and editable inside M");
        DieFlowShots.Capture(win, shots, "06-inside-m.png");
    }

    private static Component PlaceOnChip(MainWindow win, MainViewModel vm, double x, double y)
    {
        vm.CanvasInteraction.SelectedTemplate = vm.LeftPanel.AllTemplates.Single(t => t.PdkSource == DemoPdk && t.Name == Mmi);
        int before = vm.Canvas.Components.Count;
        UiInput.ClickAt(win, DieFlowShots.CanvasPoint(win, vm, x, y));
        UiInput.PressKey(win, Key.Escape);
        vm.Canvas.Components.Count.ShouldBe(before + 1, $"the MMI was placed ({vm.StatusText})");
        return vm.Canvas.Components[^1].Component;
    }

    private static ComponentGroup Group(MainViewModel vm, params Component[] members)
    {
        vm.Canvas.Selection.ClearSelection();
        foreach (var member in members)
            vm.Canvas.Selection.AddToSelection(vm.Canvas.Components.Single(c => ReferenceEquals(c.Component, member)));
        vm.CanvasInteraction.CreateGroupCommand.Execute(null);
        UiInput.RunJobs();
        return (ComponentGroup)members[0].ParentGroup!;
    }

    private static Component Place(MainWindow win, MainViewModel vm, ComponentGroup die, double x, double y)
    {
        vm.CanvasInteraction.SelectedTemplate = vm.LeftPanel.AllTemplates.Single(t => t.PdkSource == DemoPdk && t.Name == Mmi);
        int before = die.ChildComponents.Count;
        UiInput.ClickAt(win, DieFlowShots.CanvasPoint(win, vm, x, y));
        UiInput.PressKey(win, Key.Escape);
        die.ChildComponents.Count.ShouldBe(before + 1, $"the MMI landed on {die.GroupName} ({vm.StatusText})");
        return die.ChildComponents[^1];
    }

    /// <summary>Two presses well inside the double-click window (no UI work in between).</summary>
    private static void DoubleClick(MainWindow win, Point point)
    {
        win.MouseMove(point);
        win.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        win.MouseUp(point, MouseButton.Left);
        win.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        win.MouseUp(point, MouseButton.Left);
        UiInput.RunJobs();
    }

    private static PhysicalPin Pin(Component part, string name) => part.PhysicalPins.Single(p => p.Name == name);

    private static void DragWire(MainWindow win, MainViewModel vm, PhysicalPin from, PhysicalPin to)
    {
        var (fx, fy) = from.GetAbsolutePosition();
        var (tx, ty) = to.GetAbsolutePosition();
        UiInput.DragMouse(win, DieFlowShots.CanvasPoint(win, vm, fx, fy), DieFlowShots.CanvasPoint(win, vm, tx, ty));
    }

    private static List<(double, double, double, double)> Segments(CAP_Core.Components.Connections.WaveguideConnection wire) =>
        wire.RoutedPath!.Segments.Select(s => (s.StartPoint.X, s.StartPoint.Y, s.EndPoint.X, s.EndPoint.Y)).ToList();
}
