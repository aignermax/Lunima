using CAP_Core.Analysis.LogicAnalysis;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The ISA zero flag on the photonic chip: wraps an assembled
    /// <see cref="LogicNetworkEvaluator"/> of a zero-detect network (the shipped
    /// "Logic Gate Zero Detect 4-bit" example, docs/ISA.md) — Z = 1 exactly when the
    /// 4-bit word is 0, i.e. Z = NOT(A0 OR A1 OR A2 OR A3). Every evaluation drives
    /// the word bits A0–A3 and reads the flag tap Z — or custom signal names, so one
    /// chip can expose the flag next to other operations without a name collision.
    /// The network is validated at construction, so a design that does not expose the
    /// expected signal names fails loudly before the first instruction. Pass
    /// <see cref="IsZero"/> to <see cref="IsaEmulator"/> to let <c>JZ</c> branch on
    /// light instead of on the golden <c>== 0</c> check.
    /// </summary>
    public sealed class PhotonicZeroFlag
    {
        /// <summary>The shipped input signal names, one per bit of the data word.</summary>
        public static readonly string[] DefaultInputSignals = { "A0", "A1", "A2", "A3" };

        /// <summary>The shipped flag tap name.</summary>
        public const string DefaultOutputSignal = "Z";

        private readonly LogicNetworkEvaluator _network;
        private readonly string[] _inputSignals;
        private readonly string _outputSignal;

        /// <summary>
        /// True when <paramref name="network"/> exposes the four input signals
        /// (default: A0–A3) and the flag tap (default: Z) — the check the constructor
        /// makes, without throwing, so callers can decide up-front whether a built
        /// network can evaluate the zero flag photonically. Additional inputs/outputs
        /// are allowed: extra inputs are tied to 0 on every <see cref="IsZero"/>.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network) =>
            network != null
            && DefaultInputSignals.All(network.InputPinNames.Contains)
            && network.OutputPinNames.Contains(DefaultOutputSignal);

        /// <summary>
        /// Wraps an assembled zero-detect network.
        /// </summary>
        /// <param name="network">
        /// The logic network of the zero detect. It must expose the input signals
        /// <paramref name="inputSignals"/> and the flag tap <paramref name="outputSignal"/>.
        /// </param>
        /// <param name="inputSignals">
        /// The word-bit signal names to drive, LSB first; null uses the shipped names
        /// A0–A3 (<see cref="DefaultInputSignals"/>).
        /// </param>
        /// <param name="outputSignal">The flag tap to read; the shipped name is Z.</param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicZeroFlag(
            LogicNetworkEvaluator network,
            IReadOnlyList<string>? inputSignals = null,
            string outputSignal = DefaultOutputSignal)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _inputSignals = (inputSignals ?? DefaultInputSignals).ToArray();
            if (_inputSignals.Length != IsaMachine.DataBits)
            {
                throw new ArgumentException(
                    $"The zero flag needs exactly {IsaMachine.DataBits} input signals " +
                    $"but got {_inputSignals.Length}.",
                    nameof(inputSignals));
            }

            _outputSignal = outputSignal;
            foreach (var signal in _inputSignals)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The zero-detect network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            if (!network.OutputPinNames.Contains(outputSignal))
            {
                throw new ArgumentException(
                    $"The zero-detect network is missing the output signal '{outputSignal}'. " +
                    $"Declared outputs: {string.Join(", ", network.OutputPinNames)}.",
                    nameof(network));
            }
        }

        /// <summary>
        /// How often the flag was evaluated — the counter a conformance run compares
        /// against the number of executed <c>JZ</c> instructions to prove the branch
        /// decision really went through the photonic network.
        /// </summary>
        public int EvaluationCount { get; private set; }

        /// <summary>
        /// Evaluates the zero flag for one 4-bit data word: true exactly when
        /// <paramref name="value"/> is 0. Only the low <see cref="IsaMachine.DataBits"/>
        /// bits are driven; any higher bits are ignored.
        /// </summary>
        public bool IsZero(int value)
        {
            var bits = _network.InputPinNames.ToDictionary(name => name, _ => false);
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[_inputSignals[bit]] = ((value >> bit) & 1) == 1;
            }

            EvaluationCount++;
            return _network.Evaluate(bits)[_outputSignal];
        }
    }
}
