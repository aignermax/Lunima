using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Components.Core;

namespace UnitTests.Analysis.Snapshots;

internal static class GateViewSnapshotFixture
{
    internal static GateViewGraph Graph(params ComponentGroup[] groups) =>
        new GateViewGraphExtractor().Extract(groups, []);

    internal sealed class CountingExtractor(bool holdFirst = false) : TruthTableExtractor
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        internal LogicSnapshotFixture.WorkerBarrier Barrier { get; } = new();

        protected override async Task<TruthTable> SolveSnapshotAsync(TruthTableSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1 && holdFirst)
                await Barrier.WaitAsync();
            return await base.SolveSnapshotAsync(snapshot, cancellationToken);
        }
    }

    internal static async Task FinishAsync(CountingExtractor extractor, Task running)
    {
        extractor.Barrier.Release.TrySetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
