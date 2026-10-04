using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using Shouldly;
using UnitTests.Components;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Undo test for the "Align chiplet" one-click fix (issue #1248): aligning through
/// <see cref="ChipletAlignmentService"/> must go through the undoable group-move
/// command, so a single Undo restores the shifted chiplet's position exactly.
/// </summary>
public class AlignChipletUndoTests
{
    private const double WavelengthNm = 1550;

    [Fact]
    public void Align_ThenUndo_RestoresOriginalPositions()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        var canvas = design.Canvas;
        var commandManager = new CommandManager();

        design.ChipletB.MoveGroup(7.0, 2.0); // gap + lateral offset
        double shiftedX = design.ChipletB.PhysicalX;
        double shiftedY = design.ChipletB.PhysicalY;

        var link = CrossChipletLink(canvas);
        var checker = new ChipletInterfaceChecker();
        checker.Check(new[] { link }, WavelengthNm).ShouldNotBeEmpty("the shifted link must warn");

        var service = new ChipletAlignmentService(canvas, commandManager);
        service.TryAlign(link, WavelengthNm).ShouldBeNull("the unblocked link must align");

        checker.Check(new[] { link }, WavelengthNm).ShouldBeEmpty("the aligned link must come back clean");
        ChipletEdgeCouplerCoupling.FieldFactor(link, WavelengthNm).ShouldBe(1.0);
        commandManager.CanUndo.ShouldBeTrue("the alignment must be recorded as an undoable command");

        commandManager.Undo().ShouldBeTrue();

        design.ChipletB.PhysicalX.ShouldBe(shiftedX, "undo must restore the pre-alignment position exactly");
        design.ChipletB.PhysicalY.ShouldBe(shiftedY);
        checker.Check(new[] { link }, WavelengthNm).ShouldNotBeEmpty("the warning must be back after undo");
    }

    [Fact]
    public void Align_BlockedByComponent_LeavesUndoStackUntouched()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        var canvas = design.Canvas;
        var commandManager = new CommandManager();

        var link = CrossChipletLink(canvas);
        var endCoupler = link.EndPin.ParentComponent;
        double targetX = endCoupler.PhysicalX; // chiplet B's coupler position before the shift…
        double targetY = endCoupler.PhysicalY;

        design.ChipletB.MoveGroup(7.0, 2.0);
        double shiftedX = design.ChipletB.PhysicalX;
        double shiftedY = design.ChipletB.PhysicalY;

        // …is exactly where the alignment would move it back to — block that spot.
        var blockerTemplate = MultiProcessChipletJourneyDesign.TemplateFor(
            design.DemoPdk, "Straight Waveguide 100µm");
        var blocker = CAP.Avalonia.ViewModels.Library.ComponentTemplates.CreateFromTemplate(
            blockerTemplate, targetX, targetY);
        canvas.AddComponent(blocker, blockerTemplate.Name, blockerTemplate.PdkSource);

        var service = new ChipletAlignmentService(canvas, commandManager);

        var refusal = service.TryAlign(link, WavelengthNm);

        refusal.ShouldBe(ChipletAlignmentRefusal.Overlap);
        design.ChipletB.PhysicalX.ShouldBe(shiftedX, "a refused alignment must not move the chiplet");
        design.ChipletB.PhysicalY.ShouldBe(shiftedY);
        commandManager.CanUndo.ShouldBeFalse("a refused alignment must not record an undoable command");
    }

    /// <summary>The single cross-chiplet edge-coupler link of the journey design.</summary>
    private static WaveguideConnection CrossChipletLink(CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel canvas) =>
        canvas.ConnectionManager.Connections.Single(c =>
            ChipletInterfaceChecker.TryGetFacet(c.StartPin, out var start)
            && ChipletInterfaceChecker.TryGetFacet(c.EndPin, out var end)
            && !ReferenceEquals(start.Chiplet, end.Chiplet));
}
