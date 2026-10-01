namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// An ALU that delegates ADD, AND and NOT to separate <see cref="IIsaAlu"/>
    /// implementations, so each operation can independently be photonic or golden —
    /// e.g. <c>new CompositeIsaAlu(new PhotonicAdderAlu(adder), new PhotonicNotAlu(not),
    /// new PhotonicAndAlu(and))</c> runs the whole ALU on the chip.
    /// </summary>
    public sealed class CompositeIsaAlu : IIsaAlu
    {
        private readonly IIsaAlu _add;
        private readonly IIsaAlu _not;
        private readonly IIsaAlu _and;

        /// <summary>
        /// <paramref name="add"/> computes every ADD, <paramref name="not"/> every NOT;
        /// AND falls back to the golden model.
        /// </summary>
        public CompositeIsaAlu(IIsaAlu add, IIsaAlu not)
            : this(add, not, new GoldenIsaAlu())
        {
        }

        /// <summary>
        /// <paramref name="add"/> computes every ADD, <paramref name="not"/> every NOT,
        /// <paramref name="and"/> every AND.
        /// </summary>
        public CompositeIsaAlu(IIsaAlu add, IIsaAlu not, IIsaAlu and)
        {
            _add = add ?? throw new ArgumentNullException(nameof(add));
            _not = not ?? throw new ArgumentNullException(nameof(not));
            _and = and ?? throw new ArgumentNullException(nameof(and));
        }

        /// <inheritdoc />
        public int Add(int a, int b) => _add.Add(a, b);

        /// <inheritdoc />
        public int And(int a, int b) => _and.And(a, b);

        /// <inheritdoc />
        public int Not(int a) => _not.Not(a);
    }
}
