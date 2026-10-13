using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// A top-level group that is not a gate yet, listed in the Logic panel's empty state
/// with a "Make gate…" button that selects it and opens the Make-gate steps.
/// </summary>
public partial class LogicGateCandidateViewModel
{
    private readonly Action<ComponentGroup> _makeGate;

    /// <summary>Creates the entry.</summary>
    /// <param name="group">The group that could become a gate.</param>
    /// <param name="makeGate">Selects the group and opens the Make-gate steps.</param>
    public LogicGateCandidateViewModel(ComponentGroup group, Action<ComponentGroup> makeGate)
    {
        Group = group;
        _makeGate = makeGate;
    }

    /// <summary>The group that could become a gate.</summary>
    public ComponentGroup Group { get; }

    /// <summary>The group's display name.</summary>
    public string Name => Group.GroupName;

    /// <summary>Selects the group and opens the Make-gate steps.</summary>
    [RelayCommand]
    private void MakeGate() => _makeGate(Group);
}
