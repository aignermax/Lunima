using Shouldly;
using Xunit;

namespace UnitTests.Regression;

/// <summary>
/// Unit tests for <see cref="GoldenComparer.Compare"/> — the tolerance and drift
/// checks that decide whether a golden gate run passes.
/// </summary>
public class GoldenComparerTests
{
    private static GoldenManifestEntry Entry() => new()
    {
        File = "demo.lun",
        StartNm = 1500,
        EndNm = 1600,
        StepCount = 2,
        Tolerance = 0.001,
        Inputs = { new GoldenInput { Pin = "in.pin" } },
        Outputs = { "out.pin" }
    };

    private static GoldenGateResult Run(double[] values) => new()
    {
        WavelengthsNm = new[] { 1500, 1600 },
        Transmission = { ["out.pin"] = values }
    };

    [Fact]
    public void Compare_ExactMatch_Passes()
    {
        var reference = new GoldenReference
        {
            WavelengthsNm = new[] { 1500, 1600 },
            Transmission = { ["out.pin"] = new[] { 0.5, 0.4 } }
        };

        GoldenComparer.Compare(Entry(), reference, Run(new[] { 0.5, 0.4 })).ShouldBeEmpty();
    }

    [Fact]
    public void Compare_DeviationBeyondTolerance_FailsWithPinAndWavelength()
    {
        var reference = new GoldenReference
        {
            WavelengthsNm = new[] { 1500, 1600 },
            Transmission = { ["out.pin"] = new[] { 0.5, 0.4 } }
        };

        var failures = GoldenComparer.Compare(Entry(), reference, Run(new[] { 0.5, 0.401 }));

        failures.Count.ShouldBe(1);
        failures[0].ShouldContain("out.pin");
        failures[0].ShouldContain("1600");
    }

    [Fact]
    public void Compare_WavelengthDrift_FailsPointingAtRegeneration()
    {
        var reference = new GoldenReference
        {
            WavelengthsNm = new[] { 1510, 1610 },
            Transmission = { ["out.pin"] = new[] { 0.5, 0.4 } }
        };

        var failures = GoldenComparer.Compare(Entry(), reference, Run(new[] { 0.5, 0.4 }));

        failures.Count.ShouldBe(1);
        failures[0].ShouldContain(GoldenManifest.UpdateSwitchName);
    }

    [Fact]
    public void Compare_MissingOutputInReference_Fails()
    {
        var reference = new GoldenReference
        {
            WavelengthsNm = new[] { 1500, 1600 },
            Transmission = { ["other.pin"] = new[] { 0.5, 0.4 } }
        };

        GoldenComparer.Compare(Entry(), reference, Run(new[] { 0.5, 0.4 }))
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void Compare_TruthRowMismatch_Fails()
    {
        var reference = new GoldenReference
        {
            WavelengthsNm = new[] { 1500, 1600 },
            Transmission = { ["out.pin"] = new[] { 0.5, 0.4 } },
            TruthTable = { new GoldenTruthRow
            {
                WavelengthNm = 1550,
                Inputs = { new GoldenInput { Pin = "in.pin", Power = 0 } },
                Expected = { ["logic.out"] = 0.0 }
            } }
        };
        var run = Run(new[] { 0.5, 0.4 });
        run.TruthActuals.Add(new Dictionary<string, double> { ["logic.out"] = 0.9 });

        var failures = GoldenComparer.Compare(Entry(), reference, run);

        failures.Count.ShouldBe(1);
        failures[0].ShouldContain("truth row 0");
        failures[0].ShouldContain("logic.out");
    }

    [Fact]
    public void Compare_TruthRowUnEvaluated_Fails()
    {
        var reference = new GoldenReference
        {
            WavelengthsNm = new[] { 1500, 1600 },
            Transmission = { ["out.pin"] = new[] { 0.5, 0.4 } },
            TruthTable = { new GoldenTruthRow { WavelengthNm = 1550 } }
        };

        var failures = GoldenComparer.Compare(Entry(), reference, Run(new[] { 0.5, 0.4 }));

        failures.Single().ShouldContain("not evaluated");
    }

    [Fact]
    public void Compare_ZeroExpected_DeviationWithinFloorTolerance_Passes()
    {
        var reference = new GoldenReference
        {
            WavelengthsNm = new[] { 1500, 1600 },
            Transmission = { ["out.pin"] = new[] { 0.0, 0.0 } }
        };

        // tolerance × 1e-6 absolute floor = 1e-9 abs threshold for zero expectation.
        GoldenComparer.Compare(Entry(), reference, Run(new[] { 0.0, 5e-10 })).ShouldBeEmpty();
    }

    [Fact]
    public void Compare_ZeroExpected_DeviationBeyondFloorTolerance_Fails()
    {
        var reference = new GoldenReference
        {
            WavelengthsNm = new[] { 1500, 1600 },
            Transmission = { ["out.pin"] = new[] { 0.0, 0.0 } }
        };

        GoldenComparer.Compare(Entry(), reference, Run(new[] { 0.0, 1e-6 }))
            .ShouldNotBeEmpty();
    }
}
