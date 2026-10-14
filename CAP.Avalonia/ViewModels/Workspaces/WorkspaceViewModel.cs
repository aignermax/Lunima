using System.ComponentModel;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Component = CAP_Core.Components.Core.Component;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Workspaces;

/// <summary>
/// The toolbar's workspace switch (Layout / Optics / Logic / System): holds the active
/// <see cref="Workspace"/>, restricts the analysis dock to the workspace's tabs and exposes
/// the Run button's label, tooltip and pressed state. The workspace is stored per design in the .lun file
/// (<see cref="CaptureForSave"/> / <see cref="RestoreAfterLoad"/>); switching it is a view
/// change and does not mark the design dirty.
/// </summary>
public partial class WorkspaceViewModel : ObservableObject
{
    private const string ActiveRunLabelKey = "Workspace.RunActive";
    private const string ActiveRunTipKey = "Workspace.RunActiveTip";
    private const string LogicStepLabelKey = "Workspace.RunLogicStep";
    private const string LogicStepTipKey = "Workspace.RunLogicStepTip";

    private readonly AnalysisDockViewModel _dock;

    /// <summary>The active workspace.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLayout), nameof(IsOptics), nameof(IsLogic), nameof(IsSystem),
        nameof(Profile), nameof(RunLabel), nameof(RunTip), nameof(IsOpticalWorkspace),
        nameof(ShowsProbeTool), nameof(ShowsGateTools), nameof(DisplayName), nameof(IsRunActive))]
    private Workspace _current;

    /// <summary>
    /// Whether the CW simulation and its power overlay are on. Mirrors the canvas overlay
    /// state; in Optics and System it turns Run into an on/off toggle (<see cref="IsRunActive"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunActive), nameof(RunLabel), nameof(RunTip))]
    private bool _isSimulationOn;

    /// <summary>
    /// Whether the Logic tab holds a built network with registers to clock. In Logic it
    /// decides what Run does next — build the network, or step its clock — and the Run
    /// label says which.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunLabel), nameof(RunTip))]
    private bool _canStepLogicClock;

    /// <summary>Creates the switch over the analysis dock it filters; starts in <see cref="Workspace.Layout"/>.</summary>
    public WorkspaceViewModel(AnalysisDockViewModel dock)
    {
        _dock = dock ?? throw new ArgumentNullException(nameof(dock));
        _dock.ApplyWorkspaceTabs(Profile.Tabs);
        LocalizationService.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>What the active workspace means (tabs, Run label).</summary>
    public WorkspaceProfile Profile => WorkspaceProfile.For(Current);

    /// <summary>True in the Layout workspace.</summary>
    public bool IsLayout => Current == Workspace.Layout;

    /// <summary>True in the Optics workspace (shows the CW/Transient sub-choice).</summary>
    public bool IsOptics => Current == Workspace.Optics;

    /// <summary>True in the Logic workspace.</summary>
    public bool IsLogic => Current == Workspace.Logic;

    /// <summary>True in the System workspace.</summary>
    public bool IsSystem => Current == Workspace.System;

    /// <summary>True where Run simulates light (Optics, System): Run toggles the simulation, and the analysis-output picker shows.</summary>
    public bool IsOpticalWorkspace => Current is Workspace.Optics or Workspace.System;

    /// <summary>
    /// True where the Probe tool (cross-section mode slice) is offered: Layout and Optics,
    /// where waveguide geometry and modes matter. Select / Connect / Delete / Cut stay in
    /// every workspace — manual edits are never locked away.
    /// </summary>
    public bool ShowsProbeTool => Current is Workspace.Layout or Workspace.Optics;

    /// <summary>True where the Truth Table / Make gate section belongs: Logic and System.</summary>
    public bool ShowsGateTools => Current is Workspace.Logic or Workspace.System;

    /// <summary>Localized name of the active workspace (status-bar chip).</summary>
    public string DisplayName => LocalizationService.Instance.Translate($"Workspace.{Current}");

    /// <summary>
    /// True while Run shows its pressed state: the simulation is on in a light-simulating
    /// workspace, so the next Run (or L / P) turns it off.
    /// </summary>
    public bool IsRunActive => IsOpticalWorkspace && IsSimulationOn;

    /// <summary>Run button label naming the action, e.g. "Run checks", or "Simulation on" while active.</summary>
    public string RunLabel => LocalizationService.Instance.Translate(
        IsRunActive ? ActiveRunLabelKey : IsLogicStep ? LogicStepLabelKey : Profile.RunLabelKey);

    /// <summary>Run button tooltip: what Run does in this workspace (or how to turn it off), with its key hint.</summary>
    public string RunTip => LocalizationService.Instance.Translate(
        IsRunActive ? ActiveRunTipKey : IsLogicStep ? LogicStepTipKey : Profile.RunTipKey);

    /// <summary>True when Run steps the clock of an already built logic network.</summary>
    private bool IsLogicStep => IsLogic && CanStepLogicClock;

    /// <summary>Switches to <paramref name="workspace"/> (toolbar segments, Ctrl+1..4).</summary>
    [RelayCommand]
    private void Select(Workspace workspace) => Current = workspace;

    partial void OnCurrentChanged(Workspace value) => _dock.ApplyWorkspaceTabs(Profile.Tabs);

    /// <summary>The value written to the .lun file.</summary>
    public string CaptureForSave() => Current.ToString();

    /// <summary>
    /// Restores the workspace after a design load: the stored value when it names a
    /// workspace, otherwise the content-based default of <see cref="WorkspaceDefaults"/>.
    /// </summary>
    /// <param name="stored">The .lun file's workspace field; null for files without one.</param>
    /// <param name="topLevelComponents">The loaded design's top-level components.</param>
    /// <param name="connections">The loaded design's connections.</param>
    public void RestoreAfterLoad(
        string? stored,
        IReadOnlyCollection<Component> topLevelComponents,
        IReadOnlyCollection<WaveguideConnection> connections)
    {
        Current = Enum.TryParse<Workspace>(stored, ignoreCase: true, out var parsed)
                  && Enum.IsDefined(parsed)
            ? parsed
            : WorkspaceDefaults.ChooseFor(topLevelComponents, connections);
    }

    /// <summary>A new, empty design starts in <see cref="Workspace.Layout"/>.</summary>
    public void ResetForNewDesign() => Current = Workspace.Layout;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RunLabel));
        OnPropertyChanged(nameof(RunTip));
        OnPropertyChanged(nameof(DisplayName));
    }
}
