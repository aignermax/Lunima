using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The photonic NOT unit against a hand-wired 4-slice NOT network built from the
/// pinned NAND gates (one NAND per bit with both inputs tied to the operand bit,
/// NAND(a, a) = NOT a) — the same function the shipped "Logic Gate NOT 4-bit"
/// example has, so the ALU's bit driving and result reading are pinned without the
/// heavy example fixture. Also covers the constructor validation: a network that
/// does not expose the expected signal names must throw, naming the missing signal.
/// </summary>
public class PhotonicNotAluTests
{
    [Fact]
    public void Not_All16Inputs_MatchTheGoldenAlu()
    {
        var photonic = new PhotonicNotAlu(BuildNandNotNetwork());
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            photonic.Not(a).ShouldBe(golden.Not(a), $"A={a}");
        }
    }

    [Fact]
    public void Constructor_NetworkMissingResultSignal_ThrowsNamingTheSignal()
    {
        var network = BuildNandNotNetwork(tapY2: false);

        var exception = Should.Throw<ArgumentException>(() => new PhotonicNotAlu(network));

        exception.Message.ShouldContain("Y2");
    }

    [Fact]
    public void Constructor_NetworkMissingInputSignal_ThrowsNamingTheSignal()
    {
        var network = BuildNandNotNetwork(bit3Name: "Operand3");

        var exception = Should.Throw<ArgumentException>(() => new PhotonicNotAlu(network));

        exception.Message.ShouldContain("A3");
    }

    [Fact]
    public void Accepts_ReportsWhetherTheNetworkExposesEverySignal()
    {
        PhotonicNotAlu.Accepts(BuildNandNotNetwork()).ShouldBeTrue();
        PhotonicNotAlu.Accepts(BuildNandNotNetwork(tapY2: false)).ShouldBeFalse();
        PhotonicNotAlu.Accepts(null).ShouldBeFalse();
    }

    [Fact]
    public void Emulator_NotOnPhotonicAlu_InvertsModulo16()
    {
        var program = new IsaAssembler().Assemble("LOAD 5\nNOT\nSTORE 0\nHALT");
        var emulator = new IsaEmulator(program, new PhotonicNotAlu(BuildNandNotNetwork()));

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(10);
        emulator.Ram[0].ShouldBe(10);
        emulator.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void CompositeIsaAlu_RoutesEachOperationToItsOwnUnit()
    {
        var addUnit = new RecordingAlu();
        var notUnit = new PhotonicNotAlu(BuildNandNotNetwork());
        var composite = new CompositeIsaAlu(addUnit, notUnit);

        composite.Add(9, 9);
        composite.Not(5).ShouldBe(10);

        addUnit.AddCalls.ShouldBe(1, "ADD must go to the ADD unit");
        addUnit.NotCalls.ShouldBe(0, "NOT must go to the NOT unit");
    }

    /// <summary>Counts the calls each operation receives, to pin the composite routing.</summary>
    private sealed class RecordingAlu : IIsaAlu
    {
        public int AddCalls { get; private set; }
        public int NotCalls { get; private set; }

        public int Add(int a, int b)
        {
            AddCalls++;
            return new GoldenIsaAlu().Add(a, b);
        }

        public int And(int a, int b) => new GoldenIsaAlu().And(a, b);

        public int Not(int a)
        {
            NotCalls++;
            return new GoldenIsaAlu().Not(a);
        }
    }

    /// <summary>
    /// Wires a 4-bit NOT from the pinned NAND gates: one NAND per bit with both
    /// inputs driven by the operand bit. The result taps are named Y0–Y3.
    /// </summary>
    private static LogicNetworkEvaluator BuildNandNotNetwork(
        string bit3Name = "A3", bool tapY2 = true)
    {
        var inputs = new List<string> { "A0", "A1", "A2", bit3Name };
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();

        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            var gateId = $"N{bit}";
            LogicNetDriver input = new LogicNetDriver.NetworkInput(inputs[bit]);
            gates[gateId] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef(gateId, "A")] = input;
            wiring[new LogicPinRef(gateId, "B")] = input;
            if (bit != 2 || tapY2)
            {
                taps[$"Y{bit}"] = new LogicPinRef(gateId, "Y");
            }
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }
}
