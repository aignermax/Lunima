using Avalonia.Headless.XUnit;
using CAP_Core.Analysis.LogicAnalysis;
using Shouldly;
using UnitTests.Analysis.Snapshots;
using Xunit;
using static UnitTests.Analysis.LogicAnalysis.BuildSlicing.CaptureSlicingFixture;

namespace UnitTests.Analysis.LogicAnalysis.BuildSlicing;

/// <summary>
/// The Logic build's owner-thread capture cut into slices: the assembler yields after every
/// <see cref="LogicNetworkSnapshot.GatesPerSlice"/> gates and once before the topology, the
/// sliced capture assembles exactly the network the one-slice capture does, and a yield that
/// rejects its resumption (the design changed meanwhile) ends the build before any gate is solved.
/// </summary>
public class LogicNetworkSnapshotSlicingTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(LogicNetworkSnapshot.GatesPerSlice, 1)]
    [InlineData(LogicNetworkSnapshot.GatesPerSlice + 1, 2)]
    [InlineData(2 * LogicNetworkSnapshot.GatesPerSlice + 6, 3)]
    public async Task AssembleAsync_YieldsAfterEveryFullSliceOfGatesAndBeforeTopology(int gateCount, int expectedYields)
    {
        var (components, connections) = GateChain(gateCount);
        var slices = new RecordingYield();

        await new LogicNetworkAssembler().AssembleAsync(
            components, connections, LogicSnapshotFixture.Wavelength, slices);

        slices.Yields.ShouldBe(expectedYields);
    }

    [Fact]
    public async Task AssembleAsync_SlicedCapture_BuildsTheSameNetworkAsOneSlice()
    {
        var (components, connections) = GateChain(2 * LogicNetworkSnapshot.GatesPerSlice + 6);

        var oneSlice = await new LogicNetworkAssembler().AssembleAsync(
            components, connections, LogicSnapshotFixture.Wavelength);
        var sliced = await new LogicNetworkAssembler().AssembleAsync(
            components, connections, LogicSnapshotFixture.Wavelength, new RecordingYield());

        LogicBuildDescription.DescribeNetwork(sliced).ShouldBe(LogicBuildDescription.DescribeNetwork(oneSlice));
    }

    [AvaloniaFact]
    public async Task AssembleAsync_OnDispatcher_ResumesEverySliceOnTheOwnerThread()
    {
        await ContinueInsideDispatcherJobAsync();
        var (components, connections) = GateChain(2 * LogicNetworkSnapshot.GatesPerSlice + 6);
        var slices = new PoolCompletingYield();
        var assembler = new OwnerRecordingAssembler();

        await assembler.AssembleAsync(components, connections, LogicSnapshotFixture.Wavelength, slices);

        slices.RequestedOnUiThread.ShouldBe([true, true, true], "every slice must read live state on the owner thread");
        assembler.CapturedOnUiThread.ShouldBe(true, "the topology is live state too");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task AssembleAsync_ResumptionRejected_CancelsBeforeAnyGateIsSolved(int rejectedYield)
    {
        var (components, connections) = GateChain(2 * LogicNetworkSnapshot.GatesPerSlice + 6);
        var extractor = new CountingExtractor();

        await Should.ThrowAsync<OperationCanceledException>(() => new LogicNetworkAssembler(extractor)
            .AssembleAsync(components, connections, LogicSnapshotFixture.Wavelength, new RecordingYield(rejectedYield)));

        extractor.Solves.ShouldBe(0, "a capture that saw the design change must never reach the worker");
    }

    [Fact]
    public async Task AssembleAsync_DesignWithoutGates_StillReportsTheReadableError()
    {
        var error = await Should.ThrowAsync<InvalidOperationException>(() => new LogicNetworkAssembler()
            .AssembleAsync([], [], LogicSnapshotFixture.Wavelength, new RecordingYield()));

        error.Message.ShouldContain("no logic gate");
    }
}
