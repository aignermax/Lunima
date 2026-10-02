namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The golden zero flag: the C# check <c>value == 0</c>, exactly as docs/ISA.md
    /// specifies <c>JZ</c> (<c>PC := addr</c> if <c>ACC == 0</c>). This is the default
    /// <see cref="IIsaZeroFlag"/> of <see cref="IsaEmulator"/> and the reference the
    /// photonic zero flag is checked against.
    /// </summary>
    public sealed class GoldenIsaZeroFlag : IIsaZeroFlag
    {
        /// <inheritdoc />
        public bool IsZero(int value) => value == 0;
    }
}
