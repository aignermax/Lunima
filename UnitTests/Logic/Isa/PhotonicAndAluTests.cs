using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The photonic AND unit against a hand-wired 4-slice AND network built from the
/// pinned NAND gates (two NANDs per bit: AND(a, b) = NAND(NAND(a, b), NAND(a, b))) —
/// the same function the shipped "Logic Gate AND 4-bit" example has, so the ALU's bit
/// driving and result reading are pinned without the heavy example fixture. Also
/// covers the constructor validation: a network that does not expose the expected
/// signal names must throw, naming the missing signal.
/// </summary>
public class PhotonicAndAluTests
{
    [Fact]
    public void And_All256OperandPairs_MatchTheGoldenAlu()
    {
        var photonic = new PhotonicAndAlu(BuildNandAndNetwork());
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            photonic.And(a, b).ShouldBe(golden.And(a, b), $"A={a}, B={b}");
        }
    }

    [Fact]
    public void Constructor_NetworkMissingOperandSignal_ThrowsNamingTheSignal()
    {
        var network = BuildNandAndNetwork(bit2Name: "Operand2");

        var exception = Should.Throw<ArgumentException>(() => new PhotonicAndAlu(network));

        exception.Message.ShouldContain("B2");
    }

    [Fact]
    public void Constructor_NetworkMissingResultSignal_ThrowsNamingTheSignal()
    {
        var network = BuildNandAndNetwork(tapY1: false);

        var exception = Should.Throw<ArgumentException>(() => new PhotonicAndAlu(network));

        exception.Message.ShouldContain("Y1");
    }

    [Fact]
    public void Accepts_ReportsWhetherTheNetworkExposesEverySignal()
    {
        PhotonicAndAlu.Accepts(BuildNandAndNetwork()).ShouldBeTrue();
        PhotonicAndAlu.Accepts(BuildNandAndNetwork(tapY1: false)).ShouldBeFalse();
        PhotonicAndAlu.Accepts(null).ShouldBeFalse();
    }

    [Fact]
    public void AddAndNot_FallBackToTheGoldenAlu()
    {
        var photonic = new PhotonicAndAlu(BuildNandAndNetwork());
        var golden = new GoldenIsaAlu();

        photonic.Add(9, 9).ShouldBe(golden.Add(9, 9));
        photonic.Not(5).ShouldBe(golden.Not(5));
    }

    [Fact]
    public void PhotonicAdderAndNotAlu_AndFallsBackToTheGoldenAlu()
    {
        // The other photonic units are not AND units: their AND must stay golden,
        // so a partial photonic ALU still computes the ISA semantics.
        new PhotonicNotAlu(BuildNandNotNetwork()).And(12, 10).ShouldBe(8);
    }

    [Fact]
    public void Emulator_AndOnPhotonicAlu_ComputesTheBitwiseAnd()
    {
        var program = new IsaAssembler().Assemble("LOAD 12\nSTORE 1\nLOAD 10\nAND 1\nSTORE 0\nHALT");
        var emulator = new IsaEmulator(program, new PhotonicAndAlu(BuildNandAndNetwork()));

        emulator.Run(20);

        emulator.Accumulator.ShouldBe(8);
        emulator.Ram[0].ShouldBe(8);
        emulator.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void CompositeIsaAlu_ThreeUnits_RoutesEachOperationToItsOwnUnit()
    {
        var addUnit = new RecordingAlu();
        var notUnit = new RecordingAlu();
        var andUnit = new PhotonicAndAlu(BuildNandAndNetwork());
        var composite = new CompositeIsaAlu(addUnit, notUnit, andUnit);

        composite.Add(9, 9);
        composite.Not(5);
        composite.And(12, 10).ShouldBe(8);

        addUnit.AddCalls.ShouldBe(1, "ADD must go to the ADD unit");
        notUnit.NotCalls.ShouldBe(1, "NOT must go to the NOT unit");
        addUnit.AndCalls.ShouldBe(0, "AND must go to the AND unit");
        notUnit.AndCalls.ShouldBe(0, "AND must go to the AND unit");
    }

    [Fact]
    public void CompositeIsaAlu_TwoUnits_AndFallsBackToTheGoldenAlu()
    {
        var composite = new CompositeIsaAlu(new RecordingAlu(), new RecordingAlu());

        composite.And(12, 10).ShouldBe(new GoldenIsaAlu().And(12, 10));
    }

    /// <summary>Counts the calls each operation receives, to pin the composite routing.</summary>
    private sealed class RecordingAlu : IIsaAlu
    {
        public int AddCalls { get; private set; }
        public int AndCalls { get; private set; }
        public int NotCalls { get; private set; }

        public int Add(int a, int b)
        {
            AddCalls++;
            return new GoldenIsaAlu().Add(a, b);
        }

        public int And(int a, int b)
        {
            AndCalls++;
            return new GoldenIsaAlu().And(a, b);
        }

        public int Not(int a)
        {
            NotCalls++;
            return new GoldenIsaAlu().Not(a);
        }
    }

    /// <summary>
    /// Wires a 4-bit AND from the pinned NAND gates: two NANDs per bit,
    /// AND(a, b) = NAND(NAND(a, b), NAND(a, b)). The operand bits are the network
    /// inputs A0–A3 and B0–B3; the result taps are named Y0–Y3.
    /// </summary>
    private static LogicNetworkEvaluator BuildNandAndNetwork(
        string bit2Name = "B2", bool tapY1 = true)
    {
        var inputs = new List<string> { "A0", "A1", "A2", "A3", "B0", "B1", bit2Name, "B3" };
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();

        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            LogicNetDriver a = new LogicNetDriver.NetworkInput($"A{bit}");
            LogicNetDriver b = new LogicNetDriver.NetworkInput(inputs[IsaMachine.DataBits + bit]);
            var nandId = $"N{bit}";
            var andId = $"M{bit}";
            gates[nandId] = PinnedGateTables.NandGate();
            gates[andId] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef(nandId, "A")] = a;
            wiring[new LogicPinRef(nandId, "B")] = b;
            LogicNetDriver nandOut = new LogicNetDriver.GateOutput(new LogicPinRef(nandId, "Y"));
            wiring[new LogicPinRef(andId, "A")] = nandOut;
            wiring[new LogicPinRef(andId, "B")] = nandOut;
            if (bit != 1 || tapY1)
            {
                taps[$"Y{bit}"] = new LogicPinRef(andId, "Y");
            }
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    /// <summary>
    /// Wires a 4-bit NOT from the pinned NAND gates (one NAND per bit with both
    /// inputs tied to the operand bit) — the fixture the photonic NOT unit accepts.
    /// </summary>
    private static LogicNetworkEvaluator BuildNandNotNetwork()
    {
        var inputs = new List<string> { "A0", "A1", "A2", "A3" };
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
            taps[$"Y{bit}"] = new LogicPinRef(gateId, "Y");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }
}
