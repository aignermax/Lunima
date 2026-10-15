using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies.WorkSurface;

/// <summary>
/// Deleting, grouping, ungrouping and pasting parts on a die work in place at the assembly
/// root — each one undo step — and keep the parts on their die.
/// </summary>
public class DieSurfaceCommandTests
{
    [Fact]
    public void DeletingAPartOnADie_RemovesItAndItsWire_UndoPutsBothBack()
    {
        var w = new WorkSurfaceCanvas();
        var vm = w.Vm(w.A);

        w.Commands.ExecuteCommand(Wrap(w, new[] { vm }, () => new DeleteComponentCommand(w.Canvas, vm)));

        w.Die1.ChildComponents.ShouldNotContain(w.A);
        w.Canvas.Components.ShouldNotContain(vm);
        w.Canvas.Connections.ShouldBeEmpty();

        w.Commands.Undo();

        w.Die1.ChildComponents.ShouldContain(w.A, "the part is back on its die");
        w.Canvas.Components.ShouldNotContain(vm, "and not on the top level");
        w.Canvas.Connections.ShouldHaveSingleItem().Connection.ShouldBe(w.Wire.Connection);
    }

    [Fact]
    public void GroupingPartsOnADie_MakesAGroupOnTheDie_TheirWireMovesIntoTheGroup()
    {
        var w = new WorkSurfaceCanvas();
        var parts = new List<ComponentViewModel> { w.Vm(w.A), w.Vm(w.B) };

        w.Commands.ExecuteCommand(Wrap(w, parts, () => new CreateGroupCommand(w.Canvas, parts)));

        var group = w.Die1.ChildComponents.OfType<ComponentGroup>().ShouldHaveSingleItem();
        group.ChildComponents.ShouldBe(new[] { w.A, w.B }, ignoreOrder: true);
        group.InternalPaths.ShouldHaveSingleItem("the wire belongs to the new group (lowest common ancestor)");
        w.Canvas.Connections.ShouldBeEmpty();
        w.Canvas.Components.Select(c => (c.Component as ComponentGroup)?.IsDie == true).ShouldAllBe(isDie => isDie);

        w.Commands.Undo();

        w.Die1.ChildComponents.ShouldBe(new Component[] { w.A, w.B }, ignoreOrder: true);
        ConnectionOwnership.OwnerOf(w.Canvas.Connections.ShouldHaveSingleItem().Connection).ShouldBe(w.Die1);
    }

    [Fact]
    public void UngroupingAGroupOnADie_PutsItsPartsOnTheDie_ItsWireBecomesALiveDieWire()
    {
        var w = new WorkSurfaceCanvas();
        var parts = new List<ComponentViewModel> { w.Vm(w.A), w.Vm(w.B) };
        w.Commands.ExecuteCommand(Wrap(w, parts, () => new CreateGroupCommand(w.Canvas, parts)));
        var group = w.Die1.ChildComponents.OfType<ComponentGroup>().Single();
        var groupVm = w.Vm(group);

        w.Commands.ExecuteCommand(Wrap(w, new[] { groupVm }, () => new UngroupCommand(w.Canvas, group)));

        w.Die1.ChildComponents.ShouldBe(new Component[] { w.A, w.B }, ignoreOrder: true);
        ConnectionOwnership.OwnerOf(w.Canvas.Connections.ShouldHaveSingleItem().Connection).ShouldBe(w.Die1);

        w.Commands.Undo();

        w.Die1.ChildComponents.ShouldHaveSingleItem().ShouldBe(group);
        group.InternalPaths.ShouldHaveSingleItem();
    }

    [Fact]
    public void PastingAtTheAssemblyRoot_PutsThePastedPartsOnTheDieUnderThem()
    {
        var w = new WorkSurfaceCanvas();
        var parts = new List<ComponentViewModel> { w.Vm(w.A), w.Vm(w.B) };
        w.Canvas.Clipboard.Copy(parts, w.Canvas.Connections);
        var die2 = WorkSurfaceCanvas.Outline(w.Die2);
        var paste = new PasteComponentsCommand(w.Canvas, w.Canvas.Clipboard, die2.X + 300, die2.Y + 300);

        w.Commands.ExecuteCommand(new DieSurfaceCommand(w.Canvas, () => paste, Array.Empty<ComponentViewModel>(),
            part => CAP_Core.Components.Dies.DieAssembly.DieAt(w.Canvas.Components.Select(c => c.Component),
                part.PhysicalX, part.PhysicalY), "Paste"));

        w.Die2.ChildComponents.Count.ShouldBe(2, "the copies land on the die under the paste point");
        var pastedWire = w.Canvas.Connections.Single(c => !ReferenceEquals(c, w.Wire));
        ConnectionOwnership.OwnerOf(pastedWire.Connection).ShouldBe(w.Die2);

        w.Commands.Undo();

        w.Die2.ChildComponents.ShouldBeEmpty();
        w.Canvas.Connections.ShouldHaveSingleItem();
    }

    private static DieSurfaceCommand Wrap(WorkSurfaceCanvas w, IReadOnlyList<ComponentViewModel> parts,
        Func<CAP.Avalonia.Commands.IUndoableCommand> inner) =>
        new(w.Canvas, inner, parts, w.Die1, "test");
}
