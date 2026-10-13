using CAP.Avalonia.Controls.Canvas.CrossingDrag;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using UnitTests.Routing.CrossingInsertion.CrossingMove;
using Xunit;

namespace UnitTests.Controls.Canvas.CrossingDrag;

/// <summary>
/// Axis-drag snapping of a crossing: a port snaps onto a facing pin's free coordinate within
/// the snap tolerance (and the guide for it is published), the grid applies otherwise, and
/// the locked coordinate never produces guides.
/// </summary>
public class CrossingAxisSnapperTests
{
    private const double FacingPinX = 343;
    private const double FacingPinY = 150;
    private const double SouthDegrees = 90;

    [Fact]
    public void HorizontalDrag_NearAFacingPin_SnapsTheNorthPortOntoItsX_AndShowsOnlyThatGuide()
    {
        var (scene, drag) = SceneWithFacingPin();
        double portX = CrossedWiresScene.Port(scene.Crossing, "port 3").GetAbsolutePosition().x;

        double offset = new CrossingAxisSnapper().Snap(scene.Canvas, drag, CrossingAxis.Horizontal, FacingPinX - portX - 3);

        offset.ShouldBe(FacingPinX - portX, 1e-9);
        scene.Canvas.AlignmentGuide.VerticalAlignments.ShouldHaveSingleItem().XCoordinate.ShouldBe(FacingPinX, 1e-9);
        scene.Canvas.AlignmentGuide.HorizontalAlignments.ShouldBeEmpty("the locked Y never shows guides");
    }

    [Fact]
    public void HorizontalDrag_OutsideTheSnapTolerance_KeepsTheRawOffset_WithoutGuides()
    {
        var (scene, drag) = SceneWithFacingPin();
        double portX = CrossedWiresScene.Port(scene.Crossing, "port 3").GetAbsolutePosition().x;
        double raw = FacingPinX - portX - 20;

        double offset = new CrossingAxisSnapper().Snap(scene.Canvas, drag, CrossingAxis.Horizontal, raw);

        offset.ShouldBe(raw, 1e-9);
        scene.Canvas.AlignmentGuide.HasAlignments.ShouldBeFalse();
    }

    [Fact]
    public void HorizontalDrag_WithoutANearbyPin_SnapsTheCentreToTheGrid()
    {
        var (scene, drag) = SceneWithFacingPin();
        scene.Canvas.GridSnap.GridSizeMicrometers = 25;
        scene.Canvas.GridSnap.IsEnabled = true;

        double offset = new CrossingAxisSnapper().Snap(scene.Canvas, drag, CrossingAxis.Horizontal, -20);

        (drag.CenterX + offset).ShouldBe(275, 1e-9, "the crossing centre lands on the 25 µm grid");
    }

    [Fact]
    public void PinSnap_WinsOverTheGrid()
    {
        var (scene, drag) = SceneWithFacingPin();
        scene.Canvas.GridSnap.IsEnabled = true;
        double portX = CrossedWiresScene.Port(scene.Crossing, "port 3").GetAbsolutePosition().x;

        double offset = new CrossingAxisSnapper().Snap(scene.Canvas, drag, CrossingAxis.Horizontal, FacingPinX - portX + 2);

        offset.ShouldBe(FacingPinX - portX, 1e-9);
    }

    [Fact]
    public void DisabledAlignmentGuides_NeitherSnapToPinsNorShowGuides()
    {
        var (scene, drag) = SceneWithFacingPin();
        scene.Canvas.AlignmentGuide.IsEnabled = false;
        double portX = CrossedWiresScene.Port(scene.Crossing, "port 3").GetAbsolutePosition().x;
        double raw = FacingPinX - portX - 3;

        double offset = new CrossingAxisSnapper().Snap(scene.Canvas, drag, CrossingAxis.Horizontal, raw);

        offset.ShouldBe(raw, 1e-9);
        scene.Canvas.AlignmentGuide.HasAlignments.ShouldBeFalse();
    }

    private static (CrossedWiresScene Scene, CrossingAxisDragPreview Drag) SceneWithFacingPin()
    {
        var scene = CrossedWiresScene.Build();
        var terminal = CrossingTestCircuit.CreateTerminal("facing", FacingPinX, FacingPinY, SouthDegrees);
        var (x, y) = terminal.PhysicalPin.GetAbsolutePosition();
        terminal.Component.PhysicalX += FacingPinX - x;
        terminal.Component.PhysicalY += FacingPinY - y;
        scene.Canvas.AddComponent(terminal.Component, "Terminal");
        var crossingVm = scene.Canvas.Components.First(c => c.Component == scene.Crossing);
        return (scene, new CrossingAxisDragPreview(crossingVm));
    }
}
