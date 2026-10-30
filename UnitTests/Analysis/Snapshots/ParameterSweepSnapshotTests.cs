using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Grid.FormulaReading;
using CAP_Core.LightCalculation;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class ParameterSweepSnapshotTests
{
    [Theory]
    [InlineData(1550)]
    [InlineData(650)]
    public async Task CapturedTrials_MatchLegacyFieldsAndLeaveLaterLiveEditsAlone(int wavelength)
    {
        var fixture = new SweepSnapshotFixture();
        var slider = new Slider(Guid.NewGuid(), 0, 0.5, 1, 0);
        fixture.First.AddSlider(0, slider);
        var from = fixture.First.PhysicalPins[0].LogicalPin;
        var to = fixture.First.PhysicalPins[1].LogicalPin;
        foreach (var matrix in fixture.First.WaveLengthToSMatrixMap.Values.Distinct())
            matrix.NonLinearConnections[(from.IDInFlow, to.IDOutFlow)] = MathExpressionReader.ConvertToDelegate(
                "ToComplex(SLIDER0, 0)", new Dictionary<string, Guid>(), new Dictionary<string, Guid> { ["SLIDER0"] = slider.ID });
        var config = new SweepConfiguration(new SweepParameter(fixture.First, 0, "Coupling"), 0.2, 0.8, 3, wavelength);
        var expected = await new ParameterSweeper(new GridLightCalculator(new SystemMatrixBuilder(fixture.Grid), fixture.Grid))
            .RunSweepAsync(config);
        var snapshot = ParameterSweepSnapshot.Capture(config, fixture.Grid, fixture.Ports);
        slider.Value = 0.91;
        fixture.First.WaveLengthToSMatrixMap.Clear();
        fixture.Wire.RestoreCachedPath(SweepSnapshotFixture.Path(900));
        fixture.Source.InFlowPower = new System.Numerics.Complex(0.1, 0);

        var actual = await snapshot.SolveAsync(CancellationToken.None);

        slider.Value.ShouldBe(0.91);
        actual.Count.ShouldBe(expected.DataPoints.Count);
        for (var index = 0; index < actual.Count; index++)
        {
            actual[index].ParameterValue.ShouldBe(expected.DataPoints[index].ParameterValue);
            actual[index].OutputPowers.Keys.ShouldBe(expected.DataPoints[index].OutputPowers.Keys);
            foreach (var (pin, power) in expected.DataPoints[index].OutputPowers)
                actual[index].OutputPowers[pin].ShouldBe(power, 1e-9);
        }
    }

    [Fact]
    public async Task PreCanceledCaptureSolve_DoesNotReportAnEmptySuccessfulSweep()
    {
        var fixture = new SweepSnapshotFixture();
        fixture.First.AddSlider(0, new Slider(Guid.NewGuid(), 0, 0.5, 1, 0));
        var config = new SweepConfiguration(new SweepParameter(fixture.First, 0, "Coupling"), 0, 1, 3, 1550);
        var snapshot = ParameterSweepSnapshot.Capture(config, fixture.Grid, fixture.Ports);
        await Should.ThrowAsync<OperationCanceledException>(() => snapshot.SolveAsync(new CancellationToken(true)));
    }
}
