using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using UnitTests.Analysis.LogicAnalysis;

namespace UnitTests.Analysis.Snapshots;

internal static class LogicSnapshotFixture
{
    internal const int Wavelength = 1550;

    internal static ComponentGroup Gate(string name)
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        group.GroupName = name;
        group.TruthTablePinAssignment = new()
        {
            InputPinNames = ["a", "b"], OutputPinNames = ["y"], Threshold = 0.25
        };
        group.EnsureSMatrixComputed();
        return group;
    }

    internal static WaveguideConnection Connect(ComponentGroup source, ComponentGroup target)
    {
        var connection = new WaveguideConnection
        {
            StartPin = source.PhysicalPins.Single(pin => pin.Name == "y"),
            EndPin = target.PhysicalPins.Single(pin => pin.Name == "a")
        };
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        connection.RestoreCachedPath(route);
        return connection;
    }

    internal static void RemovePhysics(ComponentGroup group)
    {
        foreach (var component in group.GetAllComponentsRecursive().Append(group))
        {
            foreach (var matrix in component.WaveLengthToSMatrixMap.Values)
                matrix.SetValues(new(), reset: true);
            component.WaveLengthToSMatrixMap.Clear();
        }
    }

    internal sealed class WorkerBarrier
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task WaitAsync()
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        internal async Task<T> MutateThenFinishAsync<T>(Task<T> running, Action mutate)
        {
            try
            {
                await Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                mutate();
            }
            finally
            {
                Release.TrySetResult();
                await running.WaitAsync(TimeSpan.FromSeconds(10));
            }
            return await running;
        }
    }

    internal sealed class HeldExtractor : TruthTableExtractor
    {
        internal WorkerBarrier Barrier { get; } = new();
        protected override async Task<TruthTable> SolveSnapshotAsync(TruthTableSnapshot snapshot, CancellationToken token)
        {
            await Barrier.WaitAsync();
            return await base.SolveSnapshotAsync(snapshot, token);
        }
    }

    internal sealed class HeldAssembler : LogicNetworkAssembler
    {
        internal WorkerBarrier Barrier { get; } = new();
        protected override async Task<LogicNetworkEvaluator> SolveSnapshotAsync(LogicNetworkSnapshot snapshot, CancellationToken token)
        {
            await Barrier.WaitAsync();
            return await base.SolveSnapshotAsync(snapshot, token);
        }
    }
}
