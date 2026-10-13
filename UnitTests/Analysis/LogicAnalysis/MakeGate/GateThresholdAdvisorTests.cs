using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// Automatic threshold math of <see cref="GateThresholdAdvisor"/> and the expected
/// bits of <see cref="GateFunctionTable"/>: the shipped NAND levels land in the 0.125
/// region with a wide margin, the AND levels exactly on 0.25 with 3 dB, overlapping
/// levels are flagged, and a custom gate splits at the largest gap.
/// </summary>
public class GateThresholdAdvisorTests
{
    // Raw powers of the shipped NOT/NAND gate (LogicGateNotNandExampleTests), rows 00, 10, 01, 11.
    private static readonly double[] NandPowers = { 0.4886186104779054, 0.2443420631494358, 0.2443420631494358, 6.551582096650341E-05 };

    // Raw powers of the shipped AND-from-NAND gate: 0, 1/6, 1/6, 1/3.
    private static readonly double[] AndPowers = { 0.0, 1.0 / 6.0, 1.0 / 6.0, 1.0 / 3.0 };

    private readonly GateThresholdAdvisor _advisor = new();

    [Fact]
    public void Nand_ThresholdSitsInThe0125Region_WithWideMargin()
    {
        var suggestion = _advisor.Suggest(Samples(NandPowers, GateFunction.Nand));

        suggestion.LevelsOverlap.ShouldBeFalse();
        suggestion.Threshold.ShouldBe(0.122, 1e-9, "midway between the darkest 1 (0.244) and the brightest 0 (~0)");
        suggestion.Threshold.ShouldBeInRange(0.1, 0.15);
        suggestion.MarginDb.ShouldBe(35.7, 0.1);
    }

    [Fact]
    public void And_ThresholdIsQuarter_WithThreeDecibelMargin()
    {
        var suggestion = _advisor.Suggest(Samples(AndPowers, GateFunction.And));

        suggestion.LevelsOverlap.ShouldBeFalse();
        suggestion.Threshold.ShouldBe(0.25, 1e-12);
        suggestion.MarginDb.ShouldBe(10 * Math.Log10(2), 1e-9, "the darkest 1 is exactly twice the brightest 0");
    }

    [Fact]
    public void WrongFunction_LevelsOverlap_IsFlagged()
    {
        // The NAND powers read as AND: the expected-1 row (11) is the darkest of all.
        var suggestion = _advisor.Suggest(Samples(NandPowers, GateFunction.And));

        suggestion.LevelsOverlap.ShouldBeTrue();
        suggestion.MarginDb.ShouldBeLessThan(0);
    }

    [Fact]
    public void Custom_SplitsAtTheLargestGap()
    {
        var suggestion = _advisor.Suggest(Samples(NandPowers, GateFunction.Custom));

        suggestion.LevelsOverlap.ShouldBeFalse();
        suggestion.BrightestZero.ShouldBe(NandPowers[3]);
        suggestion.DarkestOne.ShouldBe(NandPowers[1]);
        suggestion.Threshold.ShouldBe(0.122, 1e-9);
    }

    [Fact]
    public void Custom_AllPowersEqual_Overlaps()
    {
        var suggestion = _advisor.Suggest(Samples(new[] { 0.3, 0.3, 0.3 }, GateFunction.Custom));

        suggestion.LevelsOverlap.ShouldBeTrue();
        suggestion.Threshold.ShouldBe(GateThresholdAdvisor.NeutralThreshold);
    }

    [Fact]
    public void PerfectlyDarkZero_CapsTheMargin()
    {
        var suggestion = _advisor.Suggest(Samples(new[] { 0.0, 0.5 }, GateFunction.Custom));

        suggestion.MarginDb.ShouldBe(GateThresholdAdvisor.MaxMarginDb);
        suggestion.Threshold.ShouldBe(0.25);
    }

    [Fact]
    public void TinyLevels_ThresholdStaysInsideTheOpenInterval()
    {
        var suggestion = _advisor.Suggest(Samples(new[] { 0.0, 0.0004 }, GateFunction.Custom));

        suggestion.Threshold.ShouldBeGreaterThan(0);
        suggestion.Threshold.ShouldBeLessThan(1);
    }

    [Theory]
    [InlineData(GateFunction.Nand, new[] { true, true, true, false })]
    [InlineData(GateFunction.And, new[] { false, false, false, true })]
    [InlineData(GateFunction.Or, new[] { false, true, true, true })]
    [InlineData(GateFunction.Nor, new[] { true, false, false, false })]
    [InlineData(GateFunction.Xor, new[] { false, true, true, false })]
    [InlineData(GateFunction.Not, new[] { true, false, true, false })]
    public void FunctionTable_TwoInputs(GateFunction function, bool[] expected)
    {
        Enumerable.Range(0, 4).Select(row => GateFunctionTable.Expected(function, row, 2))
            .ShouldBe(expected.Select(b => (bool?)b));
    }

    [Fact]
    public void FunctionTable_Custom_HasNoExpectation() =>
        GateFunctionTable.Expected(GateFunction.Custom, 1, 2).ShouldBeNull();

    private static List<GatePowerSample> Samples(double[] powers, GateFunction function) =>
        powers.Select((power, row) => new GatePowerSample(power, GateFunctionTable.Expected(function, row, 2))).ToList();
}
