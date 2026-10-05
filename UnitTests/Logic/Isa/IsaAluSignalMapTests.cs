using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The optional <see cref="IsaAluSignalMap"/> of the photonic ALUs (issue #1285):
/// each ALU with a custom map must drive and read exactly the mapped signals,
/// <c>Accepts</c> must answer against the map, and the constructor error must name
/// the missing mapped signal. The combined-logic-unit preset is pinned by a
/// <see cref="CompositeIsaAlu"/> over one hand-built network that exposes AND on
/// Y0–Y3 and NOT on N0–N3 side by side — the rung-5 chip this issue unblocks.
/// </summary>
public class IsaAluSignalMapTests
{
    [Fact]
    public void Not_CustomMap_DrivesAndReadsExactlyTheMappedSignals()
    {
        var map = new IsaAluSignalMap(
            operandA: new[] { "In0", "In1", "In2", "In3" },
            result: new[] { "N0", "N1", "N2", "N3" });
        var photonic = new PhotonicNotAlu(BuildNotSliceNetwork(map), map);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            photonic.Not(a).ShouldBe(golden.Not(a), $"A={a}");
        }
    }

    [Fact]
    public void And_CustomMap_DrivesAndReadsExactlyTheMappedSignals()
    {
        var map = new IsaAluSignalMap(
            operandA: new[] { "X0", "X1", "X2", "X3" },
            result: new[] { "P0", "P1", "P2", "P3" },
            operandB: new[] { "Q0", "Q1", "Q2", "Q3" });
        var photonic = new PhotonicAndAlu(BuildAndSliceNetwork(map.OperandA, map.OperandB, map.Result), map);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            photonic.And(a, b).ShouldBe(golden.And(a, b), $"A={a}, B={b}");
        }
    }

    [Fact]
    public void Adder_CustomMap_DrivesAndReadsExactlyTheMappedSignals()
    {
        var map = new IsaAluSignalMap(
            operandA: new[] { "U0", "U1", "U2", "U3" },
            result: new[] { "R0", "R1", "R2", "R3" },
            operandB: new[] { "V0", "V1", "V2", "V3" },
            carryIn: "Carry");
        var photonic = new PhotonicAdderAlu(BuildAdderSliceNetwork(map), map);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            photonic.Add(a, b).ShouldBe(golden.Add(a, b), $"A={a}, B={b}");
        }
    }

    [Fact]
    public void Accepts_WithMap_ReportsWhetherTheNetworkExposesTheMappedSignals()
    {
        var map = IsaAluSignalMap.CombinedLogicUnitNot;

        PhotonicNotAlu.Accepts(BuildCombinedNetwork(), map).ShouldBeTrue();
        PhotonicNotAlu.Accepts(BuildNotSliceNetwork(IsaAluSignalMap.Not), map).ShouldBeFalse(
            "the plain NOT network has Y0–Y3, not the mapped N0–N3");
        PhotonicNotAlu.Accepts(null, map).ShouldBeFalse();

        PhotonicAndAlu.Accepts(BuildCombinedNetwork()).ShouldBeTrue();
        PhotonicAdderAlu.Accepts(BuildCombinedNetwork()).ShouldBeFalse(
            "the combined network has no Cin or S0–S3");
    }

    [Fact]
    public void Constructor_WithMap_MissingMappedSignal_ThrowsNamingTheSignal()
    {
        var notException = Should.Throw<ArgumentException>(() =>
            new PhotonicNotAlu(BuildNotSliceNetwork(IsaAluSignalMap.Not), IsaAluSignalMap.CombinedLogicUnitNot));
        notException.Message.ShouldContain("N0");

        var andMap = new IsaAluSignalMap(
            operandA: new[] { "A0", "A1", "A2", "A3" },
            result: new[] { "Y0", "Y1", "Y2", "Y3" },
            operandB: new[] { "Second0", "Second1", "Second2", "Second3" });
        var andException = Should.Throw<ArgumentException>(() =>
            new PhotonicAndAlu(BuildCombinedNetwork(), andMap));
        andException.Message.ShouldContain("Second0");

        var adderException = Should.Throw<ArgumentException>(() =>
            new PhotonicAdderAlu(BuildCombinedNetwork(), IsaAluSignalMap.Adder));
        adderException.Message.ShouldContain("Cin");
    }

    [Fact]
    public void Map_WrongSignalCount_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentException>(() => new IsaAluSignalMap(
                operandA: new[] { "A0", "A1", "A2" },
                result: new[] { "Y0", "Y1", "Y2", "Y3" }))
            .Message.ShouldContain("operandA");
        Should.Throw<ArgumentException>(() => new IsaAluSignalMap(
                operandA: new[] { "A0", "A1", "A2", "A3" },
                result: new[] { "Y0", "Y1", "Y2", "Y3", "Y4" }))
            .Message.ShouldContain("result");
    }

    [Fact]
    public void CombinedChip_NotOnPresetAndAndOnDefault_ComputeBothOperationsOnOneNetwork()
    {
        var network = BuildCombinedNetwork();
        var composite = new CompositeIsaAlu(
            new GoldenIsaAlu(),
            new PhotonicNotAlu(network, IsaAluSignalMap.CombinedLogicUnitNot),
            new PhotonicAndAlu(network));
        var golden = new GoldenIsaAlu();

        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            composite.Not(a).ShouldBe(golden.Not(a), $"NOT A={a}");
            for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
            {
                composite.And(a, b).ShouldBe(golden.And(a, b), $"AND A={a}, B={b}");
            }
        }
    }

    [Fact]
    public void Emulator_CombinedChip_RunsNotAndAndPhotonicallyInOneProgram()
    {
        var network = BuildCombinedNetwork();
        var alu = new CompositeIsaAlu(
            new GoldenIsaAlu(),
            new PhotonicNotAlu(network, IsaAluSignalMap.CombinedLogicUnitNot),
            new PhotonicAndAlu(network));
        var program = new IsaAssembler().Assemble("LOAD 5\nNOT\nSTORE 0\nLOAD 12\nAND 0\nSTORE 1\nHALT");
        var emulator = new IsaEmulator(program, alu);

        emulator.Run(20);

        emulator.Accumulator.ShouldBe(12 & 10);
        emulator.Ram[0].ShouldBe(10, "~5 & 0xF, computed by the photonic NOT slice");
        emulator.Ram[1].ShouldBe(8, "12 & 10, computed by the photonic AND slice on the same network");
        emulator.IsHalted.ShouldBeTrue();
    }

    /// <summary>
    /// Wires one network with both slices of the combined logic-unit chip: a NOT
    /// slice reading the operand bits A0–A3 onto the taps N0–N3 (the preset) and an
    /// AND slice reading A0–A3 &amp; B0–B3 onto Y0–Y3 (the default names).
    /// </summary>
    private static LogicNetworkEvaluator BuildCombinedNetwork()
    {
        var notMap = IsaAluSignalMap.CombinedLogicUnitNot;
        var andMap = IsaAluSignalMap.And;
        var inputs = new List<string> { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" };
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();

        void Nand(string id, LogicNetDriver inA, LogicNetDriver inB)
        {
            gates[id] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef(id, "A")] = inA;
            wiring[new LogicPinRef(id, "B")] = inB;
        }

        LogicNetDriver Out(string id) => new LogicNetDriver.GateOutput(new LogicPinRef(id, "Y"));

        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            LogicNetDriver a = new LogicNetDriver.NetworkInput(notMap.OperandA[bit]);
            LogicNetDriver b = new LogicNetDriver.NetworkInput(andMap.OperandB[bit]);

            // NOT slice: NAND(a, a) = ¬a, tapped as N0–N3.
            var notId = $"N{bit}";
            Nand(notId, a, a);
            taps[notMap.Result[bit]] = new LogicPinRef(notId, "Y");

            // AND slice: AND(a, b) = NAND(NAND(a, b), NAND(a, b)), tapped as Y0–Y3.
            var nandId = $"P{bit}";
            var andId = $"Q{bit}";
            Nand(nandId, a, b);
            Nand(andId, Out(nandId), Out(nandId));
            taps[andMap.Result[bit]] = new LogicPinRef(andId, "Y");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    /// <summary>
    /// Wires a 4-bit NOT from the pinned NAND gates (NAND(a, a) = ¬a) under the
    /// signal names of <paramref name="map"/>.
    /// </summary>
    private static LogicNetworkEvaluator BuildNotSliceNetwork(IsaAluSignalMap map)
    {
        var inputs = map.OperandA.ToList();
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();

        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            var gateId = $"N{bit}";
            LogicNetDriver input = new LogicNetDriver.NetworkInput(map.OperandA[bit]);
            gates[gateId] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef(gateId, "A")] = input;
            wiring[new LogicPinRef(gateId, "B")] = input;
            taps[map.Result[bit]] = new LogicPinRef(gateId, "Y");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    /// <summary>
    /// Wires a 4-bit AND from the pinned NAND gates (two per bit) under the given
    /// operand and result names.
    /// </summary>
    private static LogicNetworkEvaluator BuildAndSliceNetwork(
        IReadOnlyList<string> operandA, IReadOnlyList<string> operandB, IReadOnlyList<string> result)
    {
        var inputs = operandA.Concat(operandB).ToList();
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();

        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            LogicNetDriver a = new LogicNetDriver.NetworkInput(operandA[bit]);
            LogicNetDriver b = new LogicNetDriver.NetworkInput(operandB[bit]);
            var nandId = $"N{bit}";
            var andId = $"M{bit}";
            gates[nandId] = PinnedGateTables.NandGate();
            gates[andId] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef(nandId, "A")] = a;
            wiring[new LogicPinRef(nandId, "B")] = b;
            LogicNetDriver nandOut = new LogicNetDriver.GateOutput(new LogicPinRef(nandId, "Y"));
            wiring[new LogicPinRef(andId, "A")] = nandOut;
            wiring[new LogicPinRef(andId, "B")] = nandOut;
            taps[result[bit]] = new LogicPinRef(andId, "Y");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    /// <summary>
    /// Wires a 4-bit ripple-carry adder from the pinned NAND gates (nine per stage)
    /// under the signal names of <paramref name="map"/>, carry-in included.
    /// </summary>
    private static LogicNetworkEvaluator BuildAdderSliceNetwork(IsaAluSignalMap map)
    {
        var inputs = map.OperandA.Concat(map.OperandB).Append(map.CarryIn!).ToList();
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();

        void Nand(string id, LogicNetDriver inA, LogicNetDriver inB)
        {
            gates[id] = PinnedGateTables.NandGate();
            wiring[new LogicPinRef(id, "A")] = inA;
            wiring[new LogicPinRef(id, "B")] = inB;
        }

        LogicNetDriver Out(string id) => new LogicNetDriver.GateOutput(new LogicPinRef(id, "Y"));

        LogicNetDriver carry = new LogicNetDriver.NetworkInput(map.CarryIn!);
        for (var stage = 0; stage < IsaMachine.DataBits; stage++)
        {
            var p = $"T{stage}";
            LogicNetDriver a = new LogicNetDriver.NetworkInput(map.OperandA[stage]);
            LogicNetDriver b = new LogicNetDriver.NetworkInput(map.OperandB[stage]);
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
            taps[map.Result[stage]] = new LogicPinRef($"{p}S", "Y");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }
}
