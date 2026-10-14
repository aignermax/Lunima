using System.Numerics;

namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>
/// Names the schematic symbol of a gate. The exact answer comes from its extracted
/// truth table (<see cref="FromTruthTable"/>); before that is known, the pin structure
/// alone already identifies registers and one-to-many copies (<see cref="FromStructure"/>).
/// </summary>
public static class GateSymbolClassifier
{
    /// <summary>
    /// The symbol implied by the pin structure alone: a register flag wins, one input
    /// feeding several outputs is a fan-out copy, anything else stays unknown until
    /// its truth table is classified.
    /// </summary>
    /// <param name="inputCount">Number of logic input pins.</param>
    /// <param name="outputCount">Number of logic output pins.</param>
    /// <param name="isRegister">True for a register-designated gate.</param>
    public static GateSymbolKind FromStructure(int inputCount, int outputCount, bool isRegister)
    {
        if (isRegister)
            return GateSymbolKind.Register;
        if (inputCount == 1 && outputCount > 1)
            return GateSymbolKind.FanOut;
        return GateSymbolKind.Unknown;
    }

    /// <summary>
    /// Classifies an extracted truth table: every output must compute the same standard
    /// function of the inputs (several equal outputs of a one-input buffer make a
    /// fan-out). Registers keep the flip-flop symbol whatever their table says.
    /// </summary>
    /// <param name="table">The gate's truth table in binary-counting row order.</param>
    /// <param name="isRegister">True for a register-designated gate.</param>
    public static GateSymbolKind FromTruthTable(TruthTable table, bool isRegister)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (isRegister)
            return GateSymbolKind.Register;
        var inputCount = table.InputPinNames.Count;
        if (inputCount == 0 || table.OutputPinNames.Count == 0 || table.Rows.Count != 1 << inputCount)
            return GateSymbolKind.Unknown;

        var first = OutputColumn(table, table.OutputPinNames[0]);
        if (table.OutputPinNames.Skip(1).Any(pin => OutputColumn(table, pin) != first))
            return GateSymbolKind.Unknown;

        var kind = Match(first, inputCount);
        if (kind == GateSymbolKind.Buffer && table.OutputPinNames.Count > 1)
            return GateSymbolKind.FanOut;
        return kind;
    }

    /// <summary>The output bits of one pin packed as a bit mask: bit r is row r's output.</summary>
    private static int OutputColumn(TruthTable table, string pin)
    {
        var mask = 0;
        for (var row = 0; row < table.Rows.Count; row++)
        {
            if (table.Rows[row].Outputs.TryGetValue(pin, out var value) && value.IsOne)
                mask |= 1 << row;
        }
        return mask;
    }

    /// <summary>Matches one output column against the standard functions of the inputs.</summary>
    private static GateSymbolKind Match(int column, int inputCount)
    {
        var rows = 1 << inputCount;
        if (inputCount == 1)
        {
            return column switch
            {
                0b10 => GateSymbolKind.Buffer,
                0b01 => GateSymbolKind.Not,
                _ => GateSymbolKind.Unknown,
            };
        }

        foreach (var candidate in MultiInputKinds)
        {
            if (column == ColumnOf(candidate, rows, inputCount))
                return candidate;
        }
        return GateSymbolKind.Unknown;
    }

    private static readonly GateSymbolKind[] MultiInputKinds =
    [
        GateSymbolKind.And, GateSymbolKind.Nand, GateSymbolKind.Or,
        GateSymbolKind.Nor, GateSymbolKind.Xor, GateSymbolKind.Xnor,
    ];

    /// <summary>The output column a standard function produces over all input patterns.</summary>
    private static int ColumnOf(GateSymbolKind kind, int rows, int inputCount)
    {
        var mask = 0;
        for (var pattern = 0; pattern < rows; pattern++)
        {
            var ones = BitOperations.PopCount((uint)pattern);
            var bit = kind switch
            {
                GateSymbolKind.And => ones == inputCount,
                GateSymbolKind.Nand => ones != inputCount,
                GateSymbolKind.Or => ones > 0,
                GateSymbolKind.Nor => ones == 0,
                GateSymbolKind.Xor => ones % 2 == 1,
                GateSymbolKind.Xnor => ones % 2 == 0,
                _ => false,
            };
            if (bit)
                mask |= 1 << pattern;
        }
        return mask;
    }
}
