using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;

/// <summary>
/// Gate-candidate half of <see cref="LogicPanelViewModel"/>: while no network is shown,
/// the empty state lists every top-level group that is not a gate yet (no persisted
/// pin assignment), each with a "Make gate…" button — the link from the Logic tab back
/// to the step that turns a grouped circuit into a gate.
/// </summary>
public partial class LogicPanelViewModel
{
    /// <summary>Top-level groups that are not gates yet.</summary>
    public ObservableCollection<LogicGateCandidateViewModel> GateCandidates { get; } = new();

    /// <summary>True when at least one group could still become a gate.</summary>
    [ObservableProperty]
    private bool _hasGateCandidates;

    /// <summary>Selects a group and opens its Make-gate steps (wired by the main window).</summary>
    public Action<ComponentGroup>? MakeGateRequested { get; set; }

    /// <summary>Rebuilds the candidate list from the canvas; leaves it untouched when nothing changed.</summary>
    public void RefreshGateCandidates()
    {
        if (_work.IsStopping) return;
        var groups = _canvas?.Components
            .Select(c => c.Component)
            .OfType<ComponentGroup>()
            .Where(group => group.TruthTablePinAssignment == null)
            .ToList() ?? new List<ComponentGroup>();
        if (!groups.SequenceEqual(GateCandidates.Select(c => c.Group)))
        {
            GateCandidates.Clear();
            foreach (var group in groups)
                GateCandidates.Add(new LogicGateCandidateViewModel(group, RequestMakeGate));
        }
        HasGateCandidates = GateCandidates.Count > 0;
    }

    private void RequestMakeGate(ComponentGroup group)
    {
        if (!_work.IsStopping) MakeGateRequested?.Invoke(group);
    }
}
