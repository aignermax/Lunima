using System.Collections.ObjectModel;
using System.Globalization;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// A single instruction definition of the learning ISA: opcode, mnemonic and
    /// operand kind. <see cref="All"/> is the single source of truth for the fixed
    /// 8-bit encoding table documented in docs/ISA.md — high nibble is the opcode,
    /// low nibble is the operand.
    /// </summary>
    public sealed class IsaInstruction
    {
        private static readonly IsaInstruction[] Table =
        {
            new(IsaOpcode.Load, "LOAD", IsaOperandKind.Immediate),
            new(IsaOpcode.Add, "ADD", IsaOperandKind.RamAddress),
            new(IsaOpcode.And, "AND", IsaOperandKind.RamAddress),
            new(IsaOpcode.Not, "NOT", IsaOperandKind.None),
            new(IsaOpcode.Store, "STORE", IsaOperandKind.RamAddress),
            new(IsaOpcode.Jmp, "JMP", IsaOperandKind.CodeAddress),
            new(IsaOpcode.Jz, "JZ", IsaOperandKind.CodeAddress),
            new(IsaOpcode.Halt, "HALT", IsaOperandKind.None),
        };

        private IsaInstruction(IsaOpcode opcode, string mnemonic, IsaOperandKind operandKind)
        {
            Opcode = opcode;
            Mnemonic = mnemonic;
            OperandKind = operandKind;
        }

        /// <summary>All instruction definitions, indexed by opcode value.</summary>
        public static IReadOnlyList<IsaInstruction> All { get; } =
            new ReadOnlyCollection<IsaInstruction>(Table);

        /// <summary>The opcode, occupying the high nibble of the encoded word.</summary>
        public IsaOpcode Opcode { get; }

        /// <summary>The assembler mnemonic (uppercase, case-insensitive on input).</summary>
        public string Mnemonic { get; }

        /// <summary>What the low nibble of the encoded word means for this instruction.</summary>
        public IsaOperandKind OperandKind { get; }

        /// <summary>
        /// Encodes this instruction with the given operand into one 8-bit word.
        /// The operand is masked to its low 4 bits; operand-less instructions
        /// always encode 0 in the low nibble.
        /// </summary>
        /// <param name="operand">Operand value (range-checked by the assembler, not here).</param>
        /// <returns>The encoded instruction byte.</returns>
        public byte Encode(int operand)
        {
            int nibble = OperandKind == IsaOperandKind.None ? 0 : operand & IsaMachine.MaxDataValue;
            return (byte)(((int)Opcode << IsaMachine.DataBits) | nibble);
        }

        /// <summary>
        /// Decodes one 8-bit word into its instruction definition and operand nibble.
        /// </summary>
        /// <param name="word">The encoded instruction byte.</param>
        /// <param name="operand">The low nibble of the word.</param>
        /// <returns>The instruction definition, or null for a reserved opcode (0x8–0xF).</returns>
        public static IsaInstruction? Decode(byte word, out int operand)
        {
            operand = word & IsaMachine.MaxDataValue;
            int opcodeValue = word >> IsaMachine.DataBits;
            return opcodeValue < Table.Length ? Table[opcodeValue] : null;
        }

        /// <summary>
        /// Finds an instruction definition by mnemonic, case-insensitively.
        /// </summary>
        /// <param name="mnemonic">The mnemonic to look up (e.g. "LOAD").</param>
        /// <returns>The instruction definition, or null when the mnemonic is unknown.</returns>
        public static IsaInstruction? FindByMnemonic(string mnemonic)
        {
            foreach (var instruction in Table)
            {
                if (string.Equals(instruction.Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase))
                {
                    return instruction;
                }
            }

            return null;
        }

        /// <summary>
        /// The largest legal operand value for this instruction's operand kind.
        /// </summary>
        /// <returns>The inclusive upper bound, or 0 for operand-less instructions.</returns>
        public int MaxOperand()
        {
            return OperandKind switch
            {
                IsaOperandKind.Immediate => IsaMachine.MaxDataValue,
                IsaOperandKind.RamAddress => IsaMachine.RamWords - 1,
                IsaOperandKind.CodeAddress => IsaMachine.ProgramRomWords - 1,
                _ => 0,
            };
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Create(CultureInfo.InvariantCulture, $"0x{(int)Opcode:X} {Mnemonic}");
        }
    }
}
