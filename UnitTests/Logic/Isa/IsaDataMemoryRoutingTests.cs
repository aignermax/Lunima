using System.Linq;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;

namespace UnitTests.Logic.Isa;

/// <summary>
/// Proves the data-memory seam of <see cref="IsaEmulator"/>: a counting
/// <see cref="IIsaDataMemory"/> fake must observe exactly one write per executed
/// <c>STORE</c> and one read per executed RAM operand of <c>ADD</c>/<c>AND</c>
/// when the shipped multiply sample runs, and the result must match the golden
/// model bit for bit.
/// </summary>
public class IsaDataMemoryRoutingTests
{
    /// <summary>STORE 1, STORE 3 plus one STORE 0 per loop round (4 rounds).</summary>
    private const int ExpectedStoreCount = 6;

    /// <summary>ADD 1 and ADD 3 per round (4 + 4), ADD 0 per non-final round (3) and once after the loop.</summary>
    private const int ExpectedRamOperandReadCount = 12;

    private const int StepBudget = 100;

    [Fact]
    public void MultiplySample_EveryStoreAndRamOperandRead_GoesThroughDataMemory()
    {
        var program = AssembleShippedMultiplySample();
        var countingMemory = new CountingDataMemory();
        var emulator = new IsaEmulator(program, dataMemory: countingMemory);

        emulator.Run(StepBudget);

        emulator.IsHalted.ShouldBeTrue("multiply-3x4 must halt within the step budget");
        emulator.Accumulator.ShouldBe(12, "routing the RAM through the seam must not change the result");
        countingMemory.WriteCount.ShouldBe(ExpectedStoreCount,
            "every executed STORE must write through the data memory");
        countingMemory.ReadCount.ShouldBe(ExpectedRamOperandReadCount,
            "every executed RAM operand of ADD/AND must read through the data memory");
        countingMemory.ResetCount.ShouldBe(0, "Run alone must not reset the memory");
    }

    [Fact]
    public void EmulatorReset_ResetsDataMemory()
    {
        var countingMemory = new CountingDataMemory();
        var emulator = new IsaEmulator(new byte[1], dataMemory: countingMemory);

        emulator.Reset();

        countingMemory.ResetCount.ShouldBe(1);
    }

    [Fact]
    public void Ram_ReadsThroughDataMemory()
    {
        var countingMemory = new CountingDataMemory();
        var emulator = new IsaEmulator(new byte[1], dataMemory: countingMemory);
        countingMemory.Write(3, 9);

        emulator.Ram.ShouldBe(new[] { 0, 0, 0, 9 });
    }

    private static byte[] AssembleShippedMultiplySample()
    {
        var sample = IsaSampleProgramCatalog.LoadDefault().Samples
            .Single(s => s.FileName == "multiply-3x4.asm");
        return new IsaAssembler().Assemble(sample.Source);
    }

    /// <summary>Golden data memory that counts every seam call.</summary>
    private sealed class CountingDataMemory : IIsaDataMemory
    {
        private readonly GoldenIsaDataMemory _inner = new();

        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int ResetCount { get; private set; }

        public int Read(int address)
        {
            ReadCount++;
            return _inner.Read(address);
        }

        public void Write(int address, int value)
        {
            WriteCount++;
            _inner.Write(address, value);
        }

        public void Reset()
        {
            ResetCount++;
            _inner.Reset();
        }
    }
}
