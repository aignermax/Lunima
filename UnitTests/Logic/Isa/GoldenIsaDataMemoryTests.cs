using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The golden data memory: the C# array behaviour <see cref="IsaEmulator"/> had
/// before the <see cref="IIsaDataMemory"/> seam existed — power-up zero, 4-bit
/// wrap on write, independent words, address bounds and <see cref="IIsaDataMemory.Reset"/>.
/// </summary>
public class GoldenIsaDataMemoryTests
{
    [Fact]
    public void Read_AtPowerUp_ReturnsZeroForEveryWord()
    {
        var memory = new GoldenIsaDataMemory();
        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Read(address).ShouldBe(0, $"word {address} powers up cleared");
        }
    }

    [Fact]
    public void WriteThenRead_RoundTripsEveryWordIndependently()
    {
        var memory = new GoldenIsaDataMemory();
        var patterns = new[] { 3, 5, 10, 12 };
        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Write(address, patterns[address]);
        }

        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Read(address).ShouldBe(patterns[address], $"word {address} holds its own value");
        }
    }

    [Fact]
    public void Write_KeepsOnlyTheLowFourBits()
    {
        var memory = new GoldenIsaDataMemory();
        memory.Write(0, 0x1F);
        memory.Read(0).ShouldBe(0xF, "the data word is four bits; arithmetic wraps modulo 16");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void ReadWrite_AddressOutOfRange_Throws(int address)
    {
        var memory = new GoldenIsaDataMemory();
        Should.Throw<ArgumentOutOfRangeException>(() => memory.Read(address));
        Should.Throw<ArgumentOutOfRangeException>(() => memory.Write(address, 1));
    }

    [Fact]
    public void Reset_ClearsEveryWord()
    {
        var memory = new GoldenIsaDataMemory();
        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Write(address, address + 1);
        }

        memory.Reset();

        for (var address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Read(address).ShouldBe(0, $"word {address} returns to the power-on state");
        }
    }

    [Fact]
    public void Emulator_WithoutDataMemory_KeepsTheGoldenArrayBehaviour()
    {
        var program = new IsaAssembler().Assemble("LOAD 7\nSTORE 2\nLOAD 0\nADD 2\nHALT");
        var emulator = new IsaEmulator(program);

        emulator.Run(10);

        emulator.Accumulator.ShouldBe(7, "the default data memory stays the golden C# array");
        emulator.Ram[2].ShouldBe(7);
        emulator.IsHalted.ShouldBeTrue();
    }
}
