namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The opcodes of the 4-bit learning ISA. The opcode value occupies the high
    /// nibble of every fixed-length 8-bit instruction word.
    /// </summary>
    public enum IsaOpcode
    {
        /// <summary>ACC := immediate operand.</summary>
        Load = 0x0,

        /// <summary>ACC := (ACC + RAM[operand]) mod 16.</summary>
        Add = 0x1,

        /// <summary>ACC := ACC bitwise-and RAM[operand].</summary>
        And = 0x2,

        /// <summary>ACC := bitwise-not ACC, masked to 4 bits.</summary>
        Not = 0x3,

        /// <summary>RAM[operand] := ACC.</summary>
        Store = 0x4,

        /// <summary>PC := operand.</summary>
        Jmp = 0x5,

        /// <summary>PC := operand when ACC == 0, else PC := PC + 1.</summary>
        Jz = 0x6,

        /// <summary>Stops the machine; further steps are no-ops.</summary>
        Halt = 0x7,
    }
}
