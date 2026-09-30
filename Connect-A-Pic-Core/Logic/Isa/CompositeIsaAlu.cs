namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// An ALU that delegates ADD and NOT to two separate <see cref="IIsaAlu"/>
    /// implementations, so each operation can independently be photonic or golden —
    /// e.g. <c>new CompositeIsaAlu(new PhotonicAdderAlu(adder), new PhotonicNotAlu(not))</c>
    /// runs the whole ALU on the chip.
    /// </summary>
    public sealed class CompositeIsaAlu : IIsaAlu
    {
        private readonly IIsaAlu _add;
        private readonly IIsaAlu _not;

        /// <summary>
        /// <paramref name="add"/> computes every ADD, <paramref name="not"/> every NOT.
        /// </summary>
        public CompositeIsaAlu(IIsaAlu add, IIsaAlu not)
        {
            _add = add ?? throw new ArgumentNullException(nameof(add));
            _not = not ?? throw new ArgumentNullException(nameof(not));
        }

        /// <inheritdoc />
        public int Add(int a, int b) => _add.Add(a, b);

        /// <inheritdoc />
        public int Not(int a) => _not.Not(a);
    }
}
