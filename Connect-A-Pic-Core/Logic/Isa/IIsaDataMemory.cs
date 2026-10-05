namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The data-memory seam of the learning ISA (docs/ISA.md): the four 4-bit words
    /// that <c>STORE</c> writes and the RAM operands of <c>ADD</c>/<c>AND</c> read.
    /// The golden C# model (<see cref="GoldenIsaDataMemory"/>) and the photonic RAM
    /// 4x4 network (<see cref="PhotonicDataMemory"/>) implement it, so
    /// <see cref="IsaEmulator"/> can run the same program with the data memory held
    /// in a C# array or in photonic registers and the traces can be compared cycle
    /// by cycle.
    /// </summary>
    public interface IIsaDataMemory
    {
        /// <summary>
        /// Reads the 4-bit word at <paramref name="address"/> (0–3).
        /// </summary>
        /// <param name="address">The word address, 0 to <see cref="IsaMachine.RamWords"/> − 1.</param>
        /// <returns>The stored word, 0–15.</returns>
        int Read(int address);

        /// <summary>
        /// Stores the 4-bit word <paramref name="value"/> at <paramref name="address"/>.
        /// </summary>
        /// <param name="address">The word address, 0 to <see cref="IsaMachine.RamWords"/> − 1.</param>
        /// <param name="value">The word to store; only the low <see cref="IsaMachine.DataBits"/> bits are kept.</param>
        void Write(int address, int value);

        /// <summary>
        /// Returns every word to the power-on state (all zero).
        /// </summary>
        void Reset();
    }
}
