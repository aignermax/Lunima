using CAP_Core.Analysis.LogicAnalysis;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The data memory on the photonic chip: wraps an assembled
    /// <see cref="LogicNetworkEvaluator"/> of the shipped ISA-sized RAM (the
    /// "Logic Gate RAM 4x4" example, docs/ISA.md — address <c>A0</c>/<c>A1</c>,
    /// <c>LOAD</c>, data in <c>D0</c>–<c>D3</c>, read taps <c>Q0</c>–<c>Q3</c>,
    /// sixteen nested register bits). Every <c>STORE</c> drives the address and
    /// data inputs with LOAD high and runs one clock <see cref="LogicNetworkEvaluator.Step"/>;
    /// every RAM-operand read drives the address with LOAD low and reads the Q taps —
    /// or the names of a custom <see cref="IsaDataMemorySignalMap"/>, so one chip can
    /// expose the RAM next to an ALU without a name collision —
    /// so the program's memory lives in photonic registers and the traces can be
    /// compared against <see cref="GoldenIsaDataMemory"/> cycle by cycle. The network
    /// is validated at construction, so a design that does not expose the expected
    /// signal names fails loudly before the first instruction.
    /// </summary>
    public sealed class PhotonicDataMemory : IIsaDataMemory
    {
        /// <summary>The load-enable name the shipped RAM 4x4 example exposes.</summary>
        public const string DefaultLoadSignal = "LOAD";

        private readonly LogicNetworkEvaluator _network;
        private readonly IsaDataMemorySignalMap _signalMap;

        /// <summary>The address bit names, LSB first (A0, A1).</summary>
        public static IReadOnlyList<string> AddressBitNames => IsaDataMemorySignalMap.Default.Address;

        /// <summary>The data-in bit names, LSB first (D0–D3).</summary>
        public static IReadOnlyList<string> DataInBitNames => IsaDataMemorySignalMap.Default.DataIn;

        /// <summary>The read-tap bit names, LSB first (Q0–Q3).</summary>
        public static IReadOnlyList<string> DataOutBitNames => IsaDataMemorySignalMap.Default.DataOut;

        /// <summary>
        /// True when <paramref name="network"/> exposes every signal of
        /// <paramref name="signalMap"/> (default: the shipped names A0–A1, LOAD,
        /// D0–D3 → Q0–Q3) — the check the constructor makes, without throwing, so
        /// callers can decide up-front whether a built network can serve as the
        /// photonic data memory. Additional inputs/outputs are allowed: extra inputs
        /// are tied to 0 on every access.
        /// </summary>
        public static bool Accepts(LogicNetworkEvaluator? network, IsaDataMemorySignalMap? signalMap = null)
        {
            var map = signalMap ?? IsaDataMemorySignalMap.Default;
            return network != null
                && map.AllInputs.All(network.InputPinNames.Contains)
                && map.DataOut.All(network.OutputPinNames.Contains);
        }

        /// <summary>
        /// Wraps an assembled RAM 4x4 network.
        /// </summary>
        /// <param name="network">
        /// The logic network of the ISA-sized RAM. It must expose the input and
        /// output signals named by <paramref name="signalMap"/>.
        /// </param>
        /// <param name="signalMap">
        /// The address/load/data signal names to drive and read; null uses the
        /// shipped names A0–A1, LOAD, D0–D3 → Q0–Q3
        /// (<see cref="IsaDataMemorySignalMap.Default"/>). Pass a prefixed map
        /// (<see cref="IsaDataMemorySignalMap.WithPrefix"/>) when the network
        /// exposes the RAM next to an ALU that reuses the plain names.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The network does not expose one of the expected signals; the message names
        /// the first missing signal.
        /// </exception>
        public PhotonicDataMemory(LogicNetworkEvaluator network, IsaDataMemorySignalMap? signalMap = null)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _signalMap = signalMap ?? IsaDataMemorySignalMap.Default;
            foreach (var signal in _signalMap.AllInputs)
            {
                if (!network.InputPinNames.Contains(signal))
                {
                    throw new ArgumentException(
                        $"The data-memory network is missing the input signal '{signal}'. " +
                        $"Declared inputs: {string.Join(", ", network.InputPinNames)}.",
                        nameof(network));
                }
            }

            foreach (var tap in _signalMap.DataOut)
            {
                if (!network.OutputPinNames.Contains(tap))
                {
                    throw new ArgumentException(
                        $"The data-memory network is missing the output signal '{tap}'. " +
                        $"Declared outputs: {string.Join(", ", network.OutputPinNames)}.",
                        nameof(network));
                }
            }
        }

        /// <summary>
        /// How many <see cref="Read"/> calls this memory answered since construction —
        /// the trace seam that pins "one read per executed RAM operand" against the
        /// golden model.
        /// </summary>
        public int ReadCount { get; private set; }

        /// <summary>
        /// How many <see cref="Write"/> calls this memory committed since construction —
        /// the trace seam that pins "one write per executed <c>STORE</c>".
        /// </summary>
        public int WriteCount { get; private set; }

        /// <inheritdoc />
        public int Read(int address)
        {
            var checkedAddress = CheckedAddress(address);
            ReadCount++;
            var bits = AllInputsZero();
            DriveAddress(bits, checkedAddress);
            var outputs = _network.Evaluate(bits);
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
        public void Write(int address, int value)
        {
            var checkedAddress = CheckedAddress(address);
            WriteCount++;
            var bits = AllInputsZero();
            DriveAddress(bits, checkedAddress);
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

        private void DriveAddress(Dictionary<string, bool> bits, int address)
        {
            for (var bit = 0; bit < _signalMap.Address.Count; bit++)
            {
                bits[_signalMap.Address[bit]] = ((address >> bit) & 1) == 1;
            }
        }

        private static int CheckedAddress(int address)
        {
            if (address < 0 || address >= IsaMachine.RamWords)
            {
                throw new InvalidOperationException(
                    $"RAM address {address} is out of range; the RAM has {IsaMachine.RamWords} words.");
            }

            return address;
        }
    }
}
