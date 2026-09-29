using CAP_Core.Logic.Isa;
using Shouldly;

namespace UnitTests.Logic.Isa;

public class IsaAssemblerTests
{
    private readonly IsaAssembler _assembler = new();

    [Fact]
    public void Assemble_SimpleProgram_ProducesExpectedBytes()
    {
        byte[] words = _assembler.Assemble("LOAD 7\nSTORE 0\nHALT");

        words.ShouldBe(new byte[] { 0x07, 0x40, 0x70 });
    }

    [Fact]
    public void Assemble_IgnoresCommentsAndBlankLines()
    {
        byte[] words = _assembler.Assemble("; heading\n\n  LOAD 3 ; trailing comment\n\nHALT\n");

        words.ShouldBe(new byte[] { 0x03, 0x70 });
    }

    [Fact]
    public void Assemble_ResolvesLabels_OnOwnLineAndInline()
    {
        var source = "start: LOAD 0\nJMP done\nLOAD 1\ndone:\nHALT";

        byte[] words = _assembler.Assemble(source);

        words.ShouldBe(new byte[] { 0x00, 0x53, 0x01, 0x70 });
    }

    [Fact]
    public void Assemble_UnknownMnemonic_ThrowsWithLineNumber()
    {
        var source = "LOAD 1\nFROB 2\nHALT";

        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble(source));

        ex.LineNumber.ShouldBe(2);
        ex.Message.ShouldContain("Line 2");
        ex.Message.ShouldContain("FROB");
    }

    [Fact]
    public void Assemble_UndefinedLabel_ThrowsWithLineNumber()
    {
        var source = "LOAD 1\nJZ nowhere";

        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble(source));

        ex.LineNumber.ShouldBe(2);
        ex.Message.ShouldContain("nowhere");
    }

    [Fact]
    public void Assemble_ImmediateOutOfRange_ThrowsWithLineNumber()
    {
        var source = "LOAD 16";

        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble(source));

        ex.LineNumber.ShouldBe(1);
        ex.Message.ShouldContain("out of range");
    }

    [Fact]
    public void Assemble_RamAddressOutOfRange_ThrowsWithLineNumber()
    {
        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble("STORE 4"));

        ex.LineNumber.ShouldBe(1);
        ex.Message.ShouldContain("0–3");
    }

    [Fact]
    public void Assemble_OperandlessInstructionWithOperand_Throws()
    {
        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble("HALT 1"));

        ex.LineNumber.ShouldBe(1);
    }

    [Fact]
    public void Assemble_MissingOperand_Throws()
    {
        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble("LOAD"));

        ex.LineNumber.ShouldBe(1);
    }

    [Fact]
    public void Assemble_NonNumericOperand_Throws()
    {
        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble("LOAD three"));

        ex.LineNumber.ShouldBe(1);
        ex.Message.ShouldContain("decimal number");
    }

    [Fact]
    public void Assemble_DuplicateLabel_Throws()
    {
        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble("a: HALT\na: HALT"));

        ex.LineNumber.ShouldBe(2);
        ex.Message.ShouldContain("Duplicate label");
    }

    [Fact]
    public void Assemble_ProgramLongerThanRom_Throws()
    {
        var source = string.Join('\n', Enumerable.Repeat("NOT", IsaMachine.ProgramRomWords + 1));

        var ex = Should.Throw<IsaAssemblerException>(() => _assembler.Assemble(source));

        ex.Message.ShouldContain("ROM");
    }

    [Fact]
    public void Assemble_HandlesWindowsLineEndings()
    {
        byte[] words = _assembler.Assemble("LOAD 5\r\nHALT\r\n");

        words.ShouldBe(new byte[] { 0x05, 0x70 });
    }
}
