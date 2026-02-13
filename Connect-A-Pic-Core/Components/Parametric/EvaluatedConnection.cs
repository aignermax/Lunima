using System.Numerics;

namespace CAP_Core.Components.Parametric
{
    /// <summary>
    /// Result of evaluating a parametric S-Matrix connection.
    /// Contains the pin names and the computed complex transfer value.
    /// </summary>
    public readonly record struct EvaluatedConnection(
        string FromPin,
        string ToPin,
        Complex Value);
}
