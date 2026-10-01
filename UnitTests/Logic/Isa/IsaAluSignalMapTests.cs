using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The optional <see cref="IsaAluSignalMap"/> of the photonic ALUs (issue #1285): a
/// custom map must make an ALU drive and read exactly the mapped signals, so one
/// chip can expose several ISA operations without a tap-name collision. Covers the
/// combined logic-unit preset (AND on Y0–Y3, NOT on N0–N3, shared operands A0–A3)
/// over a single hand-wired network, plus map validation and Accepts/constructor
/// errors that name the missing mapped signal.
/// </summary>
public class IsaAluSignalMapTests
{
    [Fact]
    public void Map_WrongSignalCount_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            new IsaAluSignalMap(new[] { "A0" }, null, null, Names("Y")));
        Should.Throw<ArgumentException>(() =>
            new IsaAluSignalMap(Names("A"), null, null, new[] { "Y0" }));
        Should.Throw<ArgumentException>(() =>
            new IsaAluSignalMap(Names("A"), new[] { "B0" }, null, Names("Y")));
    }

    [Fact]
    public void Not_CustomMap_DrivesAndReadsTheMappedSignals()
    {
        var map = new IsaAluSignalMap(Names("X"), null, null, Names("R"));
        var network = BuildNotNetwork("X", "R");
        var photonic = new PhotonicNotAlu(network, map);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            photonic.Not(a).ShouldBe(golden.Not(a), $"A={a}");
        }

        PhotonicNotAlu.Accepts(network, map).ShouldBeTrue();
        PhotonicNotAlu.Accepts(network).ShouldBeFalse("the default map names Y0–Y3, not R0–R3");
    }

    [Fact]
    public void Not_ConstructorMissingMappedSignal_ThrowsNamingIt()
    {
        var map = new IsaAluSignalMap(Names("X"), null, null, Names("R"));
        var network = BuildNotNetwork("X", "R", tapBit2: false);

        var exception = Should.Throw<ArgumentException>(() => new PhotonicNotAlu(network, map));

        exception.Message.ShouldContain("R2");
        PhotonicNotAlu.Accepts(network, map).ShouldBeFalse();
    }

    [Fact]
    public void And_CustomMap_DrivesAndReadsTheMappedSignals()
    {
        var map = new IsaAluSignalMap(Names("P"), Names("Q"), null, Names("Z"));
        var network = BuildAndNetwork("P", "Q", "Z");
        var photonic = new PhotonicAndAlu(network, map);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            photonic.And(a, b).ShouldBe(golden.And(a, b), $"A={a}, B={b}");
        }
    }

    [Fact]
    public void And_ConstructorMissingMappedSignal_ThrowsNamingIt()
    {
        var map = new IsaAluSignalMap(Names("P"), Names("Q"), null, Names("Z"));
        var network = BuildAndNetwork("P", "Q", "Z", tapBit0: false);

        var exception = Should.Throw<ArgumentException>(() => new PhotonicAndAlu(network, map));

        exception.Message.ShouldContain("Z0");
        PhotonicAndAlu.Accepts(network, map).ShouldBeFalse();
    }

    [Fact]
    public void And_UnaryMap_Throws()
    {
        var network = BuildAndNetwork("A", "B", "Y");

        Should.Throw<ArgumentException>(() =>
            new PhotonicAndAlu(network, IsaAluSignalMap.NotDefault));
    }

    [Fact]
    public void Adder_CustomMap_DrivesAndReadsTheMappedSignals()
    {
        var map = new IsaAluSignalMap(Names("U"), Names("V"), "Carry", Names("T"));
        var network = BuildAdderNetwork("U", "V", "Carry", "T");
        var photonic = new PhotonicAdderAlu(network, map);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            photonic.Add(a, b).ShouldBe(golden.Add(a, b), $"A={a}, B={b}");
        }

        PhotonicAdderAlu.Accepts(network, map).ShouldBeTrue();
        PhotonicAdderAlu.Accepts(network).ShouldBeFalse("the default map names Cin/S0–S3");
    }

    [Fact]
    public void Adder_ConstructorMissingMappedSignal_ThrowsNamingIt()
    {
        var map = new IsaAluSignalMap(Names("U"), Names("V"), "Carry", Names("T"));
        var network = BuildAdderNetwork("U", "V", "DifferentCarry", "T");

        var exception = Should.Throw<ArgumentException>(() => new PhotonicAdderAlu(network, map));

        exception.Message.ShouldContain("Carry");
        PhotonicAdderAlu.Accepts(network, map).ShouldBeFalse();
    }

    [Fact]
    public void CompositeIsaAlu_CombinedPreset_OneNetworkComputesAndAndNot()
    {
        // The combined logic-unit chip: AND keeps the default Y0–Y3 taps, the NOT
        // taps N0–N3, both read the shared operands A0–A3 (AND also B0–B3).
        var network = BuildCombinedNotAndNetwork();
        var notUnit = new PhotonicNotAlu(network, IsaAluSignalMap.CombinedNot);
        var andUnit = new PhotonicAndAlu(network);
        var composite = new CompositeIsaAlu(new GoldenIsaAlu(), notUnit, andUnit);
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

    private static string[] Names(string prefix) =>
        Enumerable.Range(0, IsaMachine.DataBits).Select(bit => $"{prefix}{bit}").ToArray();

    private static void WireNotSlice(
        Dictionary<string, LogicGateModel> gates,
        Dictionary<LogicPinRef, LogicNetDriver> wiring,
        Dictionary<string, LogicPinRef> taps,
        string gateId, string inputName, string tapName)
    {
        LogicNetDriver input = new LogicNetDriver.NetworkInput(inputName);
        gates[gateId] = PinnedGateTables.NandGate();
        wiring[new LogicPinRef(gateId, "A")] = input;
        wiring[new LogicPinRef(gateId, "B")] = input;
        taps[tapName] = new LogicPinRef(gateId, "Y");
    }

    private static void WireAndSlice(
        Dictionary<string, LogicGateModel> gates,
        Dictionary<LogicPinRef, LogicNetDriver> wiring,
        Dictionary<string, LogicPinRef> taps,
        string gateId, string inputA, string inputB, string tapName)
    {
        var nandId = $"{gateId}N";
        LogicNetDriver a = new LogicNetDriver.NetworkInput(inputA);
        LogicNetDriver b = new LogicNetDriver.NetworkInput(inputB);
        gates[nandId] = PinnedGateTables.NandGate();
        wiring[new LogicPinRef(nandId, "A")] = a;
        wiring[new LogicPinRef(nandId, "B")] = b;
        LogicNetDriver nandOut = new LogicNetDriver.GateOutput(new LogicPinRef(nandId, "Y"));
        gates[gateId] = PinnedGateTables.NandGate();
        wiring[new LogicPinRef(gateId, "A")] = nandOut;
        wiring[new LogicPinRef(gateId, "B")] = nandOut;
        taps[tapName] = new LogicPinRef(gateId, "Y");
    }

    private static LogicNetworkEvaluator BuildNotNetwork(
        string inPrefix, string outPrefix, bool tapBit2 = true)
    {
        var inputs = Names(inPrefix).ToList();
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();
        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            if (bit == 2 && !tapBit2)
            {
                continue;
            }

            WireNotSlice(gates, wiring, taps, $"N{bit}", $"{inPrefix}{bit}", $"{outPrefix}{bit}");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    private static LogicNetworkEvaluator BuildAndNetwork(
        string aPrefix, string bPrefix, string outPrefix, bool tapBit0 = true)
    {
        var inputs = Names(aPrefix).Concat(Names(bPrefix)).ToList();
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();
        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            if (bit == 0 && !tapBit0)
            {
                continue;
            }

            WireAndSlice(gates, wiring, taps,
                $"M{bit}", $"{aPrefix}{bit}", $"{bPrefix}{bit}", $"{outPrefix}{bit}");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    /// <summary>
    /// The combined logic-unit network: NOT slices on A0–A3 → N0–N3 and AND slices
    /// on A0–A3 &amp; B0–B3 → Y0–Y3 in a single evaluator.
    /// </summary>
    private static LogicNetworkEvaluator BuildCombinedNotAndNetwork()
    {
        var inputs = Names("A").Concat(Names("B")).ToList();
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();
        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            WireNotSlice(gates, wiring, taps, $"N{bit}", $"A{bit}", $"N{bit}");
            WireAndSlice(gates, wiring, taps, $"M{bit}", $"A{bit}", $"B{bit}", $"Y{bit}");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    /// <summary>
    /// Wires a 4-bit ripple-carry adder from the pinned NAND gates with renamable
    /// signal names — the same structure as the default-name fixture of
    /// <see cref="PhotonicAdderAluTests"/>.
    /// </summary>
    private static LogicNetworkEvaluator BuildAdderNetwork(
        string aPrefix, string bPrefix, string carryInName, string outPrefix)
    {
        var inputs = Names(aPrefix).Concat(Names(bPrefix)).Append(carryInName).ToList();
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

        LogicNetDriver carry = new LogicNetDriver.NetworkInput(carryInName);
        for (var stage = 0; stage < IsaMachine.DataBits; stage++)
        {
            var p = $"T{stage}";
            LogicNetDriver a = new LogicNetDriver.NetworkInput($"{aPrefix}{stage}");
            LogicNetDriver b = new LogicNetDriver.NetworkInput($"{bPrefix}{stage}");
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
            taps[$"{outPrefix}{stage}"] = new LogicPinRef($"{p}S", "Y");
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }
}
