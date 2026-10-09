namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The accumulator seam of the learning ISA (docs/ISA.md): the single 4-bit
    /// register that <c>LOAD</c>/<c>ADD</c>/<c>AND</c>/<c>NOT</c> write and that
    /// <c>ADD</c>/<c>AND</c>/<c>NOT</c>/<c>STORE</c> and the <c>JZ</c> zero check
    /// read. The golden C# model (<see cref="GoldenIsaAccumulator"/>) implements
    /// it today; a photonic register can implement it later so the same program
    /// keeps its working value on light and the traces can be compared cycle by
    /// cycle.
    /// </summary>
    public interface IIsaAccumulator
    {
        /// <summary>
        /// Reads the current accumulator value.
        /// </summary>
        /// <returns>The stored 4-bit word, 0–15.</returns>
        int Read();

        /// <summary>
        /// Stores <paramref name="value"/>, masked to the 4-bit data word
        /// (<see cref="IsaMachine.MaxDataValue"/>).
        /// </summary>
        /// <param name="value">The value to store; only the low 4 bits are kept.</param>
        void Write(int value);

        /// <summary>Restores the power-on state: the register cleared to zero.</summary>
        void Reset();
    }
}
