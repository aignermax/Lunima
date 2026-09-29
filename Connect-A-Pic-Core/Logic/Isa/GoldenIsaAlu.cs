namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The golden ALU: computes ADD in C#, exactly as docs/ISA.md specifies
    /// (wrap modulo 16). This is the default <see cref="IIsaAlu"/> of
    /// <see cref="IsaEmulator"/> and the reference the photonic ALU is checked
    /// against cycle by cycle.
    /// </summary>
    public sealed class GoldenIsaAlu : IIsaAlu
    {
        /// <inheritdoc />
        public int Add(int a, int b) => (a + b) & IsaMachine.MaxDataValue;
    }
}
