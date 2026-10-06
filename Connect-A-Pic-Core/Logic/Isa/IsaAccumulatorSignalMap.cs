namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The signal names a photonic accumulator drives (data-in word, load enable)
    /// and reads (data-out taps) on its <c>LogicNetworkEvaluator</c> — the
    /// accumulator analogue of <see cref="IsaDataMemorySignalMap"/>. The
    /// <see cref="Default"/> map is prefixed (<c>ACC.D0</c>–<c>ACC.D3</c>,
    /// <c>ACC.LOAD</c> → <c>ACC.Q0</c>–<c>ACC.Q3</c>) from the start: the
    /// accumulator shares its machine with an ALU and a data RAM whose signal
    /// maps use the plain <c>D</c>/<c>LOAD</c>/<c>Q</c> names, so the ACC signals
    /// must never collide on a shared rung-5 network (docs/ISA.md).
    /// </summary>
    public sealed record IsaAccumulatorSignalMap
    {
        /// <summary>The prefix every default accumulator signal carries.</summary>
        public const string DefaultPrefix = "ACC.";

        /// <summary>
        /// Creates a map. <paramref name="dataIn"/> and <paramref name="dataOut"/>
        /// must name exactly <see cref="IsaMachine.DataBits"/> signals each, LSB
        /// first; <paramref name="load"/> names the load-enable input.
        /// </summary>
        /// <exception cref="ArgumentException">A signal list has the wrong length.</exception>
        public IsaAccumulatorSignalMap(IEnumerable<string> dataIn, string load, IEnumerable<string> dataOut)
        {
            DataIn = ToBitList(dataIn, nameof(dataIn));
            Load = load ?? throw new ArgumentNullException(nameof(load));
            DataOut = ToBitList(dataOut, nameof(dataOut));
        }

        /// <summary>Data-in bit names, LSB first.</summary>
        public IReadOnlyList<string> DataIn { get; }

        /// <summary>Load-enable input name.</summary>
        public string Load { get; }

        /// <summary>Read-tap bit names, LSB first.</summary>
        public IReadOnlyList<string> DataOut { get; }

        /// <summary>Every input signal the accumulator drives: data-in and load.</summary>
        public IEnumerable<string> AllInputs => DataIn.Concat(new[] { Load });

        /// <summary>The default prefixed names: ACC.D0–ACC.D3, ACC.LOAD → ACC.Q0–ACC.Q3.</summary>
        public static IsaAccumulatorSignalMap Default { get; } = new(
            Bits("D"), DefaultPrefix + "LOAD", Bits("Q"));

        private static string[] Bits(string name)
        {
            var bits = new string[IsaMachine.DataBits];
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[bit] = $"{DefaultPrefix}{name}{bit}";
            }

            return bits;
        }

        private static string[] ToBitList(IEnumerable<string> signals, string parameterName)
        {
            var list = (signals ?? throw new ArgumentNullException(parameterName)).ToArray();
            if (list.Length != IsaMachine.DataBits)
            {
                throw new ArgumentException(
                    $"An accumulator signal map needs exactly {IsaMachine.DataBits} '{parameterName}' names " +
                    $"(LSB first), got {list.Length}.",
                    parameterName);
            }

            return list;
        }
    }
}
