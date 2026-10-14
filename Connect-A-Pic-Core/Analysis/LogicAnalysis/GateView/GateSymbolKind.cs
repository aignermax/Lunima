namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>
/// The schematic symbol a gate is drawn with in the Logic workspace's gate view.
/// Derived from the gate's truth table (see <see cref="GateSymbolClassifier"/>), so
/// it names what the interference structure computes, not how it is built.
/// </summary>
public enum GateSymbolKind
{
    /// <summary>No standard function recognised yet (or a multi-output block): drawn as a labelled box.</summary>
    Unknown,

    /// <summary>Output follows the single input.</summary>
    Buffer,

    /// <summary>Output inverts the single input.</summary>
    Not,

    /// <summary>One input copied onto several outputs (an optical splitter used as signal copy).</summary>
    FanOut,

    /// <summary>1 only when every input is 1.</summary>
    And,

    /// <summary>0 only when every input is 1.</summary>
    Nand,

    /// <summary>1 when any input is 1.</summary>
    Or,

    /// <summary>1 only when every input is 0.</summary>
    Nor,

    /// <summary>1 when an odd number of inputs is 1.</summary>
    Xor,

    /// <summary>1 when an even number of inputs is 1.</summary>
    Xnor,

    /// <summary>A register-designated gate: drawn as a clocked flip-flop.</summary>
    Register,
}
