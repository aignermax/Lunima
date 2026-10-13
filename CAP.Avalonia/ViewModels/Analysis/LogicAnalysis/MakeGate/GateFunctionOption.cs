using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>One entry of the Make-gate panel's "intended function" picker.</summary>
/// <param name="Function">The logic function.</param>
public sealed record GateFunctionOption(GateFunction Function)
{
    /// <summary>All picker entries: custom first, then the classic gates.</summary>
    public static IReadOnlyList<GateFunctionOption> All { get; } = new[]
    {
        GateFunction.Custom, GateFunction.Nand, GateFunction.And, GateFunction.Or,
        GateFunction.Nor, GateFunction.Xor, GateFunction.Not,
    }.Select(f => new GateFunctionOption(f)).ToList();

    /// <summary>
    /// Display name: the gate's standard upper-case name (language-neutral, like the
    /// pin names), or the localized word for a custom function.
    /// </summary>
    public string DisplayName => Function == GateFunction.Custom
        ? LocalizationService.Instance.Translate("MakeGate.FunctionCustom")
        : Function.ToString().ToUpperInvariant();

    /// <summary>The default gate name for this function, or null for custom.</summary>
    public string? GateNameStem => Function == GateFunction.Custom ? null : Function.ToString().ToUpperInvariant();

    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}
