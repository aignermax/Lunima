using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The photonic zero flag against a hand-wired zero-detect network built the way the
/// shipped "Logic Gate Zero Detect 4-bit" example composes it: three OR gates in a
/// tree (OR01 reads A0/A1, OR23 reads A2/A3, ORALL combines them) feeding one pinned
/// NOT gate, whose output tap carries the flag name Z — so Z = NOT(A0 OR A1 OR A2 OR
/// A3), the <c>JZ</c> branch condition. Also covers the constructor validation: a
/// network that does not expose the expected signal names must throw, naming the
/// missing signal, and the emulator's default zero flag must stay the golden check.
/// </summary>
public class PhotonicZeroFlagTests
{
    [Fact]
    public void IsZero_All16Values_MatchTheGoldenFlag()
    {
        var photonic = new PhotonicZeroFlag(BuildZeroDetectNetwork());
        var golden = new GoldenIsaZeroFlag();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            photonic.IsZero(a).ShouldBe(golden.IsZero(a), $"A={a}");
        }
    }

    [Fact]
    public void Constructor_NetworkMissingOperandSignal_ThrowsNamingTheSignal()
    {
        var network = BuildZeroDetectNetwork(bit2Name: "Operand2");

        var exception = Should.Throw<ArgumentException>(() => new PhotonicZeroFlag(network));

        exception.Message.ShouldContain("A2");
    }

    [Fact]
    public void Constructor_NetworkMissingZeroTap_ThrowsNamingTheSignal()
    {
        var network = BuildZeroDetectNetwork(nameFlagTap: false);

        var exception = Should.Throw<ArgumentException>(() => new PhotonicZeroFlag(network));

        exception.Message.ShouldContain("Z");
    }

    [Fact]
    public void Accepts_ReportsWhetherTheNetworkExposesEverySignal()
    {
        PhotonicZeroFlag.Accepts(BuildZeroDetectNetwork()).ShouldBeTrue();
        PhotonicZeroFlag.Accepts(BuildZeroDetectNetwork(nameFlagTap: false)).ShouldBeFalse();
        PhotonicZeroFlag.Accepts(null).ShouldBeFalse();
    }

    [Fact]
    public void Emulator_JzOnPhotonicFlag_BranchesOnTheNetwork()
    {
        var takenProgram = new IsaAssembler().Assemble("LOAD 0\nJZ done\nLOAD 5\ndone: HALT");
        var taken = new IsaEmulator(takenProgram, zeroFlag: new PhotonicZeroFlag(BuildZeroDetectNetwork()));

        taken.Run(10);

        taken.ProgramCounter.ShouldBe(4, "JZ over a zero accumulator must branch to HALT");
        taken.IsHalted.ShouldBeTrue();

        var skippedProgram = new IsaAssembler().Assemble("LOAD 3\nJZ done\nLOAD 5\ndone: HALT");
        var skipped = new IsaEmulator(skippedProgram, zeroFlag: new PhotonicZeroFlag(BuildZeroDetectNetwork()));

        skipped.Run(10);

        skipped.Accumulator.ShouldBe(5, "JZ over a non-zero accumulator must fall through");
        skipped.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void Emulator_WithoutZeroFlag_KeepsTheGoldenBranchDecision()
    {
        var program = new IsaAssembler().Assemble("LOAD 0\nJZ done\nLOAD 5\ndone: HALT");
        var emulator = new IsaEmulator(program);

        emulator.Run(10);

        emulator.ProgramCounter.ShouldBe(4, "the default zero flag stays the golden ACC == 0 check");
        emulator.IsHalted.ShouldBeTrue();
    }

    /// <summary>
    /// Wires the zero-detect cascade: OR01 = A0 OR A1, OR23 = A2 OR A3,
    /// ORALL = OR01 OR OR23, Z = NOT ORALL. The operand bits are the network inputs
    /// A0–A3; the flag tap is named Z — the same signal names the shipped example
    /// persists.
    /// </summary>
    private static LogicNetworkEvaluator BuildZeroDetectNetwork(
        string bit2Name = "A2", bool nameFlagTap = true)
    {
        var inputs = new List<string> { "A0", "A1", bit2Name, "A3" };
        var gates = new Dictionary<string, LogicGateModel>
        {
            ["OR01"] = OrGate(),
            ["OR23"] = OrGate(),
            ["ORALL"] = OrGate(),
            ["NOTZ"] = PinnedGateTables.NotGate(),
        };
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>
        {
            [new LogicPinRef("OR01", "A")] = new LogicNetDriver.NetworkInput(inputs[0]),
            [new LogicPinRef("OR01", "B")] = new LogicNetDriver.NetworkInput(inputs[1]),
            [new LogicPinRef("OR23", "A")] = new LogicNetDriver.NetworkInput(inputs[2]),
            [new LogicPinRef("OR23", "B")] = new LogicNetDriver.NetworkInput(inputs[3]),
            [new LogicPinRef("ORALL", "A")] = new LogicNetDriver.GateOutput(new LogicPinRef("OR01", "Y")),
            [new LogicPinRef("ORALL", "B")] = new LogicNetDriver.GateOutput(new LogicPinRef("OR23", "Y")),
            [new LogicPinRef("NOTZ", "A")] = new LogicNetDriver.GateOutput(new LogicPinRef("ORALL", "Y")),
        };
        var taps = new Dictionary<string, LogicPinRef>
        {
            ["OR01.Y"] = new("OR01", "Y"),
            ["OR23.Y"] = new("OR23", "Y"),
            ["ORALL.Y"] = new("ORALL", "Y"),
        };
        if (nameFlagTap)
        {
            taps["Z"] = new LogicPinRef("NOTZ", "Y");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    /// <summary>
    /// The OR reading of the shipped OR/AND gate, rebuilt by value like
    /// <see cref="PinnedGateTables"/> does for the NAND/NOT gate: at threshold 0.25 a
    /// single active input already delivers half the power, so the rows read
    /// 0/1/1/1 with raw powers 0.0/0.5/0.5/1.0.
    /// </summary>
    private static LogicGateModel OrGate()
    {
        string[] inputNames = { "A", "B" };
        var rows = new[]
        {
            OrRow(inputNames, new[] { false, false }, false, 0.0),
            OrRow(inputNames, new[] { true, false }, true, 0.5),
            OrRow(inputNames, new[] { false, true }, true, 0.5),
            OrRow(inputNames, new[] { true, true }, true, 1.0),
        };
        return LogicGateModel.FromTruthTable(new TruthTable(
            "OR/AND Gate", inputNames, new[] { "Y" }, Array.Empty<string>(),
            0.25, PinnedGateTables.WavelengthNm, rows));
    }

    /// <summary>Builds one OR table row: the input bits per pin name plus the single Y output.</summary>
    private static TruthTableRow OrRow(string[] inputNames, bool[] bits, bool y, double power) =>
        new(
            inputNames.Select((name, i) => (name, bit: bits[i])).ToDictionary(pair => pair.name, pair => pair.bit),
            new Dictionary<string, LogicOutputValue> { ["Y"] = new(y, power) });
}
