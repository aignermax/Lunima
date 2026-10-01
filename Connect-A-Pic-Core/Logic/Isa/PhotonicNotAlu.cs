using CAP_Core.Analysis.LogicAnalysis;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// NOT on the photonic chip: wraps an assembled <see cref="LogicNetworkEvaluator"/>
    /// of the shipped 4-bit NOT network (the "Logic Gate NOT 4-bit" example,
    /// docs/ISA.md). Every NOT drives the operand bits A0–A3 and reads the result
    /// taps Y0–Y3 — or the names of a custom <see cref="IsaAluSignalMap"/>, so one
    /// chip can expose NOT next to another operation without a name collision. The
    /// network is validated at construction, so a design that does not expose the
    /// expected signal names fails loudly before the first instruction.
    /// </summary>
    public sealed class PhotonicNotAlu : IIsaAlu
    {
        private readonly LogicNetworkEvaluator _network;
        private readonly IsaAluSignalMap _signalMap;

        /// <summary>
        /// True when <paramref name="network"/> exposes every signal of
        /// <paramref name="signalMap"/> (default: the shipped names A0–A3 → Y0–Y3) —
        /// the check the constructor makes, without throwing, so callers can decide
        /// up-front whether a built network can compute NOT photonically. Additional
        /// inputs/outputs are allowed: extra inputs are tied to 0 on every
        /// <see cref="Not"/>.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network, IsaAluSignalMap? signalMap = null)
        {
            var map = signalMap ?? IsaAluSignalMap.Not;
            return network != null
                && map.OperandA.All(network.InputPinNames.Contains)
                && map.Result.All(network.OutputPinNames.Contains);
        }

        /// <summary>
        /// Wraps an assembled NOT network.
        /// </summary>
        /// <param name="network">
        /// The logic network of the 4-bit NOT. It must expose the input and output
        /// signals named by <paramref name="signalMap"/>.
        /// </param>
        /// <param name="signalMap">
        /// The operand/result signal names to drive and read; null uses the shipped
        /// names A0–A3 → Y0–Y3 (<see cref="IsaAluSignalMap.Not"/>).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicNotAlu(LogicNetworkEvaluator network, IsaAluSignalMap? signalMap = null)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _signalMap = signalMap ?? IsaAluSignalMap.Not;
            foreach (var signal in _signalMap.OperandA)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The NOT network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            foreach (var signal in _signalMap.Result)
            {
                if (!network.OutputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The NOT network is missing the output signal '{signal}'. " +
                        $"Declared outputs: {string.Join(", ", network.OutputPinNames)}.",
                        nameof(network));
                }
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// This unit is NOT-only: ADD falls back to the golden model. Use
        /// <see cref="CompositeIsaAlu"/> with a <see cref="PhotonicAdderAlu"/> to run
        /// both operations on photonic networks.
        /// </remarks>
        public int Add(int a, int b) => new GoldenIsaAlu().Add(a, b);

        /// <inheritdoc />
        /// <remarks>
        /// This unit is NOT-only: AND falls back to the golden model. Use
        /// <see cref="CompositeIsaAlu"/> with a <see cref="PhotonicAndAlu"/> to run
        /// both operations on photonic networks.
        /// </remarks>
        public int And(int a, int b) => new GoldenIsaAlu().And(a, b);

        /// <inheritdoc />
        public int Not(int a)
        {
            var bits = _network.InputPinNames.ToDictionary(name => name, _ => false);
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[_signalMap.OperandA[bit]] = ((a >> bit) & 1) == 1;
            }

            var outputs = _network.Evaluate(bits);
            var result = 0;
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                if (outputs[_signalMap.Result[bit]])
                {
                    result |= 1 << bit;
                }
            }

            return result;
        }
    }
}
