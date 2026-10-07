using CAP_Core.Analysis.LogicAnalysis;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The accumulator on the photonic chip: wraps an assembled
    /// <see cref="LogicNetworkEvaluator"/> that holds a 4-bit register with load
    /// enable (data in <c>ACC.D0</c>–<c>ACC.D3</c>, <c>ACC.LOAD</c>, read taps
    /// <c>ACC.Q0</c>–<c>ACC.Q3</c> — or the names of a custom
    /// <see cref="IsaAccumulatorSignalMap"/>). Every <see cref="Write"/> drives the
    /// data bits with LOAD high and runs one clock <see cref="LogicNetworkEvaluator.Step"/>,
    /// then releases LOAD; every <see cref="Read"/> evaluates the output taps
    /// combinationally. Extra network inputs are tied to 0 on every access, as in
    /// <see cref="PhotonicDataMemory"/>, so the ACC can share a network with the
    /// ALU and the RAM. The network is validated at construction, so a design that
    /// does not expose the expected signal names fails loudly before the first
    /// instruction, and the traces can be compared against
    /// <see cref="GoldenIsaAccumulator"/> cycle by cycle.
    /// </summary>
    public sealed class PhotonicAccumulator : IIsaAccumulator
    {
        private readonly LogicNetworkEvaluator _network;
        private readonly IsaAccumulatorSignalMap _signalMap;

        /// <summary>
        /// True when <paramref name="network"/> exposes every signal of
        /// <paramref name="signalMap"/> (default: the prefixed names ACC.D0–ACC.D3,
        /// ACC.LOAD → ACC.Q0–ACC.Q3) — the check the constructor makes, without
        /// throwing, so callers can decide up-front whether a built network can
        /// serve as the photonic accumulator. Additional inputs/outputs are
        /// allowed: extra inputs are tied to 0 on every access.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network, IsaAccumulatorSignalMap? signalMap = null)
        {
            var map = signalMap ?? IsaAccumulatorSignalMap.Default;
            return network != null
                && map.AllInputs.All(network.InputPinNames.Contains)
                && map.DataOut.All(network.OutputPinNames.Contains);
        }

        /// <summary>
        /// Wraps an assembled 4-bit register network.
        /// </summary>
        /// <param name="network">
        /// The logic network holding the accumulator register. It must expose the
        /// input and output signals named by <paramref name="signalMap"/>.
        /// </param>
        /// <param name="signalMap">
        /// The data/load signal names to drive and read; null uses the prefixed
        /// default names (<see cref="IsaAccumulatorSignalMap.Default"/>).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicAccumulator(LogicNetworkEvaluator network, IsaAccumulatorSignalMap? signalMap = null)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _signalMap = signalMap ?? IsaAccumulatorSignalMap.Default;
            foreach (var signal in _signalMap.AllInputs)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The accumulator network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            foreach (var tap in _signalMap.DataOut)
            {
                if (!network.OutputPinNames.Contains(tap))
                {
                    throw new ArgumentException(
                        $"The accumulator network is missing the output signal '{tap}'. " +
                        $"Declared outputs: {string.Join(", ", network.OutputPinNames)}.",
                        nameof(network));
                }
            }
        }

        /// <inheritdoc />
        public int Read()
        {
            var outputs = _network.Evaluate(AllInputsZero());
            int value = 0;
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                if (outputs[_signalMap.DataOut[bit]])
                {
                    value |= 1 << bit;
                }
            }

            return value;
        }

        /// <inheritdoc />
        public void Write(int value)
        {
            var bits = AllInputsZero();
            bits[_signalMap.Load] = true;
            int masked = value & IsaMachine.MaxDataValue;
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[_signalMap.DataIn[bit]] = ((masked >> bit) & 1) == 1;
            }

            _network.Evaluate(bits);
            _network.Step();

            bits[_signalMap.Load] = false;
            _network.Evaluate(bits);
        }

        /// <inheritdoc />
        public void Reset()
        {
            _network.ResetRegisters();
        }

        private Dictionary<string, bool> AllInputsZero() =>
            _network.InputPinNames.ToDictionary(name => name, _ => false);
    }
}
