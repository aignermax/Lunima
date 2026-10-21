using CAP.Avalonia.Commands;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

public class CommandManagerAsyncTests
{
    private sealed class Entry(string name, List<string> undone) : IUndoableCommand
    {
        public string Description => name;
        public void Execute() { }
        public void Undo() => undone.Add(name);
    }

    [Fact]
    public void StaleAsyncCompletion_RefreshesAvailabilityOfNewDocumentHistory()
    {
        var undone = new List<string>();
        var commands = new CommandManager();
        var stale = new Entry("old document", undone);
        commands.BeginExecution(stale);
        commands.ClearHistory();
        commands.ExecuteCommand(new Entry("new document", undone));
        bool? notifiedCanUndo = null;
        commands.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(commands.CanUndo)) notifiedCanUndo = commands.CanUndo;
        };
        commands.ExecuteCommand(stale);
        commands.UndoCount.ShouldBe(1);
        notifiedCanUndo.ShouldBe(true);
        commands.Undo().ShouldBeTrue();
        undone.ShouldBe(new[] { "new document" });
    }

    [Fact]
    public void AbandonedAsyncExecution_PreservesExistingRedo()
    {
        var undone = new List<string>();
        var commands = new CommandManager();
        commands.ExecuteCommand(new Entry("previous", undone));
        commands.Undo();
        var abandoned = new Entry("abandoned", undone);
        commands.BeginExecution(abandoned);
        commands.CanRedo.ShouldBeFalse();
        commands.CancelExecution(abandoned);
        commands.CanRedo.ShouldBeTrue();
        commands.RedoDescription.ShouldBe("previous");
    }

    [Fact]
    public void AsyncExecution_KeepsItsPositionBeforeLaterEdits()
    {
        var undone = new List<string>();
        var commands = new CommandManager();
        var conversion = new Entry("conversion", undone);
        commands.BeginExecution(conversion);
        commands.ExecuteCommand(new Entry("later edit", undone));
        commands.ExecuteCommand(conversion);
        commands.Undo();
        commands.Undo();
        undone.ShouldBe(new[] { "later edit", "conversion" });
    }
}
