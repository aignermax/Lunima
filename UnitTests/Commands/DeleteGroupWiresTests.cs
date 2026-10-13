using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Top-level wires to a gate end at the leaf pins inside the gate group, not at a pin of the
/// group itself. Deleting the group must still take those wires with it — on the canvas and
/// in the connection manager — and undo must bring them back.
/// </summary>
public class DeleteGroupWiresTests
{
    private const string ExampleFileName = "Logic Gate Half Adder.lun";

    [Fact]
    public async Task DeleteAll_InExampleWithGates_LeavesNoWire()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;
        var commands = new CommandManager();
        var interaction = new CanvasInteractionViewModel(canvas, commands);
        int wiresBefore = canvas.Connections.Count;
        wiresBefore.ShouldBeGreaterThan(0);

        canvas.Selection.SelectInRectangle(canvas.Components, -1e7, -1e7, 2e7, 2e7);
        interaction.DeleteSelectedCommand.Execute(null);

        canvas.Components.ShouldBeEmpty();
        canvas.Connections.ShouldBeEmpty("no wire may survive the components it connects");
        canvas.ConnectionManager.Connections.ShouldBeEmpty();

        commands.Undo();
        canvas.Connections.Count.ShouldBe(wiresBefore);
        canvas.ConnectionManager.Connections.Count.ShouldBe(wiresBefore);
    }
}
