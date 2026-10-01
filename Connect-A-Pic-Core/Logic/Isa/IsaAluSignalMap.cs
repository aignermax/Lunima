namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// The signal names a photonic ALU drives (operand bits, carry-in) and reads
    /// (result taps) on its <c>LogicNetworkEvaluator</c>. The per-ALU defaults
    /// (<see cref="Adder"/>, <see cref="Not"/>, <see cref="And"/>) are exactly the
    /// names the shipped single-operation examples expose, so existing callers and
    /// networks stay unchanged; a custom map lets one chip expose several operations
    /// without a name collision — e.g. the combined logic-unit chip
    /// (<see cref="CombinedLogicUnitNot"/>, docs/ISA.md) keeps AND on Y0–Y3 and moves
    /// NOT to N0–N3.
    /// </summary>
    public sealed record IsaAluSignalMap
    {
        /// <summary>
        /// Creates a map. <paramref name="operandA"/> and <paramref name="result"/>
        /// must name exactly <see cref="IsaMachine.DataBits"/> signals, LSB first;
        /// <paramref name="operandB"/> is null for unary operations (NOT) and
        /// <paramref name="carryIn"/> names the carry-in input when the network has
        /// one (only the adder uses it; it is driven 0 on every ADD).
        /// </summary>
        /// <exception cref="ArgumentException">A signal list has the wrong length.</exception>
        public IsaAluSignalMap(
            IEnumerable<string> operandA,
            IEnumerable<string> result,
            IEnumerable<string>? operandB = null,
            string? carryIn = null)
        {
            OperandA = ToBitList(operandA, nameof(operandA));
            Result = ToBitList(result, nameof(result));
            OperandB = operandB == null
                ? Array.Empty<string>()
                : ToBitList(operandB, nameof(operandB));
            CarryIn = carryIn;
        }

        /// <summary>Operand-A bit names, LSB first.</summary>
        public IReadOnlyList<string> OperandA { get; }

        /// <summary>Operand-B bit names, LSB first; empty for unary operations.</summary>
        public IReadOnlyList<string> OperandB { get; }

        /// <summary>Result bit names, LSB first.</summary>
        public IReadOnlyList<string> Result { get; }

        /// <summary>Carry-in input name, or null when the network has none.</summary>
        public string? CarryIn { get; }

        /// <summary>Every input signal a binary ALU drives: both operand words.</summary>
        public IEnumerable<string> AllOperands =>
            OperandA.Concat(OperandB).Concat(CarryIn == null ? Array.Empty<string>() : new[] { CarryIn });

        /// <summary>The shipped 4-bit adder names: A0–A3, B0–B3, Cin → S0–S3.</summary>
        public static IsaAluSignalMap Adder { get; } = new(
            Bits("A"), Bits("S"), Bits("B"), carryIn: "Cin");

        /// <summary>The shipped 4-bit NOT names: A0–A3 → Y0–Y3.</summary>
        public static IsaAluSignalMap Not { get; } = new(Bits("A"), Bits("Y"));

        /// <summary>The shipped 4-bit AND names: A0–A3, B0–B3 → Y0–Y3.</summary>
        public static IsaAluSignalMap And { get; } = new(Bits("A"), Bits("Y"), Bits("B"));

        /// <summary>
        /// The NOT slice of the combined logic-unit chip: NOT shares the operand
        /// bits A0–A3 with AND but reads its own result taps N0–N3, so one network
        /// can expose AND (default map, Y0–Y3) and NOT (this map) side by side.
        /// </summary>
        public static IsaAluSignalMap CombinedLogicUnitNot { get; } = new(Bits("A"), Bits("N"));

        private static string[] Bits(string prefix)
        {
            var bits = new string[IsaMachine.DataBits];
            for (var bit = 0; bit < IsaMachine.DataBits; bit++)
            {
                bits[bit] = $"{prefix}{bit}";
            }

            return bits;
        }

        private static string[] ToBitList(IEnumerable<string> signals, string parameterName)
        {
            var list = (signals ?? throw new ArgumentNullException(parameterName)).ToArray();
            if (list.Length != IsaMachine.DataBits)
            {
                throw new ArgumentException(
                    $"A signal map needs exactly {IsaMachine.DataBits} '{parameterName}' names " +
                    $"(LSB first), got {list.Length}.",
                    parameterName);
            }

            return list;
        }
    }
}
