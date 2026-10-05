using CAP_Core.Analysis.LogicAnalysis;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The data memory on the photonic chip: wraps an assembled
    /// <see cref="LogicNetworkEvaluator"/> of the shipped RAM 4x4 network (the
    /// "Logic Gate RAM 4x4" example, docs/ISA.md) — a 2-bit address A0/A1, a LOAD
    /// strobe, data inputs D0–D3 and read taps Q0–Q3 over sixteen nested register
    /// bits. A <see cref="Write"/> drives the address, the data bits and LOAD=1 and
    /// commits one clock <see cref="LogicNetworkEvaluator.Step"/>; a
    /// <see cref="Read"/> drives the address with LOAD=0 and decodes Q0–Q3. Every
    /// <c>STORE</c> and every RAM-operand read of <c>ADD</c>/<c>AND</c> therefore
    /// lives in photonic registers, which moves the machine's data memory from C#
    /// onto light. The network is validated at construction, so a design that does
    /// not expose the expected signal names fails loudly before the first
    /// instruction.
    /// </summary>
    public sealed class PhotonicDataMemory : IIsaDataMemory
    {
        /// <summary>The LOAD strobe name the shipped RAM 4x4 example exposes.</summary>
        public const string LoadSignal = "LOAD";

        private readonly LogicNetworkEvaluator _network;

        /// <summary>
        /// True when <paramref name="network"/> exposes the address bits A0/A1, the
        /// LOAD strobe, the data inputs D0–D3 and the read taps Q0–Q3 — the check the
        /// constructor makes, without throwing, so callers can decide up-front whether
        /// a built network can hold the machine's data memory photonically.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network) =>
            network != null
            && RequiredInputNames.All(network.InputPinNames.Contains)
            && OutputBitNames.All(network.OutputPinNames.Contains);

        /// <summary>The address bit names, LSB first (A0, A1).</summary>
        public static IReadOnlyList<string> AddressBitNames { get; } = new[] { "A0", "A1" };

        /// <summary>The data input bit names, LSB first (D0–D3).</summary>
        public static IReadOnlyList<string> DataBitNames { get; } =
            Enumerable.Range(0, IsaMachine.DataBits).Select(bit => $"D{bit}").ToArray();

        /// <summary>The read tap names, LSB first (Q0–Q3).</summary>
        public static IReadOnlyList<string> OutputBitNames { get; } =
            Enumerable.Range(0, IsaMachine.DataBits).Select(bit => $"Q{bit}").ToArray();

        /// <summary>Every input the memory drives: address, LOAD and data bits.</summary>
        public static IReadOnlyList<string> RequiredInputNames { get; } =
            AddressBitNames.Concat(new[] { LoadSignal }).Concat(DataBitNames).ToArray();

        /// <summary>
        /// Wraps an assembled RAM 4x4 network.
        /// </summary>
        /// <param name="network">
        /// The logic network of the 4-words × 4-bits RAM. It must expose the input
        /// signals A0/A1, LOAD and D0–D3 and the output taps Q0–Q3.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicDataMemory(LogicNetworkEvaluator network)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            foreach (var signal in RequiredInputNames)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The RAM network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            foreach (var signal in OutputBitNames)
            {
                if (!network.OutputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The RAM network is missing the output signal '{signal}'. " +
                        $"Declared outputs: {string.Join(", ", network.OutputPinNames)}.",
                        nameof(network));
                }
            }
        }

        /// <summary>
        /// How many <see cref="Read"/> consultations this memory answered since
        /// construction — the trace seam that pins "the photonic RAM is read exactly
        /// once per executed RAM-operand instruction".
        /// </summary>
        public int ReadCount { get; private set; }

        /// <summary>
        /// How many <see cref="Write"/> commits this memory performed since
        /// construction — the trace seam that pins "the photonic RAM is written
        /// exactly once per executed <c>STORE</c>".
        /// </summary>
        public int WriteCount { get; private set; }

        /// <inheritdoc />
        public int Read(int address)
        {
            ReadCount++;
            var outputs = _network.Evaluate(Bits(address, load: false, data: 0));
            var value = 0;
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                if (outputs[OutputBitNames[bit]])
                {
                    value |= 1 << bit;
                }
            }

            return value;
        }

        /// <inheritdoc />
        public void Write(int address, int value)
        {
            WriteCount++;
            _network.Evaluate(Bits(address, load: true, value));
            _network.Step();
        }

        /// <inheritdoc />
        public void Reset() => _network.ResetRegisters();

        /// <summary>
        /// The network input bits for one address/LOAD/data triple: every declared
        /// input tied low unless it carries a bit of the triple.
        /// </summary>
        private Dictionary<string, bool> Bits(int address, bool load, int data)
        {
            var bits = _network.InputPinNames.ToDictionary(name => name, _ => false);
            for (var bit = 0; bit < AddressBitNames.Count; bit++)
            {
                bits[AddressBitNames[bit]] = ((address >> bit) & 1) == 1;
            }

            bits[LoadSignal] = load;
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[DataBitNames[bit]] = ((data >> bit) & 1) == 1;
            }

            return bits;
        }
    }
}
