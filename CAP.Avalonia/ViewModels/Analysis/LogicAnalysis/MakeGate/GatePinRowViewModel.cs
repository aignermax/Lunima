using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// One external pin in the Make-gate panel: the role chip (in / out / bias / unused)
/// and the editable short name the pin gets when the gate is saved. The four
/// <c>Is…</c> flags drive radio-style chips; setting one to true picks that role.
/// </summary>
public partial class GatePinRowViewModel : ObservableObject
{
    private readonly GroupPin? _sourcePin;
    private readonly string _originalPinName;

    /// <summary>Creates the row with the suggested role and name preselected.</summary>
    /// <param name="pinName">Current name of the group's external pin.</param>
    /// <param name="role">Initial (suggested or persisted) role.</param>
    /// <param name="name">Initial short name.</param>
    /// <param name="suggestedRole">Role the heuristic suggested, kept for the "suggested" hint.</param>
    /// <param name="sourcePin">Stable physical pin identity; its name follows undo/redo.</param>
    public GatePinRowViewModel(string pinName, GatePinRole role, string name, GatePinRole suggestedRole, GroupPin? sourcePin = null)
    {
        _originalPinName = pinName;
        _sourcePin = sourcePin;
        _role = role;
        _name = name;
        SuggestedRole = suggestedRole;
    }

    /// <summary>Current name of the group's external pin (what the extractor addresses).</summary>
    public string PinName => _sourcePin?.Name ?? _originalPinName;

    internal void RefreshPinName() => OnPropertyChanged(nameof(PinName));

    /// <summary>The role the heuristic suggested.</summary>
    public GatePinRole SuggestedRole { get; }

    /// <summary>The chosen role.</summary>
    [ObservableProperty]
    private GatePinRole _role;

    /// <summary>The short name the pin gets when the gate is saved.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>True when the pin is a logic input.</summary>
    public bool IsInput
    {
        get => Role == GatePinRole.Input;
        set => Pick(GatePinRole.Input, value);
    }

    /// <summary>True when the pin is a logic output.</summary>
    public bool IsOutput
    {
        get => Role == GatePinRole.Output;
        set => Pick(GatePinRole.Output, value);
    }

    /// <summary>True when the pin is a bias input (lit in every row).</summary>
    public bool IsBias
    {
        get => Role == GatePinRole.Bias;
        set => Pick(GatePinRole.Bias, value);
    }

    /// <summary>True when the pin is not part of the gate interface.</summary>
    public bool IsUnused
    {
        get => Role == GatePinRole.Unused;
        set => Pick(GatePinRole.Unused, value);
    }

    /// <summary>True when the chosen role still matches the suggestion.</summary>
    public bool IsSuggestedRole => Role == SuggestedRole;

    private void Pick(GatePinRole role, bool selected)
    {
        if (selected)
            Role = role;
    }

    partial void OnRoleChanged(GatePinRole value)
    {
        OnPropertyChanged(nameof(IsInput));
        OnPropertyChanged(nameof(IsOutput));
        OnPropertyChanged(nameof(IsBias));
        OnPropertyChanged(nameof(IsUnused));
        OnPropertyChanged(nameof(IsSuggestedRole));
    }
}
