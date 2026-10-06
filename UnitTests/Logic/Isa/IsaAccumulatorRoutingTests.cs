using System.Collections.Generic;
using System.Linq;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;

namespace UnitTests.Logic.Isa;

/// <summary>
/// Proves the accumulator seam of <see cref="IsaEmulator"/>: a counting
/// <see cref="IIsaAccumulator"/> fake must observe exactly one write per executed
/// <c>LOAD</c>/<c>ADD</c>/<c>AND</c>/<c>NOT</c> and one read per executed
/// <c>ADD</c>/<c>AND</c>/<c>NOT</c>/<c>STORE</c>/<c>JZ</c> when the shipped
/// multiply sample runs, and the golden and fake-backed traces of every shipped
/// sample must be identical.
/// </summary>
public class IsaAccumulatorRoutingTests
{
    /// <summary>
    /// 3 setup LOADs, 2 writes per loop round (ADD 1, ADD 3; 4 rounds), 2 writes
    /// per non-final round (LOAD 0, ADD 0; 3 rounds) and 2 after the loop.
    /// </summary>
    private const int ExpectedWriteCount = 19;

    /// <summary>
    /// 2 setup STOREs, 4 reads per loop round (ADD 1, STORE 0, ADD 3, JZ; 4 rounds),
    /// 1 read per non-final round (ADD 0; 3 rounds) and 1 after the loop.
    /// </summary>
    private const int ExpectedReadCount = 22;

    private const int StepBudget = 100;

    [Fact]
    public void MultiplySample_EveryAccumulatorWriteAndRead_GoesThroughAccumulator()
    {
        var program = AssembleShippedMultiplySample();
        var countingAccumulator = new CountingAccumulator();
        var emulator = new IsaEmulator(program, accumulator: countingAccumulator);

        emulator.Run(StepBudget);

        int writeCount = countingAccumulator.WriteCount;
        int readCount = countingAccumulator.ReadCount;
        emulator.IsHalted.ShouldBeTrue("multiply-3x4 must halt within the step budget");
        emulator.Accumulator.ShouldBe(12, "routing the ACC through the seam must not change the result");
        writeCount.ShouldBe(ExpectedWriteCount,
            "every executed LOAD/ADD must write the ACC through the accumulator");
        readCount.ShouldBe(ExpectedReadCount,
            "every executed ADD/STORE/JZ must read the ACC through the accumulator");
        countingAccumulator.ResetCount.ShouldBe(0, "Run alone must not reset the accumulator");
    }

    [Fact]
    public void EmulatorReset_ResetsAccumulator()
    {
        var countingAccumulator = new CountingAccumulator();
        var emulator = new IsaEmulator(new byte[1], accumulator: countingAccumulator);

        emulator.Reset();

        countingAccumulator.ResetCount.ShouldBe(1);
    }

    [Fact]
    public void Accumulator_ReadsThroughAccumulator()
    {
        var countingAccumulator = new CountingAccumulator();
        var emulator = new IsaEmulator(new byte[1], accumulator: countingAccumulator);
        countingAccumulator.Write(9);

        emulator.Accumulator.ShouldBe(9);
    }

    [Theory]
    [InlineData("count-to-5.asm")]
    [InlineData("add-two-numbers.asm")]
    [InlineData("multiply-3x4.asm")]
    [InlineData("mask-and-invert.asm")]
    public void ShippedSample_GoldenAndFakeBackedTraces_AreIdentical(string fileName)
    {
        var sample = IsaSampleProgramCatalog.LoadDefault().Samples
            .Single(s => s.FileName == fileName);
        byte[] program = new IsaAssembler().Assemble(sample.Source);

        var golden = new IsaEmulator(program);
        var fakeBacked = new IsaEmulator(program, accumulator: new CountingAccumulator());
        var goldenTrace = new List<string>();
        var fakeTrace = new List<string>();

        while (!golden.IsHalted && goldenTrace.Count < StepBudget)
        {
            golden.Step();
            fakeBacked.Step();
            goldenTrace.Add(TraceLine(golden));
            fakeTrace.Add(TraceLine(fakeBacked));
        }

        golden.IsHalted.ShouldBeTrue($"{fileName} must halt within the step budget");
        fakeTrace.ShouldBe(goldenTrace,
            "routing the ACC through the seam must not change any cycle of the trace");
    }

    private static string TraceLine(IsaEmulator emulator)
    {
        return $"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
               $"RAM=[{string.Join(",", emulator.Ram)}] Halted={emulator.IsHalted}";
    }

    private static byte[] AssembleShippedMultiplySample()
    {
        var sample = IsaSampleProgramCatalog.LoadDefault().Samples
            .Single(s => s.FileName == "multiply-3x4.asm");
        return new IsaAssembler().Assemble(sample.Source);
    }

    /// <summary>Golden accumulator that counts every seam call.</summary>
    private sealed class CountingAccumulator : IIsaAccumulator
    {
        private readonly GoldenIsaAccumulator _inner = new();

        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int ResetCount { get; private set; }

        public int Read()
        {
            ReadCount++;
            return _inner.Read();
        }

        public void Write(int value)
        {
            WriteCount++;
            _inner.Write(value);
        }

        public void Reset()
        {
            ResetCount++;
            _inner.Reset();
        }
    }
}
