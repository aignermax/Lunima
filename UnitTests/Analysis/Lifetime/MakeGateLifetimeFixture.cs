using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using UnitTests.Analysis.Snapshots;

namespace UnitTests.Analysis.Lifetime;

internal sealed class MakeGateLifetimeFixture : IAsyncDisposable
{
    internal DesignCanvasViewModel Canvas { get; } = new();
    internal ComponentGroup Group { get; } = LogicSnapshotFixture.Gate("original");
    internal ComponentViewModel Component { get; }
    internal HeldExtractor Extractor { get; } = new();
    internal MakeGateViewModel Panel { get; }

    internal MakeGateLifetimeFixture()
    {
        Component = new(Group);
        Canvas.Components.Add(Component);
        Canvas.Selection.SelectSingle(Component);
        Panel = new(Extractor);
        Panel.ConfigureForSelection(Component, Canvas);
        Panel.OpenCommand.Execute(null);
    }

    internal void ChangeContext(string change)
    {
        switch (change)
        {
            case "edit": Canvas.Document.Invalidate(); break;
            case "replace": Canvas.Document.ReplaceDocument(); break;
            case "remove": Canvas.Components.Remove(Component); break;
            case "close": Panel.CloseCommand.Execute(null); break;
            case "roles": Panel.Pins.Single(pin => pin.PinName == "b").IsUnused = true; break;
            case "selection":
                var other = new ComponentViewModel(LogicSnapshotFixture.Gate("other"));
                Canvas.Components.Add(other);
                Canvas.Selection.SelectSingle(other);
                Panel.ConfigureForSelection(other, Canvas);
                Panel.OpenCommand.Execute(null);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    internal async Task FinishAsync(Task running)
    {
        Extractor.Release.TrySetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        Extractor.Release.TrySetResult();
        await Panel.StopAsync();
        await Canvas.Document.RetireAsync();
        Canvas.DisposeDocumentBindings();
    }

    internal sealed class HeldExtractor : TruthTableExtractor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        internal bool FailOnRelease { get; set; }
        internal bool HonorCancellation { get; set; }

        protected override async Task<TruthTable> SolveSnapshotAsync(TruthTableSnapshot snapshot, CancellationToken token)
        {
            Calls++;
            using var registration = token.Register(() => Cancelled.TrySetResult());
            var result = await snapshot.SolveAsync(token);
            Started.TrySetResult();
            // Hold a real completed solve before delivery. Cancellation after a solver's last
            // checkpoint must still be checked by its UI owner before publication.
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (HonorCancellation) token.ThrowIfCancellationRequested();
            if (FailOnRelease) throw new InvalidOperationException("late extraction failure");
            return result;
        }
    }
}
