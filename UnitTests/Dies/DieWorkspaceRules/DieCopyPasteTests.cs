using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.Dies.WorkSurface;
using Xunit;

namespace UnitTests.Dies.DieWorkspaceRules;

/// <summary>
/// Copy / paste of a die: the copy lands in a free spot next to the other dies (never
/// overlapping one), keeps its parts and its wires, and is one undo step. A die that overlaps
/// another one can always be moved out of the overlap, but never into a die.
/// </summary>
[Collection("LocalizationSingleton")]
public class DieCopyPasteTests
{
    [Fact]
    public void PastedDie_LandsFreeOfTheOthers_WithItsPartsAndWires()
    {
        var f = new DieWorkspaceFixture();
        f.Vm.WorkspaceSwitch.Current = Workspace.System;
        f.Select(f.DieVm(f.Die1));
        var routeBefore = WorkSurfaceCanvas.Geometry(f.Canvas.Connections.Single().Connection.RoutedPath);

        f.Vm.CanvasInteraction.CopySelectedCommand.Execute(null);
        f.Vm.CanvasInteraction.PasteSelected();

        var dies = f.Canvas.Dies.RootDies;
        dies.Count.ShouldBe(3);
        var pasted = dies.Single(d => d != f.Die1 && d != f.Die2);
        var outlines = dies.Select(DieAssembly.OutlineOf).ToList();
        for (int i = 0; i < outlines.Count; i++)
            for (int j = i + 1; j < outlines.Count; j++)
                outlines[i].Overlaps(outlines[j]).ShouldBeFalse($"dies {i} and {j} must not overlap");
        pasted.ChildComponents.Count.ShouldBe(2);
        var copiedWire = f.Canvas.Connections.Select(c => c.Connection)
            .Single(c => ReferenceEquals(c.StartPin.ParentComponent.ParentGroup, pasted));
        double dx = pasted.PhysicalX - f.Die1.PhysicalX, dy = pasted.PhysicalY - f.Die1.PhysicalY;
        WorkSurfaceCanvas.Geometry(copiedWire.RoutedPath).ShouldBe(
            routeBefore.Select(s => (s.Item1 + dx, s.Item2 + dy, s.Item3 + dx, s.Item4 + dy)).ToList(),
            "the wire is copied with its route, only translated");

        f.Vm.UndoCommand.Execute(null);
        f.Canvas.Dies.RootDies.Count.ShouldBe(2);
        f.Canvas.Connections.Count.ShouldBe(1);
        f.Vm.RedoCommand.Execute(null);
        f.Canvas.Dies.RootDies.Count.ShouldBe(3);
        f.Canvas.Connections.Count.ShouldBe(2);
    }

    [Fact]
    public void OverlappingDie_CanAlwaysMoveOut_ButNeverIntoAnotherDie()
    {
        var f = new DieWorkspaceFixture();
        var die2Vm = f.DieVm(f.Die2);
        var o1 = DieWorkspaceFixture.Outline(f.Die1);
        // Push Die 2 half into Die 1, as an old paste did.
        f.Canvas.BeginCommandExecution();
        f.Canvas.MoveComponent(die2Vm, o1.Right - 500 - die2Vm.X, 0);
        f.Canvas.EndCommandExecution();
        DieWorkspaceFixture.Outline(f.Die2).Overlaps(o1).ShouldBeTrue();

        f.Canvas.CanMoveComponentTo(die2Vm, die2Vm.X + 1000, die2Vm.Y).ShouldBeTrue("out of the overlap");
        f.Canvas.CanMoveComponentTo(die2Vm, die2Vm.X + 10, die2Vm.Y).ShouldBeTrue("still overlapping the same die is a step out");
        f.Canvas.CanMoveComponentTo(die2Vm, o1.X + 200 - (DieWorkspaceFixture.Outline(f.Die2).X - die2Vm.X), die2Vm.Y)
            .ShouldBeTrue("deeper into the die it already overlaps is not a new overlap");

        var free = DieAssembly.CanMoveTo(DieWorkspaceFixture.Outline(f.Die2) with { X = o1.Right + 5000 },
            o1 with { X = o1.X + 10 }, new[] { o1 });
        free.ShouldBeFalse("a die that does not overlap may never move into another");
    }
}
