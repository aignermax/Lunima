using CAP_Core.Analysis.LogicAnalysis;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// ADD on the photonic chip: wraps an assembled <see cref="LogicNetworkEvaluator"/>
    /// of the shipped 4-bit ripple-carry adder (the "Logic Gate 4-Bit Adder" example,
    /// docs/ISA.md). Every ADD drives the operand bits A0–A3 and B0–B3 with Cin tied
    /// to 0 and reads the sum taps S0–S3; Cout is dropped, which is exactly the ISA's
    /// mod-16 wrap. The network is validated at construction, so a design that does
    /// not expose the expected signal names fails loudly before the first instruction.
    /// Every ADD also records a <see cref="LastAddTrace"/>: the operands, the sum and
    /// the light-travel time of that addition from the event-timeline kernel.
    /// </summary>
    public sealed class PhotonicAdderAlu : IIsaAlu
    {
        private readonly LogicNetworkEvaluator _network;
        private readonly IsaAluSignalMap _map;
        private IReadOnlyDictionary<string, bool>? _previousInputs;

        /// <summary>
        /// The operands, sum and light-travel time of the most recent <see cref="Add"/>,
        /// or null before the first one.
        /// </summary>
        public PhotonicAddTrace? LastAddTrace { get; private set; }

        /// <summary>
        /// True when <paramref name="network"/> exposes every signal of
        /// <paramref name="map"/> (defaults to <see cref="IsaAluSignalMap.AdderDefault"/>:
        /// drives A0–A3, B0–B3 and Cin, reads S0–S3) — the check the constructor makes,
        /// without throwing, so callers can decide up-front whether a built network
        /// can compute ADD photonically. Additional inputs/outputs are allowed:
        /// extra inputs are tied to 0 on every <see cref="Add"/>.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network, IsaAluSignalMap? map = null)
        {
            map ??= IsaAluSignalMap.AdderDefault;
            return network != null
                && map.Inputs.All(network.InputPinNames.Contains)
                && map.Result.All(network.OutputPinNames.Contains);
        }

        /// <summary>
        /// Wraps an assembled adder network.
        /// </summary>
        /// <param name="network">
        /// The logic network of the 4-bit adder. It must expose the operand,
        /// carry-in and sum signals of <paramref name="map"/>.
        /// </param>
        /// <param name="map">
        /// The signal names to drive and read; defaults to
        /// <see cref="IsaAluSignalMap.AdderDefault"/> (A0–A3 &amp; B0–B3, Cin → S0–S3).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicAdderAlu(LogicNetworkEvaluator network, IsaAluSignalMap? map = null)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _map = map ?? IsaAluSignalMap.AdderDefault;
            if (_map.OperandB == null || _map.CarryIn == null)
            {
                throw new ArgumentException(
                    "The adder map must name the second operand (operandB) and the carry-in.",
                    nameof(map));
            }

            foreach (var signal in _map.Inputs)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The adder network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            foreach (var signal in _map.Result)
            {
                if (!network.OutputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The adder network is missing the output signal '{signal}'. " +
                        $"Declared outputs: {string.Join(", ", network.OutputPinNames)}.",
                        nameof(network));
                }
            }
        }

        /// <inheritdoc />
        public int Add(int a, int b)
        {
            var bits = _network.InputPinNames.ToDictionary(name => name, _ => false);
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[_map.OperandA[bit]] = ((a >> bit) & 1) == 1;
                bits[_map.OperandB![bit]] = ((b >> bit) & 1) == 1;
            }

            var outputs = _network.Evaluate(bits);
            var sum = 0;
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                if (outputs[_map.Result[bit]])
                {
                    sum |= 1 << bit;
                }
            }

            LastAddTrace = new PhotonicAddTrace(a, b, sum, LightTravelPicoseconds(bits));
            _previousInputs = bits;
            return sum;
        }

        /// <inheritdoc />
        /// <remarks>
        /// This unit is ADD-only: AND falls back to the golden model. Use
        /// <see cref="CompositeIsaAlu"/> with a <see cref="PhotonicAndAlu"/> to run
        /// both operations on photonic networks.
        /// </remarks>
        public int And(int a, int b) => new GoldenIsaAlu().And(a, b);

        /// <inheritdoc />
        /// <remarks>
        /// This unit is ADD-only: NOT falls back to the golden model. Use
        /// <see cref="CompositeIsaAlu"/> with a <see cref="PhotonicNotAlu"/> to run
        /// both operations on photonic networks.
        /// </remarks>
        public int Not(int a) => new GoldenIsaAlu().Not(a);

        /// <summary>
        /// Arrival time of the latest switching network output when the operand bits
        /// move from the previously driven assignment (all-zero at power-on) to
        /// <paramref name="nextInputs"/>. Reuses the Logic tab's event-timeline kernel,
        /// so the number matches what the timeline reports for the same input toggle.
        /// </summary>
        private double LightTravelPicoseconds(IReadOnlyDictionary<string, bool> nextInputs)
        {
            var previous = _previousInputs
                ?? _network.InputPinNames.ToDictionary(name => name, _ => false);
            var outputPins = new HashSet<LogicPinRef>(_network.OutputTaps.Values);
            return LogicEventTimeline.Compute(_network, previous, nextInputs)
                .Where(e => outputPins.Contains(new LogicPinRef(e.GateId, e.OutputPin)))
                .Select(e => e.TimePicoseconds)
                .DefaultIfEmpty(0.0)
                .Max();
        }
    }
}
