using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.DieSurface;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;

namespace UnitTests.Dies.DieWorkspaceRules;

/// <summary>
/// Zoom to fit (F): in Layout / Optics / Logic it frames the content of the current die (the
/// die of the selection, else under the cursor, else the die worked on last, else all content)
/// — never the empty carrier; in System it frames every die.
/// </summary>
[Collection("LocalizationSingleton")]
public class DieZoomToFitTests
{
    private const double ViewportPixels = 1000;

    [Fact]
    public void Layout_WithAPartSelected_FramesThatDiesContent()
    {
        var f = new DieWorkspaceFixture();
        f.Select(f.PartVm(f.A));

        var box = ZoomToFitTarget.Resolve(f.Canvas, fitsDies: false, cursor: null)!.Value;

        box.MinX.ShouldBe(f.A.PhysicalX, 1e-6);
        box.MaxX.ShouldBe(f.B.PhysicalX + f.B.WidthMicrometers, 1e-6);
        box.Width.ShouldBeLessThan(DieWorkspaceFixture.ChipSize / 2, "the parts, not the whole 2 mm die");
    }

    [Fact]
    public void Layout_CursorOverAnEmptyDie_FramesThatDie()
    {
        var f = new DieWorkspaceFixture();
        var o = DieWorkspaceFixture.Outline(f.Die2);

        var box = ZoomToFitTarget.Resolve(f.Canvas, fitsDies: false, cursor: (o.CenterX, o.CenterY))!.Value;

        box.MinX.ShouldBe(o.X, 1e-6);
        box.MaxX.ShouldBe(o.Right, 1e-6);
    }

    [Fact]
    public void Layout_NoSelectionNoCursor_FramesTheDieWorkedOnLast_ElseAllContent()
    {
        var f = new DieWorkspaceFixture();
        f.Canvas.Selection.ClearSelection();
        f.Canvas.SelectedComponent = null;
        f.Canvas.Surface.LastWorkedDie = null;
        var all = ZoomToFitTarget.Resolve(f.Canvas, false, null)!.Value;
        all.MaxX.ShouldBeLessThan(DieWorkspaceFixture.Outline(f.Die2).X, "all content lives on Die 1 — the empty Die 2 is not framed");

        f.Select(f.DieVm(f.Die2));
        f.Canvas.Selection.ClearSelection();
        f.Canvas.SelectedComponent = null;

        ZoomToFitTarget.CurrentDie(f.Canvas, null).ShouldBe(f.Die2, "the die worked on last");
    }

    [Fact]
    public void System_FramesEveryDie()
    {
        var f = new DieWorkspaceFixture();
        f.Vm.WorkspaceSwitch.Current = Workspace.System;
        f.Select(f.PartVm(f.A));
        var carrier = DieAssembly.CarrierOutline(f.Canvas.Dies.RootDies)!.Value;

        var box = ZoomToFitTarget.Resolve(f.Canvas, fitsDies: true, cursor: null)!.Value;

        box.MinX.ShouldBe(carrier.X, 1e-6);
        box.MaxX.ShouldBe(carrier.Right, 1e-6);
    }

    [Fact]
    public void FKey_InLayout_ZoomsCloserThanInSystem()
    {
        var f = new DieWorkspaceFixture();
        f.Select(f.PartVm(f.A));
        f.Vm.ZoomToFit(ViewportPixels, ViewportPixels);
        double layoutZoom = f.Vm.ViewportControl.ZoomLevel;

        f.Vm.WorkspaceSwitch.Current = Workspace.System;
        f.Vm.ZoomToFit(ViewportPixels, ViewportPixels);

        layoutZoom.ShouldBeGreaterThan(f.Vm.ViewportControl.ZoomLevel * 4,
            "Layout frames the parts of the die, System the whole assembly");
    }

    [Fact]
    public void SingleDieDesign_LayoutFramesTheParts_SystemFramesTheChipDie()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.SetRootChipSize(3000, 2000);
        canvas.AddComponent(DieTestFixtures.Part("p", 500, 500));

        var parts = ZoomToFitTarget.Resolve(canvas, fitsDies: false, cursor: null)!.Value;
        var chip = ZoomToFitTarget.Resolve(canvas, fitsDies: true, cursor: null)!.Value;

        parts.Width.ShouldBeLessThan(100);
        chip.MaxX.ShouldBe(3000, 1e-6);
        chip.MaxY.ShouldBe(2000, 1e-6);
    }
}
