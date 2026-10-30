using System.Numerics;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Parametric;
using CAP_Core.LightCalculation.MaterialDispersion;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class SweepSnapshotIsolationTests
{
    [Fact]
    public async Task NumericMatricesAndFlowIds_AreCapturedBeforeLiveDefinitionsAreRemoved()
    {
        var fixture = new SweepSnapshotFixture();
        var expected = await fixture.LegacyAsync();
        var snapshot = fixture.Capture();
        foreach (var component in new[] { fixture.First, fixture.Second })
        {
            foreach (var matrix in component.WaveLengthToSMatrixMap.Values) matrix.SetValues(new(), reset: true);
            component.WaveLengthToSMatrixMap.Clear();
            component.PhysicalPins.Clear();
        }
        SweepSnapshotFixture.AssertEquivalent(await snapshot.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10)), expected);
    }

    [Fact]
    public async Task SourcesAndInsertionLossReference_AreCapturedBeforePortEdits()
    {
        var fixture = new SweepSnapshotFixture();
        var expected = await fixture.LegacyAsync();
        var snapshot = fixture.Capture();
        fixture.Source.InFlowPower = new Complex(9, 0);
        fixture.Ports.Clear();
        SweepSnapshotFixture.AssertEquivalent(await snapshot.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10)), expected);
    }

    [Fact]
    public async Task RoutePhysicsAndEndpoints_AreCapturedBeforeLiveRouteReplacement()
    {
        var fixture = new SweepSnapshotFixture(coherent: true);
        fixture.Wire.DispersionModel = new PolynomialDispersion(1550, 2.4, 0.001, 0.00001);
        var expected = await fixture.LegacyAsync();
        var snapshot = fixture.Capture();
        fixture.Wire.PropagationLossDbPerCm = 1500;
        fixture.Wire.DispersionModel = new ConstantDispersion(1.1, lossDbPerCm: 1500);
        fixture.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(300));
        fixture.Wire.EndPin = fixture.First.PhysicalPins[0];
        fixture.Grid.WaveguideConnections.EnableCoherentPropagationPhase = false;
        SweepSnapshotFixture.AssertEquivalent(await snapshot.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10)), expected);
    }

    [Fact]
    public async Task NestedFrozenPathsAndInheritedMaterial_AreIndependentOfGroupEdits()
    {
        var fixture = new SweepSnapshotFixture(coherent: true);
        var inner = new ComponentGroup("inner");
        inner.AddChild(fixture.First);
        inner.AddChild(fixture.Second);
        var path = new FrozenWaveguidePath { StartPin = fixture.Wire.StartPin, EndPin = fixture.Wire.EndPin,
            Path = SweepSnapshotFixture.Path(37) };
        inner.AddInternalPath(path);
        var outer = new ComponentGroup("outer");
        outer.AddChild(inner);
        var tiles = new CAP_Core.Grid.ComponentListTileManager();
        tiles.AddComponent(outer);
        fixture.Grid.TileManager = tiles;
        fixture.Grid.WaveguideConnections = new(new CAP_Core.Routing.WaveguideRouter())
            { EnableCoherentPropagationPhase = true };
        fixture.First.WaveguideDispersion = new PolynomialDispersion(1550, 2.4, 0.001);
        var snapshot = fixture.Capture();
        var expected = await fixture.LegacyAsync();
        path.Path.Segments.Clear();
        fixture.First.WaveguideDispersion = new ConstantDispersion(1.0);
        inner.ChildComponents.Clear();
        inner.InternalPaths.Clear();
        SweepSnapshotFixture.AssertEquivalent(await snapshot.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10)), expected);
    }

    [Fact]
    public async Task ParametricDefinitions_RebuildPrivateClosuresForEachCapturedSliderValue()
    {
        var fixture = new SweepSnapshotFixture();
        var pins = fixture.First.PhysicalPins.Select(pin => pin.LogicalPin).ToList();
        var slider = new Slider(Guid.NewGuid(), 0, 0.25, 1, 0);
        fixture.First.AddSlider(0, slider);
        var matrix = ParametricSMatrixFactory.Build(pins, [slider], new(
            [new ParameterDefinition("gain", 0.25, 0, 1, sliderNumber: 0)],
            [new FormulaConnection(pins[0].Name, pins[1].Name, "gain", "0")]));
        fixture.First.WaveLengthToSMatrixMap = new() { [1550] = matrix };
        var firstExpected = await fixture.LegacyAsync();
        var first = fixture.Capture();
        slider.Value = 0.75;
        matrix.SliderReference[slider.ID] = 0.75;
        var secondExpected = await fixture.LegacyAsync();
        var second = fixture.Capture();
        matrix.ParametricRebuild = (_, _) => throw new InvalidOperationException("live factory escaped capture");
        foreach (var key in matrix.NonLinearConnections.Keys.ToArray())
            matrix.NonLinearConnections[key] = matrix.NonLinearConnections[key] with
                { CalcConnectionWeightAsync = _ => throw new InvalidOperationException("live formula escaped capture") };
        matrix.SliderReference[slider.ID] = 0.99;
        var results = await Task.WhenAll(first.SolveAsync(), second.SolveAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        SweepSnapshotFixture.AssertEquivalent(results[0], firstExpected);
        SweepSnapshotFixture.AssertEquivalent(results[1], secondExpected);
        matrix.SliderReference[slider.ID].ShouldBe(0.99);
    }

    [Fact]
    public async Task CoverageDiagnostics_UseCapturedDefinitionNames()
    {
        var fixture = new SweepSnapshotFixture();
        fixture.First.GdsFactoryFunction = "original.factory";
        var expected = await fixture.LegacyAsync();
        expected.Warnings.ShouldContain(warning => warning.Contains("original.factory"));
        var snapshot = fixture.Capture();
        fixture.First.GdsFactoryFunction = "edited.factory";
        SweepSnapshotFixture.AssertEquivalent(await snapshot.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10)), expected);
    }

    [Fact]
    public void UnknownMutableDispersion_RequiresADetachedDefinitionInsteadOfRetainingItsCallback()
    {
        var fixture = new SweepSnapshotFixture(coherent: true);
        fixture.Wire.DispersionModel = new MutableDispersion();
        Should.Throw<InvalidDataException>(() => fixture.Capture());
    }

    private sealed class MutableDispersion : IDispersionModel
    {
        public double NEffAt(double wavelengthNm) => 2.4;
        public double GroupIndexAt(double wavelengthNm) => 4;
        public double LossDbPerCmAt(double wavelengthNm) => 0.5;
    }
}
