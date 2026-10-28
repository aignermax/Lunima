using CAP_Core.Analysis.LogicAnalysis;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;

namespace UnitTests.Analysis.Snapshots;

public sealed class TruthTableSnapshotTests
{
    [Fact]
    public async Task Extraction_MaterializesConnectablePinsAndPreservesExistingPinIdentity()
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        group.PhysicalPins.ShouldBeEmpty();
        var extractor = new TruthTableExtractor();
        await extractor.ExtractAsync(group, ["a"], ["y"], 0.25, 1550);
        group.PhysicalPins.Select(pin => pin.Name).ShouldBe(group.ExternalPins.Select(pin => pin.Name));
        var firstPins = group.PhysicalPins.ToArray();
        await extractor.ExtractAsync(group, ["a"], ["y"], 0.25, 1550);
        for (var index = 0; index < firstPins.Length; index++)
            group.PhysicalPins[index].ShouldBeSameAs(firstPins[index]);
    }

    [Fact]
    public async Task WorkerHeld_RemovingNumericMatricesDoesNotChangeCapturedPower()
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        var extractor = new LogicSnapshotFixture.HeldExtractor();
        var running = extractor.ExtractAsync(group, ["a", "b"], ["y"], 0.25, 1550);
        var table = await extractor.Barrier.MutateThenFinishAsync(running,
            () => LogicSnapshotFixture.RemovePhysics(group));
        var expected = new[] { 0d, 0.5d, 0.5d, 1d };
        for (var row = 0; row < expected.Length; row++)
            table.Rows[row].Outputs["y"].Power.ShouldBe(expected[row], 0.001);
    }

    [Fact]
    public async Task WorkerHeld_PinNamesFlowIdsAndCallerRoleListsAreDetached()
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        var originalName = group.GroupName;
        var inputs = new List<string> { "a" };
        var outputs = new List<string> { "y" };
        var biases = new List<string> { "b" };
        var extractor = new LogicSnapshotFixture.HeldExtractor();
        var running = extractor.ExtractAsync(group, inputs, outputs, biases, 0.25, 1550);
        var table = await extractor.Barrier.MutateThenFinishAsync(running, () =>
        {
            inputs.Clear(); outputs.Clear(); biases.Clear();
            group.GroupName = "renamed while solving";
            foreach (var pin in group.ExternalPins)
            {
                pin.Name = "changed";
                pin.InternalPin!.LogicalPin!.IDInFlow = Guid.NewGuid();
                pin.InternalPin.LogicalPin.IDOutFlow = Guid.NewGuid();
            }
        });
        table.GroupName.ShouldBe(originalName);
        table.InputPinNames.ShouldBe(["a"]);
        table.OutputPinNames.ShouldBe(["y"]);
        table.BiasPinNames.ShouldBe(["b"]);
        table.Rows[0].Outputs["y"].Power.ShouldBe(0.5, 0.001);
        table.Rows[1].Outputs["y"].Power.ShouldBe(1, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerHeld_NestedFrozenPathsRemainPartOfCapturedInterference(bool mutate)
    {
        var mzi = LogicGateFixtureFactory.CreateBalancedMziGroup();
        var outer = new CAP_Core.Components.Core.ComponentGroup("outer");
        outer.AddChild(mzi);
        // Expose the same leaf endpoint identities through the outer interface.
        foreach (var pin in mzi.ExternalPins)
            outer.AddExternalPin(new CAP_Core.Components.Core.GroupPin { Name = pin.Name, InternalPin = pin.InternalPin! });
        var extractor = new LogicSnapshotFixture.HeldExtractor();
        var running = extractor.ExtractAsync(outer, ["in"], ["dark", "bright"], 0.25, 1550);
        var table = await extractor.Barrier.MutateThenFinishAsync(running, () =>
        {
            if (!mutate) return;
            foreach (var path in mzi.InternalPaths) path.Path.Segments.Clear();
            mzi.InternalPaths.Clear();
        });
        table.Rows[1].Outputs["dark"].Power.ShouldBeLessThan(0.001);
        table.Rows[1].Outputs["bright"].Power.ShouldBe(1, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedInput_PreservesCoherentBiasPhysics(bool bias)
    {
        var table = await new TruthTableExtractor().ExtractAsync(
            LogicGateFixtureFactory.CreateCombinerGroup(), ["a"], ["y"], bias ? ["b"] : [], 0.25, 1550);
        table.Rows[0].Outputs["y"].Power.ShouldBe(bias ? 0.5 : 0, 0.001);
        table.Rows[1].Outputs["y"].Power.ShouldBe(bias ? 1 : 0.5, 0.001);
    }

    [Fact]
    public async Task CancellationAfterCaptureRejectsSolvedResult()
    {
        using var stop = new CancellationTokenSource();
        var extractor = new LogicSnapshotFixture.HeldExtractor();
        var running = extractor.ExtractAsync(LogicGateFixtureFactory.CreateCombinerGroup(),
            ["a"], ["y"], 0.25, 1550, stop.Token);
        await Should.ThrowAsync<OperationCanceledException>(() =>
            extractor.Barrier.MutateThenFinishAsync(running, stop.Cancel));
    }
}
