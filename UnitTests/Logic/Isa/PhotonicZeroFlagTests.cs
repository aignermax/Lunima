using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The photonic zero flag against a hand-wired zero-detect network built from the
/// pinned NAND gates (OR(a, b) = NAND(NAND(a, a), NAND(b, b)); Z = NOT(any A bit)) —
/// the same function the shipped "Logic Gate Zero Detect 4-bit" example has, so the
/// flag's bit driving and tap reading are pinned without the heavy example fixture.
/// Also covers the constructor validation and the <see cref="IsaEmulator"/> JZ seam:
/// default behaviour unchanged, a custom provider decides the branch.
/// </summary>
public class PhotonicZeroFlagTests
{
    [Fact]
    public void IsZero_All16Words_TrueExactlyForZero()
    {
        var flag = new PhotonicZeroFlag(BuildZeroDetectNetwork());
        for (var value = 0; value <= IsaMachine.MaxDataValue; value++)
        {
            flag.IsZero(value).ShouldBe(value == 0, $"A={value}");
        }
    }

    [Fact]
    public void IsZero_IgnoresBitsAboveTheDataWord()
    {
        // The emulator only ever passes 0–15; higher bits must not leak into the flag.
        new PhotonicZeroFlag(BuildZeroDetectNetwork()).IsZero(1 << IsaMachine.DataBits).ShouldBeTrue();
    }

    [Fact]
    public void Constructor_NetworkMissingInputSignal_ThrowsNamingTheSignal()
    {
        var network = BuildZeroDetectNetwork(bit2Name: "Operand2");

        var exception = Should.Throw<ArgumentException>(() => new PhotonicZeroFlag(network));

        exception.Message.ShouldContain("A2");
    }

    [Fact]
    public void Constructor_NetworkMissingFlagTap_ThrowsNamingTheSignal()
    {
        var network = BuildZeroDetectNetwork(tapZ: false);

        var exception = Should.Throw<ArgumentException>(() => new PhotonicZeroFlag(network));

        exception.Message.ShouldContain("Z");
    }

    [Fact]
    public void Constructor_WrongInputSignalCount_Throws()
    {
        Should.Throw<ArgumentException>(() =>
                new PhotonicZeroFlag(BuildZeroDetectNetwork(), new[] { "A0", "A1" }))
            .Message.ShouldContain("4");
    }

    [Fact]
    public void Accepts_ReportsWhetherTheNetworkExposesEverySignal()
    {
        PhotonicZeroFlag.Accepts(BuildZeroDetectNetwork()).ShouldBeTrue();
        PhotonicZeroFlag.Accepts(BuildZeroDetectNetwork(tapZ: false)).ShouldBeFalse();
        PhotonicZeroFlag.Accepts(null).ShouldBeFalse();
    }

    [Fact]
    public void Emulator_JzWithoutProvider_KeepsTheGoldenBehaviour()
    {
        var program = new IsaAssembler().Assemble("LOAD 0\nJZ 4\nLOAD 7\nHALT\nLOAD 5\nHALT");
        var emulator = new IsaEmulator(program);

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(5, "JZ on ACC == 0 must branch to ROM word 4");
        emulator.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void Emulator_JzWithPhotonicFlag_BranchesOnTheNetworkAndCountsConsultations()
    {
        var flag = new PhotonicZeroFlag(BuildZeroDetectNetwork());
        // Loop: JZ not taken once (ACC = 3), then taken after ACC is reloaded to 0.
        var program = new IsaAssembler().Assemble("LOAD 3\nJZ 5\nLOAD 0\nJZ 5\nHALT\nLOAD 9\nHALT");
        var emulator = new IsaEmulator(program, zeroFlag: flag.IsZero);

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(9, "the second JZ — ACC == 0 — must branch on light");
        emulator.IsHalted.ShouldBeTrue();
        flag.EvaluationCount.ShouldBe(2, "exactly the two executed JZ instructions consulted the flag");
    }

    /// <summary>
    /// Wires the 4-bit zero detect from the pinned NAND gates: a two-stage OR tree
    /// (OR0 = A0 OR A1, OR1 = A2 OR A3, OR2 = OR0 OR OR1) whose output a final NAND
    /// inverts into the flag tap Z. The input names are A0–A3.
    /// </summary>
    private static LogicNetworkEvaluator BuildZeroDetectNetwork(
        string bit2Name = "A2", bool tapZ = true)
    {
        var inputs = new List<string> { "A0", "A1", bit2Name, "A3" };
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();

        LogicNetDriver In(int bit) => new LogicNetDriver.NetworkInput(inputs[bit]);
        LogicNetDriver Out(string gateId) => new LogicNetDriver.GateOutput(new LogicPinRef(gateId, "Y"));

        // OR from three NANDs: OR(a, b) = NAND(NAND(a, a), NAND(b, b)).
        LogicPinRef AddOr(string stage, LogicNetDriver a, LogicNetDriver b)
        {
            gates[$"{stage}NA"] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef($"{stage}NA", "A")] = a;
            wiring[new LogicPinRef($"{stage}NA", "B")] = a;
            gates[$"{stage}NB"] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef($"{stage}NB", "A")] = b;
            wiring[new LogicPinRef($"{stage}NB", "B")] = b;
            gates[$"{stage}OR"] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef($"{stage}OR", "A")] = Out($"{stage}NA");
            wiring[new LogicPinRef($"{stage}OR", "B")] = Out($"{stage}NB");
            return new LogicPinRef($"{stage}OR", "Y");
        }

        var low = AddOr("L", In(0), In(1));
        var high = AddOr("H", In(2), In(3));
        var any = AddOr("W", new LogicNetDriver.GateOutput(low), new LogicNetDriver.GateOutput(high));

        // The final inversion: NOT(w) = NAND(w, w).
        gates["ZN"] = PinnedGateTables.NandGate();
        wiring[new LogicPinRef("ZN", "A")] = new LogicNetDriver.GateOutput(any);
        wiring[new LogicPinRef("ZN", "B")] = new LogicNetDriver.GateOutput(any);
        if (tapZ)
        {
            taps["Z"] = new LogicPinRef("ZN", "Y");
        }
        else
        {
            // The evaluator requires at least one output — tap the tree under another name.
            taps["W"] = new LogicPinRef("ZN", "Y");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }
}
