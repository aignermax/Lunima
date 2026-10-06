using CAP_Core.Logic.Isa;
using Shouldly;

namespace UnitTests.Logic.Isa;

public class GoldenIsaAccumulatorTests
{
    [Fact]
    public void WriteThenRead_RoundTripsValue()
    {
        var accumulator = new GoldenIsaAccumulator();

        accumulator.Write(11);

        accumulator.Read().ShouldBe(11);
    }

    [Fact]
    public void Write_MasksValueToFourBits()
    {
        var accumulator = new GoldenIsaAccumulator();

        accumulator.Write(0x1F);

        accumulator.Read().ShouldBe(15, "only the low 4 bits of a written value are kept");
    }

    [Fact]
    public void Value_StartsAtZero()
    {
        new GoldenIsaAccumulator().Read().ShouldBe(0);
    }

    [Fact]
    public void Reset_ClearsValue()
    {
        var accumulator = new GoldenIsaAccumulator();
        accumulator.Write(7);

        accumulator.Reset();

        accumulator.Read().ShouldBe(0);
    }
}
