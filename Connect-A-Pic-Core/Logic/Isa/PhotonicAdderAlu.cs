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
    /// </summary>
    public sealed class PhotonicAdderAlu : IIsaAlu
    {
        private static readonly string[] OperandSignals =
            { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3", "Cin" };

        private static readonly string[] SumSignals = { "S0", "S1", "S2", "S3" };

        private readonly LogicNetworkEvaluator _network;

        /// <summary>
        /// True when <paramref name="network"/> exposes every signal the ALU drives
        /// (A0–A3, B0–B3, Cin) and reads (S0–S3) — the check the constructor makes,
        /// without throwing, so callers can decide up-front whether a built network
        /// can compute ADD photonically. Additional inputs/outputs are allowed:
        /// extra inputs are tied to 0 on every <see cref="Add"/>.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network) =>
            network != null
            && OperandSignals.All(network.InputPinNames.Contains)
            && SumSignals.All(network.OutputPinNames.Contains);

        /// <summary>
        /// Wraps an assembled adder network.
        /// </summary>
        /// <param name="network">
        /// The logic network of the 4-bit adder. It must expose the input signals
        /// A0–A3, B0–B3 and Cin and the output signals S0–S3.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicAdderAlu(LogicNetworkEvaluator network)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            foreach (var signal in OperandSignals)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The adder network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            foreach (var signal in SumSignals)
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
                bits[$"A{bit}"] = ((a >> bit) & 1) == 1;
                bits[$"B{bit}"] = ((b >> bit) & 1) == 1;
            }

            var outputs = _network.Evaluate(bits);
            var sum = 0;
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                if (outputs[$"S{bit}"])
                {
                    sum |= 1 << bit;
                }
            }

            return sum;
        }
    }
}
