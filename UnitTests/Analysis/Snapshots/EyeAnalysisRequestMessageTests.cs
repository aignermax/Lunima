using CAP_Core.Analysis.EyeDiagram;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

public sealed class EyeAnalysisRequestMessageTests
{
    [Theory]
    [InlineData("off", "No laser is switched on \u2014 turn the laser on at your input coupler.")]
    [InlineData("absent", "No light source found \u2014 place an input coupler (e.g. a grating/edge coupler).")]
    [InlineData("disconnected", "The circuit produced no output traces \u2014 connect an output path from the light source to a detector/output.")]
    public void ExpectedNoSignalMessages_PreserveLegacyText(string state, string expected)
    {
        var fixture = new SweepSnapshotFixture();
        if (state == "disconnected")
            foreach (var matrix in fixture.First.WaveLengthToSMatrixMap.Values) matrix.SetValues(new(), reset: true);
        var sources = state == "disconnected" ? new[] { new TransientSource(fixture.Input, 1) } : [];
        var outputs = state == "off" ? new[] { fixture.Output } : [];
        var request = new EyeAnalysisRequest(TransientCircuitSnapshot.Capture(fixture.Grid),
            new TransientExcitationSnapshot(sources, new(), outputs, -145), 100e9, PrbsOrder.Prbs7,
            0.5, null, false, new("designated", "noise", "multiple"));

        request.Solve().Error.ShouldBe(expected);
    }
}
