using CAP_Core.Analysis.LogicAnalysis;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The zero flag on the photonic chip: wraps an assembled
    /// <see cref="LogicNetworkEvaluator"/> of the shipped 4-bit zero-detect network (the
    /// "Logic Gate Zero Detect 4-bit" example, docs/ISA.md) — three OR slices in a tree
    /// feeding one NOT slice, so the flag tap reads Z = NOT(A0 OR A1 OR A2 OR A3). Every
    /// <c>JZ</c> drives the operand bits A0–A3 with the accumulator and reads Z, which
    /// moves the machine's one branch decision from C# onto light. The network is
    /// validated at construction, so a design that does not expose the expected signal
    /// names fails loudly before the first instruction.
    /// </summary>
    public sealed class PhotonicZeroFlag : IIsaZeroFlag
    {
        /// <summary>The flag tap name the shipped zero-detect example exposes.</summary>
        public const string DefaultZeroSignal = "Z";

        private readonly LogicNetworkEvaluator _network;
        private readonly string _zeroSignal;

        /// <summary>
        /// True when <paramref name="network"/> exposes every operand bit A0–A3 and the
        /// flag tap <paramref name="zeroSignal"/> — the check the constructor makes,
        /// without throwing, so callers can decide up-front whether a built network can
        /// decide <c>JZ</c> photonically. Additional inputs/outputs are allowed: extra
        /// inputs are tied to 0 on every <see cref="IsZero"/>.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network, string zeroSignal = DefaultZeroSignal) =>
            network != null
            && InputBitNames.All(network.InputPinNames.Contains)
            && network.OutputPinNames.Contains(zeroSignal);

        /// <summary>The operand bit names the flag drives, LSB first (A0–A3).</summary>
        public static IReadOnlyList<string> InputBitNames { get; } =
            Enumerable.Range(0, IsaMachine.DataBits).Select(bit => $"A{bit}").ToArray();

        /// <summary>
        /// Wraps an assembled zero-detect network.
        /// </summary>
        /// <param name="network">
        /// The logic network of the 4-bit zero detect. It must expose the input signals
        /// A0–A3 and the flag tap <paramref name="zeroSignal"/>.
        /// </param>
        /// <param name="zeroSignal">
        /// The name of the output tap that reads the flag; defaults to
        /// <see cref="DefaultZeroSignal"/>, the name the shipped example uses.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicZeroFlag(LogicNetworkEvaluator network, string zeroSignal = DefaultZeroSignal)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _zeroSignal = zeroSignal;
            foreach (var signal in InputBitNames)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The zero-detect network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            if (!network.OutputPinNames.Contains(zeroSignal))
            {
                throw new ArgumentException(
                    $"The zero-detect network is missing the output signal '{zeroSignal}'. " +
                    $"Declared outputs: {string.Join(", ", network.OutputPinNames)}.",
                    nameof(network));
            }
        }

        /// <summary>
        /// How many <see cref="IsZero"/> consultations this flag answered since
        /// construction — the trace seam that pins "the flag is consulted exactly
        /// once per executed <c>JZ</c>" (the same role
        /// <see cref="PhotonicAdderAlu.LastAddTrace"/> plays for photonic ADDs).
        /// </summary>
        public int ConsultationCount { get; private set; }

        /// <inheritdoc />
        public bool IsZero(int value)
        {
            ConsultationCount++;
            var bits = _network.InputPinNames.ToDictionary(name => name, _ => false);
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[InputBitNames[bit]] = ((value >> bit) & 1) == 1;
            }

            return _network.Evaluate(bits)[_zeroSignal];
        }
    }
}
