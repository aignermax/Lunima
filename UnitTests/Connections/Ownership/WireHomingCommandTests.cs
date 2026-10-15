using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Connections.Ownership;

/// <summary>
/// The owner's scenario: group M holds K1 and K2. Connecting K1 ↔ K2 while looking at the
/// level above M stores the wire in M — visible and editable when M is entered, moving with
/// M — and undo/redo of the connect is one exact step.
/// </summary>
public class WireHomingCommandTests
{
    [Fact]
    public async Task ConnectingK1ToK2_OutsideM_StoresTheWireInM()
    {
        var (canvas, f, mVm) = CanvasWithM();
        var pinsBefore = f.M.ExternalPins.ToList();
        var consumedName = f.MPinOfP1East.Name;
        var connect = new CreateConnectionCommand(canvas, f.MPinOfP1East, f.MPinOfP2West);

        new CommandManager().ExecuteCommand(connect);
        await connect.HomingCompletion;

        f.M.InternalPaths.ShouldHaveSingleItem().Path.Segments.ShouldNotBeEmpty("the routed geometry is kept");
        canvas.Connections.ShouldBeEmpty("the wire lives in M, not on the level above");
        f.M.ExternalPins.Count.ShouldBe(pinsBefore.Count - 2);
        canvas.AllPins.Where(p => p.ParentComponentViewModel == mVm).Select(p => p.Pin)
            .ShouldNotContain(p => p.Name == consumedName);
    }

    [Fact]
    public async Task EnteringM_ShowsTheWire_LeavingKeepsIt()
    {
        var (canvas, f, _) = CanvasWithM();
        var connect = new CreateConnectionCommand(canvas, f.MPinOfP1East, f.MPinOfP2West);
        new CommandManager().ExecuteCommand(connect);
        await connect.HomingCompletion;

        canvas.EnterGroupEditMode(f.M);

        canvas.Connections.ShouldHaveSingleItem("the wire is visible and editable inside M");
        canvas.ExitToRoot();
        f.M.InternalPaths.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MovingM_MovesTheWireWithIt()
    {
        var (canvas, f, mVm) = CanvasWithM();
        var connect = new CreateConnectionCommand(canvas, f.MPinOfP1East, f.MPinOfP2West);
        var manager = new CommandManager();
        manager.ExecuteCommand(connect);
        await connect.HomingCompletion;
        var startBefore = f.M.InternalPaths.Single().Path.Segments[0].StartPoint;

        manager.ExecuteCommand(new MoveComponentCommand(canvas, mVm, mVm.X, mVm.Y, mVm.X + 200, mVm.Y + 50));

        var startAfter = f.M.InternalPaths.Single().Path.Segments[0].StartPoint;
        (startAfter.X - startBefore.X, startAfter.Y - startBefore.Y).ShouldBe((200, 50));
    }

    [Fact]
    public async Task UndoAndRedo_TakeTheWireOutOfMAndPutItBack_Exactly()
    {
        var (canvas, f, _) = CanvasWithM();
        var pinsBefore = f.M.ExternalPins.ToList();
        var manager = new CommandManager();
        var connect = new CreateConnectionCommand(canvas, f.MPinOfP1East, f.MPinOfP2West);
        manager.ExecuteCommand(connect);
        await connect.HomingCompletion;

        manager.Undo();

        f.M.InternalPaths.ShouldBeEmpty();
        f.M.ExternalPins.ShouldBe(pinsBefore);
        canvas.Connections.ShouldBeEmpty();

        manager.Redo();

        f.M.InternalPaths.ShouldHaveSingleItem();
        canvas.Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task AWireBetweenTwoTopLevelGroups_StaysOnTheCanvas()
    {
        var (canvas, f, _) = CanvasWithM();
        var other = new OwnershipFixtures(100, 600);
        canvas.AddComponent(other.M);
        var connect = new CreateConnectionCommand(canvas, f.MPinOfP2West, other.MPinOfP1East);

        new CommandManager().ExecuteCommand(connect);
        await connect.HomingCompletion;

        canvas.Connections.ShouldHaveSingleItem("its owner is the root, the level it was drawn on");
        f.M.InternalPaths.ShouldBeEmpty();
    }

    [Fact]
    public void LoadMigration_StoresAMisplacedRootWireInM()
    {
        var (canvas, f, _) = CanvasWithM();
        canvas.ConnectPinsWithCachedRoute(f.MPinOfP1East, f.MPinOfP2West,
            UnitTests.Dies.WorkSurface.WorkSurfaceCanvas.StraightBetween(f.MPinOfP1East, f.MPinOfP2West));

        var plan = canvas.ApplyWireOwnership();

        plan.RootWiresToGroups.ShouldHaveSingleItem().Owner.ShouldBe(f.M);
        canvas.Connections.ShouldBeEmpty();
        f.M.InternalPaths.ShouldHaveSingleItem();
        canvas.ApplyWireOwnership().IsEmpty.ShouldBeTrue("the migration is idempotent");
    }

    private static (DesignCanvasViewModel Canvas, OwnershipFixtures F, ComponentViewModel MVm) CanvasWithM()
    {
        var canvas = new DesignCanvasViewModel();
        var f = new OwnershipFixtures();
        var mVm = canvas.AddComponent(f.M);
        return (canvas, f, mVm);
    }
}
