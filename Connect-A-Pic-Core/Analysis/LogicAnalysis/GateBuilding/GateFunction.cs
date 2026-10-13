namespace CAP_Core.Analysis.LogicAnalysis.GateBuilding;

/// <summary>
/// The logic function a user intends a gate to compute. Knowing the intent lets the
/// threshold advisor separate the expected-0 rows from the expected-1 rows instead of
/// guessing from the raw powers alone.
/// </summary>
public enum GateFunction
{
    /// <summary>No declared intent: the threshold sits in the largest gap between the measured powers.</summary>
    Custom,

    /// <summary>Inverts the first input.</summary>
    Not,

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
}

/// <summary>The expected output bit of a <see cref="GateFunction"/> for one input pattern.</summary>
public static class GateFunctionTable
{
    /// <summary>
    /// Returns the bit the function produces for <paramref name="pattern"/>, where bit
    /// i of the pattern is input i (the <see cref="TruthTable"/> row order), or null
    /// for <see cref="GateFunction.Custom"/>, which declares no expectation.
    /// </summary>
    /// <param name="function">The intended function.</param>
    /// <param name="pattern">Row index: bit i is the level of input i.</param>
    /// <param name="inputCount">Number of logic inputs (at least 1).</param>
    public static bool? Expected(GateFunction function, int pattern, int inputCount)
    {
        if (inputCount < 1)
            throw new ArgumentOutOfRangeException(nameof(inputCount), inputCount, "A gate needs at least one input.");
        var allOnes = (1 << inputCount) - 1;
        var onesCount = System.Numerics.BitOperations.PopCount((uint)(pattern & allOnes));
        return function switch
        {
            GateFunction.Not => (pattern & 1) == 0,
            GateFunction.And => onesCount == inputCount,
            GateFunction.Nand => onesCount != inputCount,
            GateFunction.Or => onesCount > 0,
            GateFunction.Nor => onesCount == 0,
            GateFunction.Xor => onesCount % 2 == 1,
            _ => null,
        };
    }
}
