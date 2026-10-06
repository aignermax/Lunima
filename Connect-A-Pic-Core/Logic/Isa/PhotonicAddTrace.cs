namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// What one photonic ADD did (issue #1227): the two operands and the sum the
    /// network settled on, plus the light-travel time of that addition — the
    /// arrival time of the latest switching network output for exactly these input
    /// values, walked by <see cref="CAP_Core.Analysis.LogicAnalysis.LogicEventTimeline"/>
    /// from the previously driven operand bits (all-zero at power-on). This is the
    /// real per-input ripple, not the worst-case critical path.
    /// </summary>
    /// <param name="A">The accumulator operand (0–15).</param>
    /// <param name="B">The RAM operand (0–15).</param>
    /// <param name="Sum">The settled sum, wrapped modulo 16.</param>
    /// <param name="LightTravelPicoseconds">
    /// Arrival time of the latest switching output in picoseconds; 0 when no output
    /// switched (the inputs did not change the settled state).
    /// </param>
    public sealed record PhotonicAddTrace(int A, int B, int Sum, double LightTravelPicoseconds);
}
