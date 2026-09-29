namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The ALU seam of the learning ISA (docs/ISA.md): the part of the machine that
    /// computes ADD. Two implementations exist — the golden C# model
    /// (<see cref="GoldenIsaAlu"/>) and the photonic 4-bit adder network
    /// (<see cref="PhotonicAdderAlu"/>) — so <see cref="IsaEmulator"/> can run the
    /// same program on either and the traces can be compared cycle by cycle.
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
    }
}
