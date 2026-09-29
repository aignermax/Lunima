using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The golden ALU pins the ADD semantics of docs/ISA.md: plain C# addition that
/// wraps modulo 16 — the reference the photonic ALU is checked against.
/// </summary>
public class GoldenIsaAluTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 4, 7)]
    [InlineData(15, 1, 0)]
    [InlineData(15, 15, 14)]
    [InlineData(9, 7, 0)]
    public void Add_WrapsModulo16(int a, int b, int expected)
    {
        new GoldenIsaAlu().Add(a, b).ShouldBe(expected);
    }
}
