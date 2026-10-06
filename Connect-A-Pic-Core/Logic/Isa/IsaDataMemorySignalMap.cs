namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The signal names a photonic data memory drives (address bits, load enable,
    /// data-in word) and reads (data-out taps) on its <c>LogicNetworkEvaluator</c> —
    /// the RAM analogue of <see cref="IsaAluSignalMap"/>. The <see cref="Default"/>
    /// map is exactly the names the shipped "Logic Gate RAM 4x4" example exposes
    /// (A0–A1, LOAD, D0–D3, Q0–Q3), so existing callers and networks stay unchanged;
    /// <see cref="WithPrefix"/> moves every name under a prefix (e.g.
    /// <c>RAM.A0</c>, <c>RAM.LOAD</c>) so one chip can expose the data memory next to
    /// an ALU whose operand bits reuse the plain names — the rung-5 chip where RAM
    /// and ALU share one network (docs/ISA.md).
    /// </summary>
    public sealed record IsaDataMemorySignalMap
    {
        /// <summary>The address-bit count of the ISA RAM: four words need two bits.</summary>
        public const int AddressBitCount = 2;

        /// <summary>
        /// Creates a map. <paramref name="address"/> must name exactly
        /// <see cref="AddressBitCount"/> signals, LSB first; <paramref name="dataIn"/>
        /// and <paramref name="dataOut"/> must name exactly <see cref="IsaMachine.DataBits"/>
        /// signals each, LSB first; <paramref name="load"/> names the load-enable input.
        /// </summary>
        /// <exception cref="ArgumentException">A signal list has the wrong length.</exception>
        public IsaDataMemorySignalMap(
            IEnumerable<string> address,
            string load,
            IEnumerable<string> dataIn,
            IEnumerable<string> dataOut)
        {
            Address = ToBitList(address, AddressBitCount, nameof(address));
            Load = load ?? throw new ArgumentNullException(nameof(load));
            DataIn = ToBitList(dataIn, IsaMachine.DataBits, nameof(dataIn));
            DataOut = ToBitList(dataOut, IsaMachine.DataBits, nameof(dataOut));
        }

        /// <summary>Address bit names, LSB first.</summary>
        public IReadOnlyList<string> Address { get; }

        /// <summary>Load-enable input name.</summary>
        public string Load { get; }

        /// <summary>Data-in bit names, LSB first.</summary>
        public IReadOnlyList<string> DataIn { get; }

        /// <summary>Read-tap bit names, LSB first.</summary>
        public IReadOnlyList<string> DataOut { get; }

        /// <summary>Every input signal the memory drives: address, load and data-in.</summary>
        public IEnumerable<string> AllInputs => Address.Concat(new[] { Load }).Concat(DataIn);

        /// <summary>The shipped RAM 4x4 names: A0–A1, LOAD, D0–D3 → Q0–Q3.</summary>
        public static IsaDataMemorySignalMap Default { get; } = new(
            Bits("A", AddressBitCount), "LOAD", Bits("D", IsaMachine.DataBits), Bits("Q", IsaMachine.DataBits));

        /// <summary>
        /// The default names moved under <paramref name="prefix"/>: with
        /// <c>"RAM."</c> the address bits become <c>RAM.A0</c>/<c>RAM.A1</c>, the load
        /// enable <c>RAM.LOAD</c> and so on, so the memory signals cannot collide
        /// with the plain ALU operand names on a shared network.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="prefix"/> is null or empty.</exception>
        public static IsaDataMemorySignalMap WithPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                throw new ArgumentException("A signal-map prefix must not be empty.", nameof(prefix));
            }

            return new IsaDataMemorySignalMap(
                Default.Address.Select(name => prefix + name),
                prefix + Default.Load,
                Default.DataIn.Select(name => prefix + name),
                Default.DataOut.Select(name => prefix + name));
        }

        private static string[] Bits(string prefix, int count)
        {
            var bits = new string[count];
            for (var bit = 0; bit < count; bit++)
            {
                bits[bit] = $"{prefix}{bit}";
            }

            return bits;
        }

        private static string[] ToBitList(IEnumerable<string> signals, int expected, string parameterName)
        {
            var list = (signals ?? throw new ArgumentNullException(parameterName)).ToArray();
            if (list.Length != expected)
            {
                throw new ArgumentException(
                    $"A data-memory signal map needs exactly {expected} '{parameterName}' names " +
                    $"(LSB first), got {list.Length}.",
                    parameterName);
            }

            return list;
        }
    }
}
