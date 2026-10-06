using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The configurable signal map of <see cref="PhotonicAccumulator"/> (issue #1464):
/// the default map carries the <c>ACC.</c> prefix from the start so the register
/// can later share one network with the ALU and the RAM, and the ACC behaviour is
/// pinned golden-vs-photonic on a small hand-built 4-bit register evaluator —
/// write/read round trip of every 4-bit value, masking, reset — plus the
/// acceptance that golden and photonic-accumulator traces of every shipped ISA
/// sample are identical, step for step.
/// </summary>
public class IsaAccumulatorSignalMapTests
{
    private const int StepBudget = 500;

    [Fact]
    public void DefaultMap_CarriesTheAccPrefix()
    {
        IsaAccumulatorSignalMap.Default.DataIn.ShouldBe(new[] { "ACC.D0", "ACC.D1", "ACC.D2", "ACC.D3" });
        IsaAccumulatorSignalMap.Default.Load.ShouldBe("ACC.LOAD");
        IsaAccumulatorSignalMap.Default.DataOut.ShouldBe(new[] { "ACC.Q0", "ACC.Q1", "ACC.Q2", "ACC.Q3" });
    }

    [Fact]
    public void Map_WrongSignalCount_ThrowsNamingTheParameter()
    {
        Should.Throw<ArgumentException>(() => new IsaAccumulatorSignalMap(
                new[] { "ACC.D0", "ACC.D1", "ACC.D2" }, "ACC.LOAD",
                new[] { "ACC.Q0", "ACC.Q1", "ACC.Q2", "ACC.Q3" }))
            .Message.ShouldContain("dataIn");
        Should.Throw<ArgumentException>(() => new IsaAccumulatorSignalMap(
                new[] { "ACC.D0", "ACC.D1", "ACC.D2", "ACC.D3" }, "ACC.LOAD",
                new[] { "ACC.Q0", "ACC.Q1", "ACC.Q2", "ACC.Q3", "ACC.Q4" }))
            .Message.ShouldContain("dataOut");
    }

    [Fact]
    public void WriteReadRoundTrip_All16Values_MatchTheGoldenAccumulator()
    {
        var photonic = new PhotonicAccumulator(BuildRegisterNetwork());
        var golden = new GoldenIsaAccumulator();

        for (var value = 0; value <= IsaMachine.MaxDataValue; value++)
        {
            photonic.Write(value);
            golden.Write(value);
            photonic.Read().ShouldBe(golden.Read(),
                $"the value {value} written on light must read back exactly as on the golden model");
        }
    }

    [Fact]
    public void Write_MasksToFourBits_LikeTheGoldenAccumulator()
    {
        var photonic = new PhotonicAccumulator(BuildRegisterNetwork());
        var golden = new GoldenIsaAccumulator();

        foreach (var value in new[] { 16, 17, 31, 255, -1 })
        {
            photonic.Write(value);
            golden.Write(value);
            photonic.Read().ShouldBe(golden.Read(), $"value {value} must wrap modulo 16");
        }
    }

    [Fact]
    public void Reset_ClearsTheRegister()
    {
        var photonic = new PhotonicAccumulator(BuildRegisterNetwork());
        photonic.Write(13);

        photonic.Reset();

        photonic.Read().ShouldBe(0, "the power-up reset must clear the register to zero");
    }

    [Fact]
    public void ExtraNetworkInputs_AreTiedToZeroOnEveryAccess()
    {
        var photonic = new PhotonicAccumulator(BuildRegisterNetwork(extraInput: "ALU.Cin"));

        photonic.Write(11);

        photonic.Read().ShouldBe(11,
            "an input the accumulator does not own must be tied to 0, never disturb the register");
    }

    [Theory]
    [InlineData("count-to-5.asm")]
    [InlineData("add-two-numbers.asm")]
    [InlineData("multiply-3x4.asm")]
    [InlineData("mask-and-invert.asm")]
    public void ShippedSample_GoldenAndPhotonicAccumulatorTraces_AreIdentical(string fileName)
    {
        var sample = IsaSampleProgramCatalog.LoadDefault().Samples
            .Single(s => s.FileName == fileName);
        byte[] program = new IsaAssembler().Assemble(sample.Source);

        var golden = new IsaEmulator(program);
        var photonicBacked = new IsaEmulator(program,
            accumulator: new PhotonicAccumulator(BuildRegisterNetwork()));
        var goldenTrace = new List<string>();
        var photonicTrace = new List<string>();

        while (!golden.IsHalted && goldenTrace.Count < StepBudget)
        {
            golden.Step();
            photonicBacked.Step();
            goldenTrace.Add(TraceLine(golden));
            photonicTrace.Add(TraceLine(photonicBacked));
        }

        golden.IsHalted.ShouldBeTrue($"{fileName} must halt within the step budget");
        photonicTrace.ShouldBe(goldenTrace,
            $"{fileName} with its ACC on light must execute exactly as on the golden model");
    }

    private static string TraceLine(IsaEmulator emulator)
    {
        return $"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
               $"RAM=[{string.Join(",", emulator.Ram)}] Halted={emulator.IsHalted}";
    }

    /// <summary>
    /// A 4-bit register slice under the names of the default map: per bit one
    /// register gate (Y = LOAD ? D : Q, the feedback legal through the register).
    /// Write commits with one <see cref="LogicNetworkEvaluator.Step"/>, reads are
    /// combinational. Optionally adds one network input the accumulator does not
    /// own, to pin the tie-to-0 rule.
    /// </summary>
    private static LogicNetworkEvaluator BuildRegisterNetwork(string? extraInput = null)
    {
        var map = IsaAccumulatorSignalMap.Default;
        var inputs = map.AllInputs.ToList();
        if (extraInput != null)
        {
            inputs.Add(extraInput);
        }

        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();
        var registers = new List<string>();
        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            var registerId = $"acc{bit}";
            gates[registerId] = TableGate($"Acc{bit}", new[] { "D", "W", "Q" },
                bits => bits[1] ? bits[0] : bits[2]);
            wiring[new LogicPinRef(registerId, "D")] = new LogicNetDriver.NetworkInput(map.DataIn[bit]);
            wiring[new LogicPinRef(registerId, "W")] = new LogicNetDriver.NetworkInput(map.Load);
            wiring[new LogicPinRef(registerId, "Q")] = new LogicNetDriver.GateOutput(new LogicPinRef(registerId, "Y"));
            taps[map.DataOut[bit]] = new LogicPinRef(registerId, "Y");
            registers.Add(registerId);
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps, registerGateIds: registers);
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
