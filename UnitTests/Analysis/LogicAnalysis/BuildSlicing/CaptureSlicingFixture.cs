using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using UnitTests.Analysis.Lifetime;
using UnitTests.Analysis.Snapshots;

namespace UnitTests.Analysis.LogicAnalysis.BuildSlicing;

/// <summary>Shared pieces of the capture-slicing tests: designs of N gates, a recording yield and a sliced panel.</summary>
internal static class CaptureSlicingFixture
{
    /// <summary>
    /// Continues an <c>[AvaloniaFact]</c> inside a dispatcher job. A test body starts under xUnit's
    /// synchronization context, while UI commands — and therefore the capture's frame yields — run
    /// under Avalonia's.
    /// </summary>
    internal static async Task ContinueInsideDispatcherJobAsync() => await Task.Yield();

    /// <summary>A chain of <paramref name="count"/> two-input gates, each output driving the next gate's input <c>a</c>.</summary>
    internal static (List<Component> Components, List<WaveguideConnection> Connections) GateChain(int count)
    {
        var gates = Enumerable.Range(0, count).Select(index => LogicSnapshotFixture.Gate($"g{index:D3}")).ToList();
        var connections = gates.Zip(gates.Skip(1), LogicSnapshotFixture.Connect).ToList();
        return (gates.Cast<Component>().ToList(), connections);
    }

    /// <summary>Counts yields and can reject the resumption of one of them, like a design edit during that frame.</summary>
    internal sealed class RecordingYield(int rejectAtYield = 0) : ICaptureYield
    {
        /// <summary>Number of yields requested so far.</summary>
        internal int Yields { get; private set; }

        /// <inheritdoc />
        public Task YieldAsync(CancellationToken cancellationToken)
        {
            Yields++;
            if (Yields == rejectAtYield)
                throw new OperationCanceledException("The design changed while the capture was yielded.");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Completes every yield later on a pool thread and records whether the capture asked for it
    /// on the UI thread: a slice that resumed off the owner thread shows up at the next boundary.
    /// </summary>
    internal sealed class PoolCompletingYield : ICaptureYield
    {
        private static readonly TimeSpan PoolCompletionDelay = TimeSpan.FromMilliseconds(1);

        /// <summary>Per yield, whether it was requested on the UI thread.</summary>
        internal List<bool> RequestedOnUiThread { get; } = new();

        /// <inheritdoc />
        public Task YieldAsync(CancellationToken cancellationToken)
        {
            RequestedOnUiThread.Add(Dispatcher.UIThread.CheckAccess());
            return Task.Delay(PoolCompletionDelay, cancellationToken);
        }
    }

    /// <summary>Records whether the topology capture, which ends right before the solve, ran on the UI thread.</summary>
    internal sealed class OwnerRecordingAssembler : LogicNetworkAssembler
    {
        /// <summary>Null until the capture hands its snapshot to the solve.</summary>
        internal bool? CapturedOnUiThread { get; private set; }

        protected override Task<LogicNetworkEvaluator> SolveSnapshotAsync(
            LogicNetworkSnapshot snapshot, CancellationToken cancellationToken)
        {
            CapturedOnUiThread = Dispatcher.UIThread.CheckAccess();
            return base.SolveSnapshotAsync(snapshot, cancellationToken);
        }
    }

    /// <summary>Counts every gate solve that reaches the worker.</summary>
    internal sealed class CountingExtractor : TruthTableExtractor
    {
        private int _solves;

        /// <summary>Number of gate solves started.</summary>
        internal int Solves => Volatile.Read(ref _solves);

        protected override Task<TruthTable> SolveSnapshotAsync(TruthTableSnapshot snapshot, CancellationToken token)
        {
            Interlocked.Increment(ref _solves);
            return base.SolveSnapshotAsync(snapshot, token);
        }
    }

    /// <summary>Stands in for the real capture: yields <see cref="SliceCount"/> times, then returns a small network.</summary>
    internal sealed class SlicingAssembler : ILogicNetworkAssembler
    {
        /// <summary>Capture slice boundaries the fake passes before it returns.</summary>
        internal const int SliceCount = 3;

        /// <summary>Slice boundaries that resumed successfully.</summary>
        internal int CompletedSlices { get; private set; }

        /// <inheritdoc />
        public async Task<LogicNetworkEvaluator> AssembleAsync(IReadOnlyList<Component> components,
            IReadOnlyList<WaveguideConnection> connections, int wavelengthNm, ICaptureYield captureSlices,
            CancellationToken cancellationToken = default)
        {
            for (var slice = 0; slice < SliceCount; slice++)
            {
                await captureSlices.YieldAsync(cancellationToken);
                CompletedSlices++;
            }
            return LogicLifetimeFixture.Network();
        }
    }

    /// <summary>A Logic panel over an empty canvas whose build runs through <see cref="SlicingAssembler"/>.</summary>
    internal sealed class SlicedPanel : IAsyncDisposable
    {
        public DesignCanvasViewModel Canvas { get; } = new();
        public BuiltLogicNetworkProvider Provider { get; } = new();
        public SlicingAssembler Assembler { get; } = new();
        public LogicPanelViewModel Model { get; }

        public SlicedPanel()
        {
            Model = new LogicPanelViewModel(new LogicLifetimeFixture.OwnedClock(), Provider, Assembler);
            Model.Configure(Canvas);
        }

        public Task BuildAsync() => Model.BuildNetworkCommand.ExecuteAsync(null);

        public async ValueTask DisposeAsync()
        {
            await Model.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Canvas.DisposeDocumentBindings();
        }
    }
}
