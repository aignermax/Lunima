using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Fan-out honesty proof for the shipped <c>examples/Logic Gate Logic Unit 4-bit.lun</c>
/// (issue #1286, quantitative level report of #1011/#1018): every operand bit A0–A3 feeds
/// two gate inputs — its bit's AND slice and NOT slice — so the four A signals are the
/// network's only fan-out sites, each with exactly two loads. The ideal 1×2 split hands
/// every branch 0.5 of the input power (−3.01 dB), which stays above both receiving
/// thresholds (AND 0.25, NOT 0.375): no branch is flagged below its gate threshold, so
/// the shared operand word is physically buildable as designed — a physical build needs
/// one 1×2 splitter per A bit and nothing else. The B inputs feed a single slice each
/// and never split.
/// </summary>
public class LogicGateLogicUnit4BitFanOutLevelTests
    : IClassFixture<LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture>
{
    private const double FullInputPower = 1.0;
    private const int LoadsPerSharedOperandBit = 2;
    private const double AndThreshold = 0.25;
    private const double NotThreshold = 0.375;

    private readonly LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture _fixture;

    /// <summary>Attaches the shared loaded-and-assembled logic unit.</summary>
    public LogicGateLogicUnit4BitFanOutLevelTests(
        LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void AssembledNetwork_OnlyTheSharedOperandBits_FanOut()
    {
        var warnings = _fixture.Network.FanOutWarnings;

        warnings.Count.ShouldBe(4,
            "only A0–A3 are shared between two slices — the B inputs feed one AND slice each");
        warnings.Select(w => w.DriverDisplayName).ShouldBe(
            new[] { "A0", "A1", "A2", "A3" }, ignoreOrder: true);
        warnings.ShouldAllBe(w => w.IsNetworkInputSignal,
            "every fan-out site is a shared network-input signal (#1025/#1034)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AssembledNetwork_SharedOperandBit_SplitsIdeally_AndStaysAboveBothThresholds(int bit)
    {
        var warning = _fixture.Network.FanOutWarnings.Single(w => w.DriverDisplayName == $"A{bit}");

        warning.LoadCount.ShouldBe(LoadsPerSharedOperandBit,
            $"A{bit} drives exactly its bit's AND slice and NOT slice");
        warning.LoadNames.ShouldBe(new[] { $"AND{bit}.A", $"NOT{bit}.A" }, ignoreOrder: true);
        warning.Levels.DriverPowerOne.ShouldBe(FullInputPower,
            "a network-input signal is driven by one source at the full input power");
        warning.Levels.BranchPower.ShouldBe(FullInputPower / LoadsPerSharedOperandBit, 1e-12);
        warning.Levels.SplitLossDb.ShouldBe(10 * Math.Log10(LoadsPerSharedOperandBit), 1e-12);

        var andBranch = warning.Levels.Branches.Single(b => b.LoadName == $"AND{bit}.A");
        andBranch.Threshold.ShouldBe(AndThreshold);
        andBranch.ReadsAsOne.ShouldBeTrue(
            $"0.5 ≥ {AndThreshold} — the AND slice still reads the split operand bit as 1");

        var notBranch = warning.Levels.Branches.Single(b => b.LoadName == $"NOT{bit}.A");
        notBranch.Threshold.ShouldBe(NotThreshold);
        notBranch.ReadsAsOne.ShouldBeTrue(
            $"0.5 ≥ {NotThreshold} — the NOT slice still reads the split operand bit as 1: " +
            "no branch is flagged below its gate threshold, so the design stays honest");
    }
}
