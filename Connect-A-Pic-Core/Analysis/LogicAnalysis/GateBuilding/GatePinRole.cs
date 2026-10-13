namespace CAP_Core.Analysis.LogicAnalysis.GateBuilding;

/// <summary>The role one external pin of a group plays once the group is a logic gate.</summary>
public enum GatePinRole
{
    /// <summary>A logic input: lit (1) or dark (0) per truth-table row.</summary>
    Input,

    /// <summary>A logic output: its light power is classified against the threshold.</summary>
    Output,

    /// <summary>A reference input that is lit in every row (what inverting gates need).</summary>
    Bias,

    /// <summary>Not part of the gate interface (dangling coupler ports, electrical pins).</summary>
    Unused,
}

/// <summary>
/// The suggested role and short name for one external pin of a group.
/// </summary>
/// <param name="PinName">Current name of the external pin.</param>
/// <param name="Role">Suggested role.</param>
/// <param name="SuggestedName">Suggested short name (A, B, BIAS, Y…); the current name for unused pins.</param>
public sealed record GatePinSuggestion(string PinName, GatePinRole Role, string SuggestedName);
