using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The configurable signal map of <see cref="PhotonicDataMemory"/> (issue #1454):
/// a prefixed map (<see cref="IsaDataMemorySignalMap.WithPrefix"/>) must drive and
/// read exactly the prefixed signals, <c>Accepts</c> must answer against the map,
/// and one network must be able to expose the 4-bit adder on the plain names and
/// the RAM on prefixed names side by side without a collision — the seam for the
/// rung-5 chip where ALU and RAM share one network. The RAM behaviour is pinned
/// golden-vs-photonic on a small hand-built register evaluator.
/// </summary>
public class IsaDataMemorySignalMapTests
{
    private static readonly IsaDataMemorySignalMap Prefixed = IsaDataMemorySignalMap.WithPrefix("RAM.");

    private static readonly int[] StoredWords = { 3, 5, 10, 12 };

    [Fact]
    public void DefaultMap_MatchesTheShippedRamNames()
    {
        IsaDataMemorySignalMap.Default.Address.ShouldBe(new[] { "A0", "A1" });
        IsaDataMemorySignalMap.Default.Load.ShouldBe("LOAD");
        IsaDataMemorySignalMap.Default.DataIn.ShouldBe(new[] { "D0", "D1", "D2", "D3" });
        IsaDataMemorySignalMap.Default.DataOut.ShouldBe(new[] { "Q0", "Q1", "Q2", "Q3" });
    }

    [Fact]
    public void WithPrefix_RenamesEverySignal()
    {
        Prefixed.Address.ShouldBe(new[] { "RAM.A0", "RAM.A1" });
        Prefixed.Load.ShouldBe("RAM.LOAD");
        Prefixed.DataIn.ShouldBe(new[] { "RAM.D0", "RAM.D1", "RAM.D2", "RAM.D3" });
        Prefixed.DataOut.ShouldBe(new[] { "RAM.Q0", "RAM.Q1", "RAM.Q2", "RAM.Q3" });
        Should.Throw<ArgumentException>(() => IsaDataMemorySignalMap.WithPrefix(""));
    }

    [Fact]
    public void Map_WrongSignalCount_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentException>(() => new IsaDataMemorySignalMap(
                new[] { "A0" }, "LOAD",
                new[] { "D0", "D1", "D2", "D3" }, new[] { "Q0", "Q1", "Q2", "Q3" }))
            .Message.ShouldContain("address");
        Should.Throw<ArgumentException>(() => new IsaDataMemorySignalMap(
                new[] { "A0", "A1" }, "LOAD",
                new[] { "D0", "D1", "D2" }, new[] { "Q0", "Q1", "Q2", "Q3" }))
            .Message.ShouldContain("dataIn");
        Should.Throw<ArgumentException>(() => new IsaDataMemorySignalMap(
                new[] { "A0", "A1" }, "LOAD",
                new[] { "D0", "D1", "D2", "D3" }, new[] { "Q0", "Q1", "Q2", "Q3", "Q4" }))
            .Message.ShouldContain("dataOut");
    }

    [Fact]
    public void Accepts_PrefixedMap_ReportsWhetherTheNetworkExposesThePrefixedSignals()
    {
        var prefixedRam = BuildRamNetwork(Prefixed);

        PhotonicDataMemory.Accepts(prefixedRam, Prefixed).ShouldBeTrue();
        PhotonicDataMemory.Accepts(prefixedRam).ShouldBeFalse(
            "the prefixed RAM has no plain A0/LOAD/D/Q signals and must be rejected with the default map");
        PhotonicDataMemory.Accepts(null, Prefixed).ShouldBeFalse();
    }

    [Fact]
    public void Constructor_PrefixedMap_MissingPrefixedSignal_ThrowsNamingIt()
    {
        var plainRam = BuildRamNetwork(IsaDataMemorySignalMap.Default);

        var exception = Should.Throw<ArgumentException>(() => new PhotonicDataMemory(plainRam, Prefixed));

        exception.Message.ShouldContain("'RAM.A0'");
    }

    [Fact]
    public void PrefixedRam_StoreAllReadBack_MatchesTheGoldenMemory()
    {
        var photonic = new PhotonicDataMemory(BuildRamNetwork(Prefixed), Prefixed);
        var golden = new GoldenIsaDataMemory();

        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            photonic.Write(address, StoredWords[address]);
            golden.Write(address, StoredWords[address]);
        }

        photonic.WriteCount.ShouldBe(IsaMachine.RamWords, "one write per STORE");
        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            photonic.Read(address).ShouldBe(golden.Read(address),
                $"word {address} stored on the prefixed RAM must read back exactly as on the golden model");
        }

        photonic.ReadCount.ShouldBe(IsaMachine.RamWords, "one read per RAM operand");
    }

    [Fact]
    public void CombinedNetwork_AdderOnDefaultNamesAndRamOnPrefixedNames_BothAcceptedAndBothWork()
    {
        var network = BuildCombinedNetwork();

        PhotonicAdderAlu.Accepts(network).ShouldBeTrue();
        PhotonicDataMemory.Accepts(network, Prefixed).ShouldBeTrue();
        PhotonicDataMemory.Accepts(network).ShouldBeFalse(
            "the combined network deliberately has no unprefixed RAM signals");

        var alu = new PhotonicAdderAlu(network);
        var memory = new PhotonicDataMemory(network, Prefixed);
        var golden = new GoldenIsaDataMemory();
        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Write(address, StoredWords[address]);
            golden.Write(address, StoredWords[address]);
        }

        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            var word = memory.Read(address);
            word.ShouldBe(golden.Read(address), $"RAM word {address} on the shared network");
            alu.Add(word, 1).ShouldBe((word + 1) & IsaMachine.MaxDataValue,
                "the adder on the same network must not clash with the prefixed RAM signals");
        }
    }

    /// <summary>
    /// Wires the adder slice (plain names, <see cref="IsaAluSignalMap.Adder"/>) and
    /// the RAM slice (prefixed names) into one evaluator — the rung-5 chip shape.
    /// </summary>
    private static LogicNetworkEvaluator BuildCombinedNetwork()
    {
        var adderMap = IsaAluSignalMap.Adder;
        var inputs = adderMap.AllOperands.Concat(Prefixed.AllInputs).ToList();
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();
        var registers = new List<string>();

        AddAdderSlice(adderMap, gates, wiring, taps);
        AddRamSlice(Prefixed, gates, wiring, taps, registers);
        return new LogicNetworkEvaluator(inputs, gates, wiring, taps, registerGateIds: registers);
    }

    /// <summary>A 4x4-bit register RAM slice under the names of <paramref name="map"/>.</summary>
    private static LogicNetworkEvaluator BuildRamNetwork(IsaDataMemorySignalMap map)
    {
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();
        var registers = new List<string>();
        AddRamSlice(map, gates, wiring, taps, registers);
        return new LogicNetworkEvaluator(map.AllInputs.ToList(), gates, wiring, taps, registerGateIds: registers);
    }

    /// <summary>
    /// Adds the RAM slice: per word a write-select gate (address match and LOAD) and
    /// four register bits (Y = W ? D : Q, the feedback legal through the register);
    /// per bit a read mux (Y = reg[address]). STORE commits with one
    /// <see cref="LogicNetworkEvaluator.Step"/>, reads are combinational.
    /// </summary>
    private static void AddRamSlice(
        IsaDataMemorySignalMap map,
        Dictionary<string, LogicGateModel> gates,
        Dictionary<LogicPinRef, LogicNetDriver> wiring,
        Dictionary<string, LogicPinRef> taps,
        List<string> registers)
    {
        for (var word = 0; word < IsaMachine.RamWords; word++)
        {
            var selectId = $"sel{word}";
            gates[selectId] = TableGate($"Sel{word}", new[] { "A0", "A1", "L" },
                bits => bits[2] && ((bits[1] ? 2 : 0) + (bits[0] ? 1 : 0)) == word);
            wiring[new LogicPinRef(selectId, "A0")] = new LogicNetDriver.NetworkInput(map.Address[0]);
            wiring[new LogicPinRef(selectId, "A1")] = new LogicNetDriver.NetworkInput(map.Address[1]);
            wiring[new LogicPinRef(selectId, "L")] = new LogicNetDriver.NetworkInput(map.Load);
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                var registerId = $"reg{word}_{bit}";
                gates[registerId] = TableGate($"Reg{word}_{bit}", new[] { "D", "W", "Q" },
                    bits => bits[1] ? bits[0] : bits[2]);
                wiring[new LogicPinRef(registerId, "D")] = new LogicNetDriver.NetworkInput(map.DataIn[bit]);
                wiring[new LogicPinRef(registerId, "W")] = new LogicNetDriver.GateOutput(new LogicPinRef(selectId, "Y"));
                wiring[new LogicPinRef(registerId, "Q")] = new LogicNetDriver.GateOutput(new LogicPinRef(registerId, "Y"));
                registers.Add(registerId);
            }
        }

        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            var muxId = $"mux{bit}";
            gates[muxId] = TableGate($"Mux{bit}", new[] { "R0", "R1", "R2", "R3", "A0", "A1" },
                bits => bits[(bits[5] ? 2 : 0) + (bits[4] ? 1 : 0)]);
            for (var word = 0; word < IsaMachine.RamWords; word++)
            {
                wiring[new LogicPinRef(muxId, $"R{word}")] =
                    new LogicNetDriver.GateOutput(new LogicPinRef($"reg{word}_{bit}", "Y"));
            }

            wiring[new LogicPinRef(muxId, "A0")] = new LogicNetDriver.NetworkInput(map.Address[0]);
            wiring[new LogicPinRef(muxId, "A1")] = new LogicNetDriver.NetworkInput(map.Address[1]);
            taps[map.DataOut[bit]] = new LogicPinRef(muxId, "Y");
        }
    }

    /// <summary>Adds the 4-bit ripple-carry adder slice (nine NANDs per stage).</summary>
    private static void AddAdderSlice(
        IsaAluSignalMap map,
        Dictionary<string, LogicGateModel> gates,
        Dictionary<LogicPinRef, LogicNetDriver> wiring,
        Dictionary<string, LogicPinRef> taps)
    {
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
    }

    /// <summary>Builds a single-output gate model from an input-bits → Y function.</summary>
    private static LogicGateModel TableGate(string name, string[] inputNames, Func<bool[], bool> output)
    {
        var rows = new List<TruthTableRow>();
        for (var mask = 0; mask < (1 << inputNames.Length); mask++)
        {
            var bits = new bool[inputNames.Length];
            var inputs = new Dictionary<string, bool>();
            for (var pin = 0; pin < inputNames.Length; pin++)
            {
                bits[pin] = ((mask >> pin) & 1) == 1;
                inputs[inputNames[pin]] = bits[pin];
            }

            var y = output(bits);
            rows.Add(new TruthTableRow(
                inputs,
                new Dictionary<string, LogicOutputValue> { ["Y"] = new(y, y ? 0.5 : 0.0) }));
        }

        return LogicGateModel.FromTruthTable(
            new TruthTable(name, inputNames, new[] { "Y" }, 0.25, PinnedGateTables.WavelengthNm, rows));
    }
}
