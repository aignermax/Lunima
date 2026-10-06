using CAP_Core.Logic.Isa;
using Shouldly;

namespace UnitTests.Logic.Isa;

public class IsaEmulatorTests
{
    private static IsaEmulator Load(string source)
    {
        return new IsaEmulator(new IsaAssembler().Assemble(source));
    }

    [Fact]
    public void Step_LoadImmediate_SetsAccumulatorAndAdvancesPc()
    {
        var emulator = Load("LOAD 7\nHALT");

        emulator.Step();

        emulator.Accumulator.ShouldBe(7);
        emulator.ProgramCounter.ShouldBe(1);
        emulator.IsHalted.ShouldBeFalse();
    }

    [Fact]
    public void Add_WrapsModulo16()
    {
        var emulator = Load("LOAD 15\nSTORE 0\nLOAD 1\nADD 0\nHALT");

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(0);
        emulator.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void And_MasksWithRamWord()
    {
        var emulator = Load("LOAD 12\nSTORE 0\nLOAD 10\nAND 0\nHALT");

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(8);
    }

    [Fact]
    public void Not_InvertsWithinFourBits()
    {
        var emulator = Load("LOAD 6\nNOT\nHALT");

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(9);
    }

    [Fact]
    public void Jz_TakenWhenAccumulatorIsZero()
    {
        var emulator = Load("LOAD 0\nJZ target\nLOAD 1\ntarget: HALT");

        emulator.Run(10);

        emulator.ProgramCounter.ShouldBe(4);
        emulator.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void Jz_NotTakenWhenAccumulatorIsNonZero()
    {
        var emulator = Load("LOAD 5\nJZ target\nLOAD 1\ntarget: HALT");

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(1);
        emulator.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void Step_AfterHalt_IsNoOp()
    {
        var emulator = Load("HALT");
        emulator.Step();
        int pc = emulator.ProgramCounter;

        emulator.Step();

        emulator.ProgramCounter.ShouldBe(pc);
        emulator.IsHalted.ShouldBeTrue();
    }

    [Fact]
    public void Run_RespectsStepBudget_OnInfiniteLoop()
    {
        var emulator = Load("loop: JMP loop");

        int steps = emulator.Run(50);

        steps.ShouldBe(50);
        emulator.IsHalted.ShouldBeFalse();
    }

    [Fact]
    public void Step_ReservedOpcode_Faults()
    {
        var emulator = new IsaEmulator(new byte[] { 0x80 });

        Should.Throw<InvalidOperationException>(() => emulator.Step());
    }

    [Fact]
    public void Step_RamAddressBeyondWord3_Faults()
    {
        var emulator = new IsaEmulator(new byte[] { 0x4F });

        Should.Throw<InvalidOperationException>(() => emulator.Step());
    }

    [Fact]
    public void Constructor_ProgramLongerThanRom_Throws()
    {
        Should.Throw<ArgumentException>(
            () => new IsaEmulator(new byte[IsaMachine.ProgramRomWords + 1]));
    }

    [Fact]
    public void Reset_RestoresPowerOnState()
    {
        var emulator = Load("LOAD 9\nSTORE 0\nHALT");
        emulator.Run(10);

        emulator.Reset();

        emulator.ProgramCounter.ShouldBe(0);
        emulator.Accumulator.ShouldBe(0);
        emulator.IsHalted.ShouldBeFalse();
        emulator.Ram.ShouldAllBe(word => word == 0);
    }
}
