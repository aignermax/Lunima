using CAP_Core.Logic.Isa;
using Shouldly;

namespace UnitTests.Logic.Isa;

public class GoldenIsaDataMemoryTests
{
    [Fact]
    public void WriteThenRead_RoundTripsValue()
    {
        var memory = new GoldenIsaDataMemory();

        memory.Write(2, 11);

        memory.Read(2).ShouldBe(11);
    }

    [Fact]
    public void Write_MasksValueToFourBits()
    {
        var memory = new GoldenIsaDataMemory();

        memory.Write(0, 0x1F);

        memory.Read(0).ShouldBe(15, "only the low 4 bits of a written value are kept");
    }

    [Fact]
    public void Words_StartAtZero()
    {
        var memory = new GoldenIsaDataMemory();

        for (int address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Read(address).ShouldBe(0);
        }
    }

    [Fact]
    public void Reset_ClearsAllWords()
    {
        var memory = new GoldenIsaDataMemory();
        for (int address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Write(address, address + 1);
        }

        memory.Reset();

        for (int address = 0; address < IsaMachine.RamWords; address++)
        {
            memory.Read(address).ShouldBe(0);
        }
    }

    [Fact]
    public void Read_OutOfRangeAddress_Faults()
    {
        var memory = new GoldenIsaDataMemory();

        Should.Throw<InvalidOperationException>(() => memory.Read(IsaMachine.RamWords));
    }

    [Fact]
    public void Write_OutOfRangeAddress_Faults()
    {
        var memory = new GoldenIsaDataMemory();

        Should.Throw<InvalidOperationException>(() => memory.Write(IsaMachine.RamWords, 1));
    }
}
