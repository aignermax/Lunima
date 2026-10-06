namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The kind of operand an instruction expects, encoded in the low nibble of the
    /// instruction word.
    /// </summary>
    public enum IsaOperandKind
    {
        /// <summary>No operand; the low nibble is encoded as 0 and ignored on decode.</summary>
        None,

        /// <summary>A 4-bit immediate value (0–15).</summary>
        Immediate,

        /// <summary>An address into the 4-word data RAM (0–3).</summary>
        RamAddress,

        /// <summary>An address into the 16-word program ROM (0–15); may be written as a label.</summary>
        CodeAddress,
    }
}
