using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies.WorkSurface;

/// <summary>
/// A wire dragged from one die to another: between facing edge couplers it becomes the
/// die-to-die link (the far die aligned onto the facet axis first, one undo step); between
/// any other pins it is refused and nothing changes.
/// </summary>
public class CrossDieWiringTests
{
    [Fact]
    public void AWaveguideAcrossADieEdge_IsNeverCreated()
    {
        var w = new WorkSurfaceCanvas();
        var other = Part("other", 0, 0);
        AddPart(w, w.Die2, other, 200, 200);

        var connect = new CreateConnectionCommand(w.Canvas, w.B.PhysicalPins[1], other.PhysicalPins[0]);
        connect.CrossesDieEdge.ShouldBeTrue();
        w.Commands.ExecuteCommand(connect);

        w.Canvas.Connections.ShouldHaveSingleItem().ShouldBe(w.Wire, "the refused wire was not added");
        FacetWireCommandFactory.Create(w.Canvas, w.B.PhysicalPins[1], other.PhysicalPins[0])
            .RefusalKey.ShouldBe("Dies.Wiring.CouplersOnly");
    }

    [Fact]
    public void FacingEdgeCouplers_AreLinked_TheFarDieIsAlignedFirst_OneUndoStep()
    {
        var w = new WorkSurfaceCanvas();
        var outline1 = WorkSurfaceCanvas.Outline(w.Die1);
        var outline2 = WorkSurfaceCanvas.Outline(w.Die2);
        var east = EdgeCoupler("east", 0, 0);
        AddPart(w, w.Die1, east, outline1.Right - CouplerWidth, 500);
        ComponentPoseTransform.Rotate90CounterClockwise(east);
        ComponentPoseTransform.Rotate90CounterClockwise(east);
        var west = EdgeCoupler("west", 0, 0);
        AddPart(w, w.Die2, west, outline2.X, 300);
        var facetEast = EdgeCouplerFacet.FacetPinOf(east)!;
        var facetWest = EdgeCouplerFacet.FacetPinOf(west)!;
        double die2X = w.Die2.PhysicalX;

        var result = FacetWireCommandFactory.Create(w.Canvas, facetEast, facetWest);
        result.RefusalKey.ShouldBeNull();
        w.Commands.ExecuteCommand(result.Command!);

        var link = w.Canvas.Connections.Single(c => c.Connection.IsCrossChipletFacetLink).Connection;
        DieLinkGeometry.Gap(link).ShouldBe(DieLinkSnapper.DefaultGapMicrometers, 1e-6);
        facetWest.GetAbsolutePosition().Item2.ShouldBe(facetEast.GetAbsolutePosition().Item2, 1e-6, "aligned on the facet axis");

        w.Commands.Undo();

        w.Canvas.Connections.ShouldNotContain(c => c.Connection.IsCrossChipletFacetLink);
        w.Die2.PhysicalX.ShouldBe(die2X, 1e-9);
    }

    private static void AddPart(WorkSurfaceCanvas w, ComponentGroup die, Component part, double x, double y)
    {
        part.PhysicalX = x;
        part.PhysicalY = y;
        die.AddChild(part);
        w.Canvas.RefreshDieInterfaces();
    }
}
