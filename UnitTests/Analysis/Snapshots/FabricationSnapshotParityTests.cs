using System.Numerics;
using CAP_Core.Analysis.MonteCarloAnalysis;
using CAP_Core.Analysis.MonteCarloAnalysis.FabricationVariance;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.LightCalculation;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class FabricationSnapshotParityTests
{
    [Fact]
    public void CapturedSpanAndMmiPinOrder_PreserveOriginalComplexPerturbation()
    {
        var component = new ResizableComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        component.Name = component.NazcaFunctionName = "mmi";
        var pins = component.GetAllPins();
        var key = (pins[0].IDInFlow, pins[1].IDOutFlow);
        var fixedMatrix = new FixedMatrix(pins, key);
        var snapshot = new FabricationSnapshot([component], ProcessTolerances.Default);
        var originalSource = new FabricationVarianceSource([component], ProcessTolerances.Default);
        originalSource.ApplyVariance(new GaussianSampler(42));
        var expected = new PerturbedSystemMatrixBuilder(fixedMatrix, originalSource)
            .GetSystemSMatrix(1550).GetNonNullValues()[key];

        component.Resize(7);
        component.Parts[0, 0].Pins.Reverse();
        var changed = new PerturbedSystemMatrixBuilder(fixedMatrix, originalSource)
            .GetSystemSMatrix(1550).GetNonNullValues()[key];
        changed.ShouldNotBe(expected, "span and MMI output order must affect the live numerical oracle");
        var state = snapshot.CreateRun(default);
        state.ApplyVariance(new GaussianSampler(42));
        var actual = state.Decorate(fixedMatrix).GetSystemSMatrix(1550).GetNonNullValues()[key];

        actual.Real.ShouldBe(expected.Real, 1e-12);
        actual.Imaginary.ShouldBe(expected.Imaginary, 1e-12);
    }

    [Fact]
    public void OrderedRepeatedParticipants_ConsumeEveryLocalDrawAndUseLastDeviation()
    {
        var first = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var second = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        Component[] participants = [first, second, first];
        var tolerances = new ProcessTolerances(3, 7);
        var captured = new FabricationSnapshot(participants, tolerances).CreateRun(default);
        var live = new FabricationVarianceSource(participants, tolerances);
        var liveSampler = new GaussianSampler(87);
        var capturedSampler = new GaussianSampler(87);
        var oracle = new GaussianSampler(87);
        for (var run = 0; run < 4; run++)
        {
            var waferWidth = oracle.NextGaussian() * 3;
            var waferThickness = oracle.NextGaussian() * 7;
            var expected = new Dictionary<Component, ComponentDeviation>();
            foreach (var participant in participants)
                expected[participant] = new(waferWidth + oracle.NextGaussian() * 0.2 * 3,
                    waferThickness + oracle.NextGaussian() * 0.2 * 7);
            live.ApplyVariance(liveSampler);
            captured.ApplyVariance(capturedSampler);
            for (var index = 0; index < participants.Length; index++)
            {
                live.CurrentDeviations![participants[index]].ShouldBe(expected[participants[index]]);
                captured.Deviations![captured.Participants[index]].ShouldBe(expected[participants[index]]);
            }
            captured.Participants[0].ShouldBeSameAs(captured.Participants[2]);
        }
        captured.RestoreNominal();
        captured.Deviations.ShouldBeNull();
    }

    [Fact]
    public async Task NestedGroupParticipantsAndFrozenRoute_MatchLegacyThenSurviveRemoval()
    {
        var fixture = new MonteCarloSnapshotFixture();
        var inner = new ComponentGroup("inner");
        inner.AddChild(fixture.Circuit.First);
        inner.AddChild(fixture.Circuit.Second);
        var frozen = new FrozenWaveguidePath { StartPin = fixture.Circuit.Wire.StartPin,
            EndPin = fixture.Circuit.Wire.EndPin, Path = SweepSnapshotFixture.Path(37) };
        inner.AddInternalPath(frozen);
        var outer = new ComponentGroup("outer");
        outer.AddChild(inner);
        var tiles = new CAP_Core.Grid.ComponentListTileManager();
        tiles.AddComponent(outer);
        fixture.Circuit.Grid.TileManager = tiles;
        fixture.Circuit.Grid.WaveguideConnections = new(new CAP_Core.Routing.WaveguideRouter())
            { EnableCoherentPropagationPhase = true };
        // Match the UI's preorder traversal including non-leaf groups. Their local Gaussian
        // draws still advance the stream even when they own no final intra-component transfer.
        fixture.Participants.Clear();
        fixture.Participants.AddRange([outer, inner, fixture.Circuit.First, fixture.Circuit.Second]);
        var captured = fixture.Capture();
        var expected = await fixture.LegacyAsync();
        inner.ChildComponents.Clear();
        inner.InternalPaths.Clear();
        frozen.Path.Segments.Clear();
        fixture.Participants.Reverse();

        MonteCarloSnapshotFixture.AssertEquivalent(
            await captured.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10)), expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingFlowOwners_KeepTheLastParticipant(bool groupLast)
    {
        var leaf = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var group = new ComponentGroup("mmi");
        group.AddChild(leaf);
        group.Parts[0, 0].Pins.Clear();
        group.Parts[0, 0].Pins.AddRange(leaf.GetAllPins());
        Component[] participants = groupLast ? [leaf, group] : [group, leaf];
        var pins = leaf.GetAllPins();
        var key = (pins[0].IDInFlow, pins[1].IDOutFlow);
        var matrix = new FixedMatrix(pins, key);
        var captured = new FabricationSnapshot(participants, ProcessTolerances.Default).CreateRun(default);
        var live = new FabricationVarianceSource(participants, ProcessTolerances.Default);
        live.ApplyVariance(new GaussianSampler(42));
        captured.ApplyVariance(new GaussianSampler(42));
        var expected = new PerturbedSystemMatrixBuilder(matrix, live).GetSystemSMatrix(1550).GetNonNullValues()[key];
        var actual = captured.Decorate(matrix).GetSystemSMatrix(1550).GetNonNullValues()[key];

        actual.Real.ShouldBe(expected.Real, 1e-12);
        actual.Imaginary.ShouldBe(expected.Imaginary, 1e-12);
    }

    private sealed class ResizableComponent(Component source) : Component(source.WaveLengthToSMatrixMap, [],
        source.NazcaFunctionName, source.NazcaFunctionParameters, source.Parts, source.TypeNumber,
        source.Name, source.Rotation90CounterClock, source.PhysicalPins)
    {
        internal void Resize(int span)
        {
            var original = Parts[0, 0];
            Parts = new Part[span, 1];
            Parts[0, 0] = original;
            for (var index = 1; index < span; index++) Parts[index, 0] = new Part([]);
        }
    }

    private sealed class FixedMatrix(List<Pin> pins, (Guid, Guid) key) : ISystemMatrixBuilder
    {
        public SMatrix GetSystemSMatrix(int wavelengthNm) => GetSystemSMatrix((double)wavelengthNm);
        public SMatrix GetSystemSMatrix(double wavelengthNm)
        {
            var matrix = new SMatrix(pins.SelectMany(pin => new[] { pin.IDInFlow, pin.IDOutFlow }).ToList(), new());
            matrix.SetValues(new() { [key] = new Complex(0.8, 0.1) });
            return matrix;
        }
    }
}
