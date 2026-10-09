namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The golden data memory: a C# <c>int[]</c> of <see cref="IsaMachine.RamWords"/>
    /// words, values masked to the 4-bit data word on write — exactly the behaviour
    /// <see cref="IsaEmulator"/> had before the memory became a seam. This is the
    /// default <see cref="IIsaDataMemory"/> of <see cref="IsaEmulator"/> and the
    /// reference a photonic data memory is checked against.
    /// </summary>
    public sealed class GoldenIsaDataMemory : IIsaDataMemory
    {
        private readonly int[] _words = new int[IsaMachine.RamWords];

        /// <inheritdoc />
        public int Read(int address)
        {
            return _words[CheckedAddress(address)];
        }

        /// <inheritdoc />
        public void Write(int address, int value)
        {
            _words[CheckedAddress(address)] = value & IsaMachine.MaxDataValue;
        }

        /// <inheritdoc />
        public void Reset()
        {
            Array.Clear(_words);
        }

        private static int CheckedAddress(int address)
        {
            if (address < 0 || address >= IsaMachine.RamWords)
            {
                throw new InvalidOperationException(
                    $"RAM address {address} is out of range; the RAM has {IsaMachine.RamWords} words.");
            }

            return address;
        }
    }
}
