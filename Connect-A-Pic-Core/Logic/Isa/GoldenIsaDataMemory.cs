namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The golden data memory: a C# array of <see cref="IsaMachine.RamWords"/> words,
    /// exactly the behaviour <see cref="IsaEmulator"/> had before the seam existed.
    /// This is the default <see cref="IIsaDataMemory"/> of <see cref="IsaEmulator"/>
    /// and the reference the photonic data memory is checked against.
    /// </summary>
    public sealed class GoldenIsaDataMemory : IIsaDataMemory
    {
        private readonly int[] _words = new int[IsaMachine.RamWords];

        /// <inheritdoc />
        public int Read(int address)
        {
            CheckAddress(address);
            return _words[address];
        }

        /// <inheritdoc />
        public void Write(int address, int value)
        {
            CheckAddress(address);
            _words[address] = value & IsaMachine.MaxDataValue;
        }

        /// <inheritdoc />
        public void Reset() => Array.Clear(_words);

        private static void CheckAddress(int address)
        {
            if (address < 0 || address >= IsaMachine.RamWords)
            {
                throw new ArgumentOutOfRangeException(nameof(address), address,
                    $"RAM address {address} is out of range; the RAM has {IsaMachine.RamWords} words.");
            }
        }
    }
}
