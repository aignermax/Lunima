using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The photonic ALU against a hand-wired 4-bit ripple-carry adder built from the
/// pinned NAND gates (nine per full-adder stage, carry rippling stage to stage) —
/// the same structure the shipped "Logic Gate 4-Bit Adder" example has, so the
/// ALU's bit driving and sum reading are pinned without the heavy example fixture.
/// Also covers the constructor validation: a network that does not expose the
/// expected signal names must throw, naming the missing signal.
/// </summary>
public class PhotonicAdderAluTests
{
    [Fact]
    public void Add_All256OperandPairs_MatchTheGoldenAlu()
    {
        var photonic = new PhotonicAdderAlu(BuildNandAdderNetwork());
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            photonic.Add(a, b).ShouldBe(golden.Add(a, b), $"A={a}, B={b}");
        }
    }

    [Fact]
    public void Constructor_NetworkMissingSumSignal_ThrowsNamingTheSignal()
    {
        var network = BuildNandAdderNetwork(tapS3: false);

        var exception = Should.Throw<ArgumentException>(() => new PhotonicAdderAlu(network));

        exception.Message.ShouldContain("S3");
    }

    [Fact]
    public void Constructor_NetworkMissingInputSignal_ThrowsNamingTheSignal()
    {
        var network = BuildNandAdderNetwork(carryInName: "CarryIn");

        var exception = Should.Throw<ArgumentException>(() => new PhotonicAdderAlu(network));

        exception.Message.ShouldContain("Cin");
    }

    [Fact]
    public void Emulator_AddOnPhotonicAlu_WrapsModulo16()
    {
        var program = new IsaAssembler().Assemble("LOAD 15\nSTORE 0\nLOAD 1\nADD 0\nHALT");
        var emulator = new IsaEmulator(program, new PhotonicAdderAlu(BuildNandAdderNetwork()));

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(0);
        emulator.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void Add_RecordsOperandsSumAndLightTravelTime()
    {
        var alu = new PhotonicAdderAlu(BuildNandAdderNetwork(gateDelayPicoseconds: 5));

        alu.Add(3, 5);

        var trace = alu.LastAddTrace.ShouldNotBeNull();
        trace.A.ShouldBe(3);
        trace.B.ShouldBe(5);
        trace.Sum.ShouldBe(8);
        trace.LightTravelPicoseconds.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Add_LongerCarryChain_TakesMoreLightTravelTime()
    {
        var shortChain = new PhotonicAdderAlu(BuildNandAdderNetwork(gateDelayPicoseconds: 5));
        var longChain = new PhotonicAdderAlu(BuildNandAdderNetwork(gateDelayPicoseconds: 5));

        shortChain.Add(1, 1);
        longChain.Add(7, 1);

        var shortDelay = shortChain.LastAddTrace.ShouldNotBeNull().LightTravelPicoseconds;
        var longDelay = longChain.LastAddTrace.ShouldNotBeNull().LightTravelPicoseconds;
        longDelay.ShouldBeGreaterThan(shortDelay,
            "7 + 1 ripples the carry through every stage from power-on, 1 + 1 only through the first");
    }

    [Fact]
    public void Add_SameOperandsTwice_SecondAddTravelsNoFurther()
    {
        var alu = new PhotonicAdderAlu(BuildNandAdderNetwork(gateDelayPicoseconds: 5));

        alu.Add(3, 5);
        alu.Add(3, 5);

        alu.LastAddTrace.ShouldNotBeNull().LightTravelPicoseconds.ShouldBe(0,
            "the inputs did not change, so no output switches — the trace reports the per-input " +
            "light travel, not the worst-case critical path");
    }

    /// <summary>
    /// Wires a 4-bit ripple-carry adder from the pinned NAND gates: per stage
    /// p = NAND(a, b), x = a XOR b (four NANDs around p), the sum s = x XOR cin
    /// (four more) and cout = NAND(p, NAND(x, cin)) = ab | x·cin, with the carry
    /// rippling into the next stage. Every gate gets <paramref name="gateDelayPicoseconds"/>
    /// propagation delay so the light-travel trace has real times to walk.
    /// </summary>
    private static LogicNetworkEvaluator BuildNandAdderNetwork(
        string carryInName = "Cin", bool tapS3 = true, double gateDelayPicoseconds = 0)
    {
        var inputs = new List<string> { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3", carryInName };
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();
        var gateDelays = new Dictionary<string, double>();

        void Nand(string id, LogicNetDriver inA, LogicNetDriver inB)
        {
            gates[id] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef(id, "A")] = inA;
            wiring[new LogicPinRef(id, "B")] = inB;
            gateDelays[id] = gateDelayPicoseconds;
        }

        LogicNetDriver Out(string id) => new LogicNetDriver.GateOutput(new LogicPinRef(id, "Y"));

        LogicNetDriver carry = new LogicNetDriver.NetworkInput(carryInName);
        for (var stage = 0; stage < IsaMachine.DataBits; stage++)
        {
            var p = $"T{stage}";
            LogicNetDriver a = new LogicNetDriver.NetworkInput($"A{stage}");
            LogicNetDriver b = new LogicNetDriver.NetworkInput($"B{stage}");
            Nand($"{p}P", a, b);
            Nand($"{p}Q", a, Out($"{p}P"));
            Nand($"{p}R", b, Out($"{p}P"));
            Nand($"{p}X", Out($"{p}Q"), Out($"{p}R"));
            Nand($"{p}P2", Out($"{p}X"), carry);
            Nand($"{p}Q2", Out($"{p}X"), Out($"{p}P2"));
            Nand($"{p}R2", carry, Out($"{p}P2"));
            Nand($"{p}S", Out($"{p}Q2"), Out($"{p}R2"));
            Nand($"{p}C", Out($"{p}P"), Out($"{p}P2"));
            carry = Out($"{p}C");
            if (stage < IsaMachine.DataBits - 1 || tapS3)
            {
                taps[$"S{stage}"] = new LogicPinRef($"{p}S", "Y");
            }
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps, gateDelays);
    }
}
