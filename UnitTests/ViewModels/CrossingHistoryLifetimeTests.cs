using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>Crossing side effects follow the lifetime of their owning history entry.</summary>
public class CrossingHistoryLifetimeTests
{
    [Fact]
    public void Overflow_UndoRetainedCommandsRestoresExactTopologyAndConnectionOrder()
    {
        var fixture = new Fixture();
        var before = new List<WaveguideConnection[]>();
        for (var i = 0; i < 130; i++)
        {
            before.Add(fixture.Canvas.ConnectionManager.Connections.ToArray());
            fixture.Commands.ExecuteCommand(fixture.CreateEdit());
        }

        fixture.Commands.UndoCount.ShouldBe(100);
        for (var i = 129; i >= 30; i--)
        {
            fixture.Commands.Undo().ShouldBeTrue();
            fixture.Canvas.ConnectionManager.Connections.ShouldBe(before[i]);
            fixture.Canvas.Components.Count.ShouldBe(i);
        }
        fixture.Commands.Undo().ShouldBeFalse();
    }

    [Fact]
    public void Overflow_RetiresOnlyEvictedMetadata()
    {
        var fixture = new Fixture();
        var entries = Enumerable.Range(0, 101).Select(_ => fixture.CreateEdit()).ToArray();
        foreach (var entry in entries) fixture.Commands.ExecuteCommand(entry);

        fixture.Journal.ChangeCount(entries[0]).ShouldBe(0);
        fixture.Journal.ChangeCount(entries[1]).ShouldBe(1);
        fixture.Journal.ChangeCount(entries[100]).ShouldBe(1);
    }

    [Fact]
    public async Task CanceledReservation_RejectsLateWorkerMetadata()
    {
        var fixture = new Fixture();
        var entry = fixture.CreateEdit();
        fixture.Commands.BeginExecution(entry);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Task.Run(async () =>
        {
            await release.Task;
            fixture.Journal.Record(new CrossingChange([], [], [], () => { }));
        });
        fixture.Commands.CancelExecution(entry);
        release.SetResult();
        await worker;
        fixture.Journal.ChangeCount(entry).ShouldBe(0);
    }

