namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The data-memory seam of the learning ISA (docs/ISA.md): the 4-word RAM that
    /// <c>STORE</c> writes and that <c>ADD</c>/<c>AND</c> read their RAM operand
    /// from. The golden C# model (<see cref="GoldenIsaDataMemory"/>) implements it
    /// today; a photonic register bank can implement it later so the same program
    /// keeps its data on light and the traces can be compared cycle by cycle.
    /// </summary>
    public interface IIsaDataMemory
    {
        /// <summary>
        /// Reads the data word at <paramref name="address"/>.
        /// </summary>
        /// <param name="address">RAM word address, 0 to <see cref="IsaMachine.RamWords"/> - 1.</param>
        /// <returns>The stored 4-bit word, 0–15.</returns>
        /// <exception cref="InvalidOperationException">The address is out of range.</exception>
        int Read(int address);

        /// <summary>
        /// Stores <paramref name="value"/> at <paramref name="address"/>, masked to
        /// the 4-bit data word (<see cref="IsaMachine.MaxDataValue"/>).
        /// </summary>
        /// <param name="address">RAM word address, 0 to <see cref="IsaMachine.RamWords"/> - 1.</param>
        /// <param name="value">The value to store; only the low 4 bits are kept.</param>
        /// <exception cref="InvalidOperationException">The address is out of range.</exception>
        void Write(int address, int value);

        /// <summary>Restores the power-on state: every word cleared to zero.</summary>
        void Reset();
    }
}
