using Avalonia.Headless.XUnit;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Analysis.Snapshots;
using Xunit;
using static UnitTests.Analysis.LogicAnalysis.BuildSlicing.CaptureSlicingFixture;

namespace UnitTests.Analysis.LogicAnalysis.BuildSlicing;

/// <summary>
/// The gate view's owner-thread physics capture cut into slices of
/// <see cref="GateViewClassificationSnapshot.NodesPerSlice"/> nodes: the sliced capture holds
/// exactly the gates the one-slice capture does, and a rejected resumption ends it.
/// </summary>
public class GateViewClassificationSnapshotSlicingTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(GateViewClassificationSnapshot.NodesPerSlice, 0)]
    [InlineData(GateViewClassificationSnapshot.NodesPerSlice + 1, 1)]
    [InlineData(2 * GateViewClassificationSnapshot.NodesPerSlice + 6, 2)]
    public async Task CaptureAsync_YieldsBetweenSlices_AndCapturesTheSameGates(int gateCount, int expectedYields)
    {
        var graph = Graph(gateCount);
        var slices = new RecordingYield();

        var sliced = await GateViewClassificationSnapshot.CaptureAsync(graph, LogicSnapshotFixture.Wavelength, slices);

        slices.Yields.ShouldBe(expectedYields);
        Describe(sliced).ShouldBe(Describe(GateViewClassificationSnapshot.Capture(graph, LogicSnapshotFixture.Wavelength)));
    }

    [AvaloniaFact]
    public async Task CaptureAsync_OnDispatcher_ResumesEverySliceOnTheOwnerThread()
    {
        await ContinueInsideDispatcherJobAsync();
        var slices = new PoolCompletingYield();

        await GateViewClassificationSnapshot.CaptureAsync(
            Graph(3 * GateViewClassificationSnapshot.NodesPerSlice + 6), LogicSnapshotFixture.Wavelength, slices);

        slices.RequestedOnUiThread.ShouldBe([true, true, true], "every slice must read live state on the owner thread");
    }

    [Fact]
    public async Task CaptureAsync_ResumptionRejected_ThrowsCancellation()
    {
        var graph = Graph(GateViewClassificationSnapshot.NodesPerSlice + 1);

        await Should.ThrowAsync<OperationCanceledException>(() => GateViewClassificationSnapshot.CaptureAsync(
            graph, LogicSnapshotFixture.Wavelength, new RecordingYield(rejectAtYield: 1)));
    }

    private static GateViewGraph Graph(int gateCount) =>
        GateViewSnapshotFixture.Graph(GateChain(gateCount).Components.Cast<ComponentGroup>().ToArray());

    private static IEnumerable<string> Describe(GateViewClassificationSnapshot snapshot) =>
        snapshot.Gates.Select(gate => $"{gate.Id}|{gate.Fallback}|{gate.Physics?.ClassificationIdentity()}");
}
