using Avalonia;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls;
using CAP_Core.Components.Connections.Ownership;
using Shouldly;
using Xunit;

namespace UnitTests.Dies.WorkSurface;

/// <summary>
/// A die is a work surface: at the assembly root its parts are hit, selected, moved and
/// checked in place; moving a die moves everything on it rigidly; in the System workspace
/// the content is locked and clicks reach the die.
/// </summary>
public class DieSurfaceTests
{
    [Fact]
    public void PartsOnADie_AreHitDirectly_AtTheAssemblyRoot()
    {
        var w = new WorkSurfaceCanvas();

        var hit = DesignCanvasHitTesting.HitTestComponent(new Point(A_Center(w).X, A_Center(w).Y), w.Canvas);

        hit.ShouldBe(w.Vm(w.A), "the part, not its die, is under the pointer");
        w.Canvas.Surface.IsItem(hit).ShouldBeTrue();
        w.Canvas.InteractiveComponents.ShouldContain(w.Vm(w.A));
        w.Canvas.InteractiveComponents.ShouldNotContain(w.DieVm(w.Die1));
    }

    [Fact]
    public void EmptyDieArea_HitsTheDie()
    {
        var w = new WorkSurfaceCanvas();
        var outline = WorkSurfaceCanvas.Outline(w.Die2);

        DesignCanvasHitTesting.HitTestComponent(new Point(outline.CenterX, outline.CenterY), w.Canvas)
            .ShouldBe(w.DieVm(w.Die2));
    }

    [Fact]
    public void LockedContent_SystemWorkspace_HitsTheDieAndOffersNoPartPins()
    {
        var w = new WorkSurfaceCanvas();
        w.Canvas.Surface.IsContentLocked = true;

        DesignCanvasHitTesting.HitTestComponent(new Point(A_Center(w).X, A_Center(w).Y), w.Canvas).ShouldBe(w.DieVm(w.Die1));
        var (px, py) = w.A.PhysicalPins[0].GetAbsolutePosition();
        DesignCanvasHitTesting.HitTestPin(new Point(px, py), w.Canvas).ShouldBeNull();
        DesignCanvasHitTesting.HitTestConnection(new Point(250, 110), w.Canvas).ShouldBeNull("die wires are locked too");
    }

    [Fact]
    public void Placement_OnADie_IsValid_OffEveryDie_IsNot()
    {
        var w = new WorkSurfaceCanvas();
        var die2 = WorkSurfaceCanvas.Outline(w.Die2);

        w.Canvas.CanPlaceComponent(die2.X + 200, die2.Y + 200, 60, 20).ShouldBeTrue("no red outline on a die");
        w.Canvas.CanPlaceComponent(die2.X - 300, die2.Y + 200, 60, 20).ShouldBeFalse("the bare carrier is no place for a part");
        w.Canvas.CanPlaceComponent(110, 100, 60, 20).ShouldBeFalse("overlapping part a");
    }

    [Fact]
    public void MovingAPart_StaysOnItsDie_AndIsUndoable()
    {
        var w = new WorkSurfaceCanvas();
        var vm = w.Vm(w.A);

        w.Canvas.CanMoveComponentTo(vm, 100, 600).ShouldBeTrue();
        w.Canvas.CanMoveComponentTo(vm, WorkSurfaceCanvas.Outline(w.Die2).X + 100, 100)
            .ShouldBeFalse("a part stays on its die");

        w.Commands.ExecuteCommand(new MoveComponentCommand(w.Canvas, vm, 100, 100, 100, 600));
        w.A.PhysicalY.ShouldBe(600);
        w.Die1.ChildComponents.ShouldContain(w.A);
        w.Commands.Undo();
        w.A.PhysicalY.ShouldBe(100);
    }

    [Fact]
    public void MovingADie_TranslatesItsPartsAndRoutesRigidly_NothingIsRerouted()
    {
        var w = new WorkSurfaceCanvas();
        var before = WorkSurfaceCanvas.Geometry(w.Wire.Connection.RoutedPath);
        var dieVm = w.DieVm(w.Die1);
        var (x0, y0) = (dieVm.X, dieVm.Y);

        w.Commands.ExecuteCommand(new MoveComponentCommand(w.Canvas, dieVm, x0, y0, x0 - 700, y0 + 250));

        w.A.PhysicalX.ShouldBe(100 - 700);
        WorkSurfaceCanvas.Geometry(w.Wire.Connection.RoutedPath)
            .ShouldBe(before.Select(s => (s.Item1 - 700, s.Item2 + 250, s.Item3 - 700, s.Item4 + 250)).ToList(),
                "the route on the die is identical, only translated");
        w.Canvas.IsRouting.ShouldBeFalse("a die move never starts a routing pass");

        w.Commands.Undo();
        WorkSurfaceCanvas.Geometry(w.Wire.Connection.RoutedPath).ShouldBe(before);
    }

    [Fact]
    public void TheWireBetweenPartsOfADie_BelongsToTheDie_AndStaysLive()
    {
        var w = new WorkSurfaceCanvas();

        ConnectionOwnership.OwnerOf(w.Wire.Connection).ShouldBe(w.Die1);
        w.Canvas.Connections.ShouldContain(w.Wire, "a die's wires are edited in place");
        w.Die1.InternalPaths.ShouldBeEmpty();
    }

    private static (double X, double Y) A_Center(WorkSurfaceCanvas w) =>
        (w.A.PhysicalX + w.A.WidthMicrometers / 2, w.A.PhysicalY + w.A.HeightMicrometers / 2);
}
