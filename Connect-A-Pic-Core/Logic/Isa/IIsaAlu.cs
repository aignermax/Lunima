namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The ALU seam of the learning ISA (docs/ISA.md): the part of the machine that
    /// computes ADD and NOT. The golden C# model (<see cref="GoldenIsaAlu"/>) and the
    /// photonic units (<see cref="PhotonicAdderAlu"/>, <see cref="PhotonicNotAlu"/>)
    /// implement it, so <see cref="IsaEmulator"/> can run the same program on either
    /// and the traces can be compared cycle by cycle. <see cref="CompositeIsaAlu"/>
    /// picks the implementation per operation, so ADD and NOT can independently be
    /// photonic or golden.
    /// </summary>
    public interface IIsaAlu
    {
        /// <summary>
        /// Adds two 4-bit data words. The result wraps modulo 16
        /// (<see cref="IsaMachine.MaxDataValue"/>); there is no carry-out flag.
        /// </summary>
        /// <param name="a">First addend, 0–15.</param>
        /// <param name="b">Second addend, 0–15.</param>
        /// <returns>The sum modulo 16.</returns>
        int Add(int a, int b);

        /// <summary>
        /// Bitwise-inverts a 4-bit data word (<c>~a &amp; 0xF</c>).
        /// </summary>
        /// <param name="a">The value to invert, 0–15.</param>
        /// <returns>The inverted value, 0–15.</returns>
        int Not(int a);
    }
}
