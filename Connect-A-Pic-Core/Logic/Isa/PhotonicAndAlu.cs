using CAP_Core.Analysis.LogicAnalysis;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// AND on the photonic chip: wraps an assembled <see cref="LogicNetworkEvaluator"/>
    /// of the shipped 4-bit AND network (the "Logic Gate AND 4-bit" example,
    /// docs/ISA.md). Every AND drives the operand bits A0–A3 and B0–B3 and reads the
    /// result taps Y0–Y3. The network is validated at construction, so a design that
    /// does not expose the expected signal names fails loudly before the first
    /// instruction.
    /// </summary>
    public sealed class PhotonicAndAlu : IIsaAlu
    {
        private readonly LogicNetworkEvaluator _network;
        private readonly IsaAluSignalMap _map;

        /// <summary>
        /// True when <paramref name="network"/> exposes every signal of
        /// <paramref name="map"/> (defaults to <see cref="IsaAluSignalMap.AndDefault"/>:
        /// drives A0–A3 and B0–B3, reads Y0–Y3) — the check the constructor makes,
        /// without throwing, so callers can decide up-front whether a built network
        /// can compute AND photonically. Additional inputs/outputs are allowed: extra
        /// inputs are tied to 0 on every <see cref="And"/>.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network, IsaAluSignalMap? map = null)
        {
            map ??= IsaAluSignalMap.AndDefault;
            return network != null
                && map.Inputs.All(network.InputPinNames.Contains)
                && map.Result.All(network.OutputPinNames.Contains);
        }

        /// <summary>
        /// Wraps an assembled AND network.
        /// </summary>
        /// <param name="network">
        /// The logic network of the 4-bit AND. It must expose the operand and
        /// result signals of <paramref name="map"/>.
        /// </param>
        /// <param name="map">
        /// The signal names to drive and read; defaults to
        /// <see cref="IsaAluSignalMap.AndDefault"/> (A0–A3 &amp; B0–B3 → Y0–Y3),
        /// which the combined logic-unit chip keeps for its AND.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicAndAlu(LogicNetworkEvaluator network, IsaAluSignalMap? map = null)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _map = map ?? IsaAluSignalMap.AndDefault;
            if (_map.OperandB == null)
            {
                throw new ArgumentException(
                    "The AND map must name the second operand (operandB).", nameof(map));
            }

            foreach (var signal in _map.Inputs)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The AND network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            foreach (var signal in _map.Result)
            {
                if (!network.OutputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The AND network is missing the output signal '{signal}'. " +
                        $"Declared outputs: {string.Join(", ", network.OutputPinNames)}.",
                        nameof(network));
                }
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// This unit is AND-only: ADD and NOT fall back to the golden model. Use
        /// <see cref="CompositeIsaAlu"/> to combine it with the other photonic units.
        /// </remarks>
        public int Add(int a, int b) => new GoldenIsaAlu().Add(a, b);

        /// <inheritdoc />
        public int And(int a, int b)
        {
            var bits = _network.InputPinNames.ToDictionary(name => name, _ => false);
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[_map.OperandA[bit]] = ((a >> bit) & 1) == 1;
                bits[_map.OperandB![bit]] = ((b >> bit) & 1) == 1;
            }

            var outputs = _network.Evaluate(bits);
            var result = 0;
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                if (outputs[_map.Result[bit]])
                {
                    result |= 1 << bit;
                }
            }

            return result;
        }

        /// <inheritdoc />
        /// <remarks>
        /// This unit is AND-only: NOT falls back to the golden model. Use
        /// <see cref="CompositeIsaAlu"/> with a <see cref="PhotonicNotAlu"/> to run
        /// both operations on photonic networks.
        /// </remarks>
        public int Not(int a) => new GoldenIsaAlu().Not(a);
    }
}
