namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// Names of the signals a photonic ALU drives and reads on a
    /// <see cref="CAP_Core.Analysis.LogicAnalysis.LogicNetworkEvaluator"/>: the
    /// operand bits, the optional second operand, the optional carry-in and the
    /// result taps. Every photonic ALU has a default map matching its shipped
    /// example, so existing chips keep working unchanged; a custom map lets one
    /// chip expose several ISA operations without a tap-name collision (e.g. the
    /// combined logic unit: AND on <c>Y0–Y3</c>, NOT on <c>N0–N3</c>, both on the
    /// shared operands <c>A0–A3</c> — see docs/ISA.md).
    /// </summary>
    public sealed record IsaAluSignalMap
    {
        /// <summary>
        /// Creates a signal map. <paramref name="operandA"/> and
        /// <paramref name="result"/> must name exactly <see cref="IsaMachine.DataBits"/>
        /// signals; <paramref name="operandB"/> is null for unary operations
        /// (NOT) or names the second operand; <paramref name="carryIn"/> names the
        /// carry input of an adder or is null.
        /// </summary>
        /// <exception cref="ArgumentException">A signal list has the wrong length.</exception>
        public IsaAluSignalMap(
            IReadOnlyList<string> operandA,
            IReadOnlyList<string>? operandB,
            string? carryIn,
            IReadOnlyList<string> result)
        {
            ArgumentNullException.ThrowIfNull(operandA);
            ArgumentNullException.ThrowIfNull(result);
            if (operandA.Count != IsaMachine.DataBits)
            {
                throw new ArgumentException(
                    $"The map must name {IsaMachine.DataBits} operand-A signals, got {operandA.Count}.",
                    nameof(operandA));
            }

            if (operandB != null && operandB.Count != IsaMachine.DataBits)
            {
                throw new ArgumentException(
                    $"The map must name {IsaMachine.DataBits} operand-B signals, got {operandB.Count}.",
                    nameof(operandB));
            }

            if (result.Count != IsaMachine.DataBits)
            {
                throw new ArgumentException(
                    $"The map must name {IsaMachine.DataBits} result signals, got {result.Count}.",
                    nameof(result));
            }

            OperandA = operandA;
            OperandB = operandB;
            CarryIn = carryIn;
            Result = result;
        }

        /// <summary>The input signals of the first operand, LSB first.</summary>
        public IReadOnlyList<string> OperandA { get; }

        /// <summary>The input signals of the second operand, LSB first, or null for unary operations.</summary>
        public IReadOnlyList<string>? OperandB { get; }

        /// <summary>The carry-in input of an adder, or null when the operation has none.</summary>
        public string? CarryIn { get; }

        /// <summary>The output taps the ALU reads, LSB first.</summary>
        public IReadOnlyList<string> Result { get; }

        /// <summary>All input signals the map drives (operand A, operand B, carry-in).</summary>
        public IEnumerable<string> Inputs
        {
            get
            {
                foreach (var name in OperandA)
                {
                    yield return name;
                }

                if (OperandB != null)
                {
                    foreach (var name in OperandB)
                    {
                        yield return name;
                    }
                }

                if (CarryIn != null)
                {
                    yield return CarryIn;
                }
            }
        }

        /// <summary>The default NOT map of the shipped NOT 4-bit example: A0–A3 → Y0–Y3.</summary>
        public static IsaAluSignalMap NotDefault { get; } = new(
            Names("A"), operandB: null, carryIn: null, result: Names("Y"));

        /// <summary>The default AND map of the shipped AND 4-bit example: A0–A3 &amp; B0–B3 → Y0–Y3.</summary>
        public static IsaAluSignalMap AndDefault { get; } = new(
            Names("A"), Names("B"), carryIn: null, result: Names("Y"));

        /// <summary>The default adder map of the shipped 4-Bit Adder example: A0–A3 &amp; B0–B3, Cin → S0–S3.</summary>
        public static IsaAluSignalMap AdderDefault { get; } = new(
            Names("A"), Names("B"), carryIn: "Cin", result: Names("S"));

        /// <summary>
        /// The NOT map of the combined logic-unit chip: the NOT shares the operand
        /// inputs A0–A3 with the AND (which keeps <see cref="AndDefault"/>) but taps
        /// its results on N0–N3 so both operations coexist on one network.
        /// </summary>
        public static IsaAluSignalMap CombinedNot { get; } = new(
            Names("A"), operandB: null, carryIn: null, result: Names("N"));

        private static string[] Names(string prefix) =>
            Enumerable.Range(0, IsaMachine.DataBits).Select(bit => $"{prefix}{bit}").ToArray();
    }
}
