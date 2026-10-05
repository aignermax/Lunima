namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The zero-flag seam of the learning ISA (docs/ISA.md): the part of the machine
    /// that decides whether the accumulator is zero — the one condition <c>JZ</c>
    /// branches on. The golden C# model (<see cref="GoldenIsaZeroFlag"/>) and the
    /// photonic zero-detect network (<see cref="PhotonicZeroFlag"/>) implement it, so
    /// <see cref="IsaEmulator"/> can run the same program with the branch decision made
    /// in C# or on light and the traces can be compared cycle by cycle.
    /// </summary>
    public interface IIsaZeroFlag
    {
        /// <summary>
        /// Reports whether a 4-bit data word is zero — the <c>JZ</c> branch condition.
        /// </summary>
        /// <param name="value">The accumulator value to test, 0–15.</param>
        /// <returns>True when the value is zero.</returns>
        bool IsZero(int value);
    }
}
