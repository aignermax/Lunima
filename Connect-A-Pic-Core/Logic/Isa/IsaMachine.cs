namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// Machine dimensions of the tiny 4-bit learning ISA specified in docs/ISA.md.
    /// </summary>
    public static class IsaMachine
    {
        /// <summary>Width of the data word (accumulator and RAM cells) in bits.</summary>
        public const int DataBits = 4;

        /// <summary>Largest value a data word can hold (all arithmetic wraps modulo 16).</summary>
        public const int MaxDataValue = (1 << DataBits) - 1;

        /// <summary>Number of words in the program ROM (one byte per instruction).</summary>
        public const int ProgramRomWords = 16;

        /// <summary>Number of words in the data RAM.</summary>
        public const int RamWords = 4;
    }
}
