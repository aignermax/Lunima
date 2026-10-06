using System.Diagnostics;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 acceptance (issue #1436): <see cref="IsaEmulator"/> keeps its data RAM in
/// the photonic registers of the shipped "Logic Gate RAM 4x4" example through the
/// <see cref="IIsaDataMemory"/> seam — the step from photonic calculator to photonic
/// computer. The shipped <c>multiply-3x4.asm</c> and a store-all-words/read-back
/// program must each produce a trace (PC, ACC and RAM after every step) identical to
/// the golden run, and the access counts must pin one write per executed
/// <c>STORE</c> and one read per executed RAM operand
/// (<see cref="CAP_Core.Logic.Isa.GoldenIsaDataMemory"/> parity). The fixture is the
/// one <see cref="LogicGateRam4x4ExampleTests"/> loads through the real load path.
/// </summary>
public class IsaPhotonicDataMemoryTraceTests
    : IClassFixture<LogicGateRam4x4ExampleTests.Ram4x4Fixture>
{
    /// <summary>STORE 1, STORE 3 plus one STORE 0 per loop round (4 rounds) in multiply-3x4.</summary>
    private const int ExpectedMultiplyStoreCount = 6;

    /// <summary>ADD 1 and ADD 3 per round (4 + 4), ADD 0 per non-final round (3) and once after the loop.</summary>
    private const int ExpectedMultiplyRamOperandReadCount = 12;

    private const int StepBudget = 500;

    private static readonly TimeSpan UiBudget = TimeSpan.FromMilliseconds(100);

    /// <summary>STOREs 3, 5, 10, 12 to words 0–3, then reads every word back into ACC.</summary>
    private const string StoreAllReadBackSource =
        "LOAD 3\nSTORE 0\nLOAD 5\nSTORE 1\nLOAD 10\nSTORE 2\nLOAD 12\nSTORE 3\n" +
        "LOAD 0\nADD 0\nADD 1\nADD 2\nADD 3\nHALT";

    private readonly LogicGateRam4x4ExampleTests.Ram4x4Fixture _fixture;

    /// <summary>Attaches the shared RAM 4x4 fixture (loads and assembles once).</summary>
    public IsaPhotonicDataMemoryTraceTests(LogicGateRam4x4ExampleTests.Ram4x4Fixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void MultiplySample_OnPhotonicMemory_ProducesTheGoldenTrace_AndRoutesEveryAccess()
    {
        var program = AssembleShippedMultiplySample();
        var memory = new PhotonicDataMemory(_fixture.Network);

        // Counts run: no RAM snapshots, so the counts pin the operand traffic alone.
        memory.Reset();
        var countingRun = new IsaEmulator(program, dataMemory: memory);
        countingRun.Run(StepBudget);
        countingRun.IsHalted.ShouldBeTrue("multiply-3x4 must halt within the step budget on photonic memory");
        countingRun.Accumulator.ShouldBe(12, "the photonic memory must not change the result");
        memory.WriteCount.ShouldBe(ExpectedMultiplyStoreCount,
            "one write per executed STORE");
        memory.ReadCount.ShouldBe(ExpectedMultiplyRamOperandReadCount,
            "one read per executed RAM operand of ADD/AND");

        // Trace run: PC, ACC and RAM must match the golden model after every step.
        memory.Reset();
        var photonicTrace = RunToHalt(new IsaEmulator(program, dataMemory: memory));
        var goldenTrace = RunToHalt(new IsaEmulator(program));

        photonicTrace.ShouldBe(goldenTrace,
            "multiply-3x4.asm with its RAM on light must execute exactly as on the golden model");
    }

    [Fact]
    public void StoreAllFourWords_ReadBack_OnPhotonicMemory_ProducesTheGoldenTrace()
    {
        var program = new IsaAssembler().Assemble(StoreAllReadBackSource);
        var memory = new PhotonicDataMemory(_fixture.Network);

        memory.Reset();
        var countingRun = new IsaEmulator(program, dataMemory: memory);
        countingRun.Run(StepBudget);
        countingRun.IsHalted.ShouldBeTrue("the store-all/read-back program must halt on photonic memory");
        countingRun.Accumulator.ShouldBe((3 + 5 + 10 + 12) & IsaMachine.MaxDataValue);
        memory.WriteCount.ShouldBe(IsaMachine.RamWords, "one STORE per word");
        memory.ReadCount.ShouldBe(IsaMachine.RamWords, "one ADD per word reads each word back once");

        memory.Reset();
        var photonicTrace = RunToHalt(new IsaEmulator(program, dataMemory: memory));
        var goldenTrace = RunToHalt(new IsaEmulator(program));

        photonicTrace.ShouldBe(goldenTrace,
            "distinct words stored on light must read back exactly as on the golden model");
    }

    [Fact]
    public void Step_OnPhotonicMemory_StaysFarUnderUiBudget()
    {
        var program = AssembleShippedMultiplySample();
        var memory = new PhotonicDataMemory(_fixture.Network);

        // Warm-up: the first calls pay JIT and dictionary prime-up; the UI budget
        // applies to the steady-state step, so measure after a full first run.
        memory.Reset();
        new IsaEmulator(program, dataMemory: memory).Run(StepBudget);

        memory.Reset();
        var emulator = new IsaEmulator(program, dataMemory: memory);
        var worst = TimeSpan.Zero;
        while (!emulator.IsHalted)
        {
            var watch = Stopwatch.StartNew();
            emulator.Step();
            watch.Stop();
            if (watch.Elapsed > worst)
            {
                worst = watch.Elapsed;
            }
        }

        worst.ShouldBeLessThan(UiBudget,
            "one step with photonic data memory must stay far under the 100 ms UI budget");
    }

    /// <summary>Steps the machine to HALT, recording PC, ACC and RAM after every step.</summary>
    private static List<string> RunToHalt(IsaEmulator emulator)
    {
        var trace = new List<string>();
        while (!emulator.IsHalted && trace.Count < StepBudget)
        {
            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join(",", emulator.Ram)}]");
        }

        emulator.IsHalted.ShouldBeTrue($"the program did not halt within {StepBudget} steps");
        return trace;
    }

    private static byte[] AssembleShippedMultiplySample()
    {
        var sample = IsaSampleProgramCatalog.LoadDefault().Samples
            .Single(s => s.FileName == "multiply-3x4.asm");
        return new IsaAssembler().Assemble(sample.Source);
    }
}