    [Fact]
    public void CanceledReservation_RejectsLateCompletion()
    {
        var fixture = new Fixture();
        var entry = fixture.CreateEdit();
        fixture.Commands.BeginExecution(entry);
        fixture.Commands.CancelExecution(entry);
        fixture.Commands.ExecuteCommand(entry);
        fixture.Commands.UndoCount.ShouldBe(0);
        fixture.Canvas.ConnectionManager.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void Reset_ReleasesPendingReservationWithoutWaitingForWorker()
    {
        var fixture = new Fixture();
        var oldEntry = fixture.CreateEdit();
        fixture.Commands.BeginExecution(oldEntry);
        fixture.Commands.ClearHistory();
        fixture.Commands.ExecuteCommand(fixture.CreateEdit());

        fixture.Commands.CanUndo.ShouldBeTrue();
        fixture.Commands.ExecuteCommand(oldEntry);
        fixture.Commands.UndoCount.ShouldBe(1);
        fixture.Canvas.Components.Count.ShouldBe(1);
    }

    [Fact]
    public void UndoRedo_RecreatesCrossingChangesForRetainedEntry()
    {
        var fixture = new Fixture();
        fixture.Commands.ExecuteCommand(fixture.CreateEdit());
        fixture.Commands.ExecuteCommand(fixture.CreateEdit());
        var edited = fixture.Canvas.ConnectionManager.Connections.ToArray();

        fixture.Commands.Undo().ShouldBeTrue();
        fixture.Commands.Redo().ShouldBeTrue();
        fixture.Canvas.ConnectionManager.Connections.ShouldBe(edited);
        fixture.Commands.Undo().ShouldBeTrue();
        fixture.Commands.Undo().ShouldBeTrue();
        fixture.Canvas.ConnectionManager.Connections.ShouldBeEmpty();
        fixture.Canvas.Components.ShouldBeEmpty();
    }

    [Fact]
    public void MergedEdits_UndoAndRedoPreserveAllSideEffectsAndInitialOrder()
    {
        var fixture = new Fixture();
        fixture.Commands.ExecuteCommand(fixture.CreateEdit());
        var before = fixture.Canvas.ConnectionManager.Connections.ToArray();
        var first = new MergedEdit(fixture.CreateEdit());
        var second = new MergedEdit(fixture.CreateEdit());
        fixture.Commands.ExecuteCommand(first);
        fixture.Commands.ExecuteCommand(second);
        var edited = fixture.Canvas.ConnectionManager.Connections.ToArray();

        fixture.Commands.UndoCount.ShouldBe(2);
        fixture.Journal.ChangeCount(first).ShouldBe(2);
        fixture.Journal.ChangeCount(second).ShouldBe(0);
        fixture.Commands.Undo().ShouldBeTrue();
        fixture.Canvas.ConnectionManager.Connections.ShouldBe(before);
        fixture.Commands.Redo().ShouldBeTrue();
        fixture.Canvas.ConnectionManager.Connections.ShouldBe(edited);
        fixture.Commands.Undo().ShouldBeTrue();
        fixture.Canvas.ConnectionManager.Connections.ShouldBe(before);
    }

    [Fact]
    public void CanceledReservation_RetiresItsMetadataWithoutLosingLivePeer()
    {
        var fixture = new Fixture();
        var retained = fixture.CreateEdit();
        fixture.Commands.ExecuteCommand(retained);
        var canceled = fixture.CreateEdit();
        fixture.Commands.BeginExecution(canceled);
        fixture.Journal.Record(new CrossingChange([], [], [], () => { }));
        fixture.Commands.CancelExecution(canceled);

        fixture.Journal.ChangeCount(canceled).ShouldBe(0);
        fixture.Journal.ChangeCount(retained).ShouldBe(1);
        fixture.Commands.Undo().ShouldBeTrue();
        fixture.Canvas.Components.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("trim")]
    [InlineData("redo discarded")]
    [InlineData("reset")]
    public async Task RetiredEntry_RejectsLateWorkerMetadata(string retirement)
    {
        var fixture = new Fixture();
        var entry = fixture.CreateEdit();
        fixture.Commands.ExecuteCommand(entry);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Task.Run(async () =>
        {
            await release.Task;
            fixture.Journal.Record(new CrossingChange([], [], [], () => { }));
        });
        if (retirement == "trim")
            for (var i = 0; i < 100; i++) fixture.Commands.ExecuteCommand(fixture.CreateEdit());
        else if (retirement == "redo discarded")
        {
            fixture.Commands.Undo();
            fixture.Commands.ExecuteCommand(fixture.CreateEdit());
        }
        else fixture.Commands.ClearHistory();
        release.SetResult();
        await worker;

        fixture.Journal.ChangeCount(entry).ShouldBe(0);
    }

    private sealed class Fixture
    {
        public DesignCanvasViewModel Canvas { get; } = new();
        public CommandManager Commands { get; } = new();
        public CrossingUndoJournal Journal { get; }

        public Fixture()
        {
            Journal = new CrossingUndoJournal(Canvas,
                crossing => CrossingCanvasSync.AddCrossing(Canvas, crossing, null, null),
                crossing => CrossingCanvasSync.RemoveCrossing(Canvas, crossing));
            Journal.Attach(Commands);
        }

        public IUndoableCommand CreateEdit()
        {
            var crossing = CrossingTestCircuit.CreateCrossingComponent();
            var connection = new WaveguideConnection
            {
                StartPin = crossing.PhysicalPins[0], EndPin = crossing.PhysicalPins[1]
            };
            return new Edit(() =>
            {
                Canvas.ConnectionManager.AddExistingConnection(connection);
                Canvas.ConnectionManager.Connections.Reverse();
                CrossingCanvasSync.AddCrossing(Canvas, crossing, null, null);
                Journal.Record(new CrossingChange([crossing], [], [connection],
                    () => Canvas.ConnectionManager.RemoveConnectionDeferred(connection)));
            });
        }
    }

    private sealed class Edit(Action execute) : IUndoableCommand
    {
        public string Description => "Edit with routing side effects";
        public void Execute() => execute();
        public void Undo() { }
    }

    private sealed class MergedEdit(IUndoableCommand initial) : IMergeableCommand
    {
        private readonly List<IUndoableCommand> _edits = [initial];
        public string Description => "Merged edits with routing side effects";
        public void Execute() { foreach (var edit in _edits) edit.Execute(); }
        public void Undo() { }
        public bool CanMergeWith(IUndoableCommand other) => other is MergedEdit;
        public void MergeWith(IUndoableCommand other) => _edits.AddRange(((MergedEdit)other)._edits);
    }
}
