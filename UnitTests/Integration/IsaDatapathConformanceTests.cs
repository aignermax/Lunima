using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 kill review (issue #1193): pins that the shipped photonic datapath
/// examples — loaded through the real load path and assembled by
/// <see cref="LogicNetworkAssembler"/> — compute exactly what the ISA golden model
/// (<see cref="IsaEmulator"/>, docs/ISA.md) specifies. ADD is checked over all 256
/// operand pairs of the 4-bit adder (Cout dropped = the ISA's mod-16 wrap), AND
/// over the 1-bit ALU's bit slice at Op = 0, NOT over the NOT/NAND gate with its
/// inputs tied (NAND(a, a) = ¬a — the NOT reading of the shipped gate). Every
/// failure message names the example, the operands, the golden and the network
/// value, so a divergence points at the datapath, not the harness.
/// </summary>
public class IsaDatapathConformanceTests :
    IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>,
    IClassFixture<LogicGateAluExampleTests.AluFixture>,
    IClassFixture<IsaDatapathConformanceTests.NotNandGateFixture>
{
    private const int WavelengthNm = 1550;
    private const int BitCount = 4;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _adder;
    private readonly LogicGateAluExampleTests.AluFixture _alu;
    private readonly NotNandGateFixture _gate;

    /// <summary>Attaches the shared example fixtures (each assembles its network once).</summary>
    public IsaDatapathConformanceTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture adder,
        LogicGateAluExampleTests.AluFixture alu,
        NotNandGateFixture gate)
    {
        _adder = adder;
        _alu = alu;
        _gate = gate;
    }

    [Fact]
    public void Add_All256OperandPairs_MatchTheGoldenModel()
    {
        var carryOutSeen = false;
        for (var acc = 0; acc <= IsaMachine.MaxDataValue; acc++)
        for (var ram = 0; ram <= IsaMachine.MaxDataValue; ram++)
        {
            var golden = RunGolden($"LOAD {acc}\nSTORE 0\nLOAD {ram}\nADD 0\nHALT");
            var result = _adder.Network.Evaluate(_adder.InputBits(acc, ram, cin: false));
            var sum = 0;
            for (var bit = 0; bit < BitCount; bit++)
            {
                if (result[$"S{bit}"])
                {
                    sum |= 1 << bit;
                }
            }
            sum.ShouldBe(golden,
                $"Logic Gate 4-Bit Adder.lun: A={acc}, B={ram}, Cin=0 — golden={golden}, network={sum}");
            if (acc + ram > IsaMachine.MaxDataValue)
            {
                carryOutSeen |= result["Cout"];
            }
        }
        carryOutSeen.ShouldBeTrue(
            "Logic Gate 4-Bit Adder.lun: at least one overflowing pair (e.g. 15+1) must drive Cout = 1");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void And_BitSlice_MatchesTheGoldenModel(bool a, bool b)
    {
        var golden = RunGolden($"LOAD {Bit(a)}\nSTORE 0\nLOAD {Bit(b)}\nAND 0\nHALT") & 1;
        var result = _alu.Network.Evaluate(_alu.InputBits(a, b, op: false));
        var actual = result["Result"];
        actual.ShouldBe(golden == 1,
            $"Logic Gate ALU 1-bit.lun: A={a}, B={b}, Op=0 — golden bit0={golden}, network={Bit(actual)}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Not_BitSlice_MatchesTheGoldenModel(bool a)
    {
        var golden = RunGolden($"LOAD {Bit(a)}\nNOT\nHALT") & 1;
        var tap = _gate.Network.OutputPinNames.Single();
        var result = _gate.Network.Evaluate(new Dictionary<string, bool> { ["A"] = a, ["B"] = a });
        var actual = result[tap];
        actual.ShouldBe(golden == 1,
            $"Logic Gate NOT-NAND.lun: A=B={a} (tied inputs read the gate as NOT) — " +
            $"golden bit0={golden}, network={Bit(actual)}");
    }

    /// <summary>Assembles and runs one tiny program on the golden model; returns the accumulator.</summary>
    private static int RunGolden(string source)
    {
        var emulator = new IsaEmulator(new IsaAssembler().Assemble(source));
        emulator.Run(IsaMachine.ProgramRomWords);
        return emulator.Accumulator;
    }

    private static int Bit(bool value) => value ? 1 : 0;

    /// <summary>
    /// Shared fixture: loads the shipped <c>Logic Gate NOT-NAND.lun</c> once and
    /// assembles its single-gate network through the real load path. The persisted
    /// roles read the gate as NAND; tying A and B turns it into the NOT bit slice.
    /// </summary>
    public class NotNandGateFixture : IAsyncLifetime
    {
        private const string ExampleFileName = "Logic Gate NOT-NAND.lun";

        /// <summary>The logic network assembled from the loaded example.</summary>
        public LogicNetworkEvaluator Network { get; private set; } = null!;

        /// <summary>Loads the shipped example and assembles its logic network.</summary>
        public async Task InitializeAsync()
        {
            var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
            var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
            Network = await new LogicNetworkAssembler().AssembleAsync(
                canvas.Components.Select(c => c.Component).ToList(),
                canvas.Connections.Select(c => c.Connection).ToList(),
                WavelengthNm);
        }

        /// <summary>No shared state to release.</summary>
        public Task DisposeAsync() => Task.CompletedTask;
    }
}
