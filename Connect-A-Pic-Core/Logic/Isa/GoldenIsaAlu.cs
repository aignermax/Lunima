namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The golden ALU: computes ADD, AND and NOT in C#, exactly as docs/ISA.md
    /// specifies (ADD wraps modulo 16; AND is <c>a &amp; b &amp; 0xF</c>; NOT is
    /// <c>~a &amp; 0xF</c>). This is the default <see cref="IIsaAlu"/> of
    /// <see cref="IsaEmulator"/> and the reference the photonic ALU is checked
    /// against cycle by cycle.
    /// </summary>
    public sealed class GoldenIsaAlu : IIsaAlu
    {
        /// <inheritdoc />
        public int Add(int a, int b) => (a + b) & IsaMachine.MaxDataValue;

        /// <inheritdoc />
        public int And(int a, int b) => a & b & IsaMachine.MaxDataValue;

        /// <inheritdoc />
        public int Not(int a) => ~a & IsaMachine.MaxDataValue;
    }
}
