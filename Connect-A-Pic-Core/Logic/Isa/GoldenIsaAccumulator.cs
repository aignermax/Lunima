namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The golden accumulator: a C# <c>int</c> masked to the 4-bit data word on
    /// write — exactly the behaviour <see cref="IsaEmulator"/> had before the
    /// accumulator became a seam. This is the default <see cref="IIsaAccumulator"/>
    /// of <see cref="IsaEmulator"/> and the reference a photonic accumulator is
    /// checked against.
    /// </summary>
    public sealed class GoldenIsaAccumulator : IIsaAccumulator
    {
        private int _value;

        /// <inheritdoc />
        public int Read()
        {
            return _value;
        }

        /// <inheritdoc />
        public void Write(int value)
        {
            _value = value & IsaMachine.MaxDataValue;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _value = 0;
        }
    }
}
