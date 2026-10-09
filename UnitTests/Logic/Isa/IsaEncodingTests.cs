using CAP_Core.Logic.Isa;
using Shouldly;

namespace UnitTests.Logic.Isa;

public class IsaEncodingTests
{
    public static IEnumerable<object[]> AllInstructions =>
        IsaInstruction.All.Select(instruction => new object[] { instruction });

    [Theory]
    [MemberData(nameof(AllInstructions))]
    public void EncodeDecode_RoundTrips_ForEveryOpcode(IsaInstruction instruction)
    {
        int operand = instruction.OperandKind == IsaOperandKind.None ? 0 : 3;

        byte encoded = instruction.Encode(operand);
        var decoded = IsaInstruction.Decode(encoded, out int decodedOperand);

        decoded.ShouldBe(instruction);
        decodedOperand.ShouldBe(operand);
    }

    [Theory]
    [MemberData(nameof(AllInstructions))]
    public void Encode_PlacesOpcodeInHighNibble_PerTable(IsaInstruction instruction)
    {
        byte encoded = instruction.Encode(2);

        (encoded >> 4).ShouldBe((int)instruction.Opcode);
        (encoded & 0xF).ShouldBe(instruction.OperandKind == IsaOperandKind.None ? 0 : 2);
    }

    [Fact]
    public void EncodingTable_ContainsExactlyEightInstructions()
    {
        IsaInstruction.All.Count.ShouldBe(8);
        IsaInstruction.All.Select(i => (int)i.Opcode).ShouldBe(new[] { 0, 1, 2, 3, 4, 5, 6, 7 });
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0x9F)]
    [InlineData(0xFF)]
    public void Decode_ReservedOpcode_ReturnsNull(byte word)
    {
        IsaInstruction.Decode(word, out _).ShouldBeNull();
    }

    [Theory]
    [InlineData("load", IsaOpcode.Load)]
    [InlineData("Add", IsaOpcode.Add)]
    [InlineData("HALT", IsaOpcode.Halt)]
    public void FindByMnemonic_IsCaseInsensitive(string mnemonic, IsaOpcode expected)
    {
        IsaInstruction.FindByMnemonic(mnemonic)!.Opcode.ShouldBe(expected);
    }

    [Fact]
    public void FindByMnemonic_Unknown_ReturnsNull()
    {
        IsaInstruction.FindByMnemonic("DIV").ShouldBeNull();
    }
}
