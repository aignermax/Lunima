using System.ComponentModel;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Logic.GateView;

/// <summary>
/// The Logic workspace's choice between the abstract gate view (gate symbols joined by
/// signal nets, coloured by live logic levels) and the physical view (MMIs and
/// waveguides). Entering Logic starts in the gate view; other workspaces always show the
/// physical design. The gate view edits only what makes sense at gate level — select and
/// move and connect whole gates — so picking a waveguide tool (Cut, Probe) flips back to the
/// physical view instead of failing silently. Selection is the canvas selection itself,
/// so Properties and every other workspace stay in sync.
/// </summary>
public partial class GateViewViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly WorkspaceViewModel _workspace;

    /// <summary>Retires graph subscriptions and awaits outstanding graph extraction.</summary>
    public Task StopAsync()
    {
        if (!_work.IsStopping)
        {
            _workspace.PropertyChanged -= OnWorkspaceChanged;
            _canvas.Components.CollectionChanged -= DesignCollectionChanged;
            _canvas.Connections.CollectionChanged -= DesignCollectionChanged;
            Levels.Changed -= RaiseChanged;
            Levels.Dispose();
            ShowInLayoutRequested = null;
        }
        return _work.StopAsync();
    }

    /// <summary>True while the user wants gate symbols in the Logic workspace (reset to true on entering Logic).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(IsPhysicalShown), nameof(ToggleTip))]
    private bool _isGateViewPreferred = true;

    /// <summary>Creates the toggle over the shared canvas and workspace switch.</summary>
    /// <param name="canvas">The design canvas whose design is abstracted.</param>
    /// <param name="workspace">The workspace switch; the gate view exists only in Logic.</param>
    /// <param name="wavelengthNm">Wavelength the gates are classified at (the active laser's).</param>
    /// <param name="networkInputBits">The Logic tab's input toggles by network input name (see <see cref="GateNetLevels"/>).</param>
    /// <param name="delay">Debounce delay seam for tests; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public GateViewViewModel(
        DesignCanvasViewModel canvas,
        WorkspaceViewModel workspace,
        Func<int> wavelengthNm,
        Func<IReadOnlyDictionary<string, bool>>? networkInputBits = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        GateFunctionClassifier? classifier = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _wavelengthNm = wavelengthNm ?? throw new ArgumentNullException(nameof(wavelengthNm));
        _delay = delay ?? Task.Delay;
        _classifier = classifier ?? new GateFunctionClassifier();
        Levels = new GateNetLevels(canvas.LogicGateStates, networkInputBits);
        Levels.Changed += RaiseChanged;
        _workspace.PropertyChanged += OnWorkspaceChanged;
        _canvas.Components.CollectionChanged += DesignCollectionChanged;
        _canvas.Connections.CollectionChanged += DesignCollectionChanged;
    }

    /// <summary>Raised whenever the canvas must repaint the gate view (graph, levels, toggle).</summary>
    public event Action? Changed;

    /// <summary>
    /// Asks the shell to show a component in the Layout workspace (select + centre).
    /// Wired by <see cref="MainViewModel"/>.
    /// </summary>
    public Action<ComponentViewModel>? ShowInLayoutRequested { get; set; }

    /// <summary>Live 0/1 levels of the nets, read from the Logic tab's evaluation.</summary>
    public GateNetLevels Levels { get; }

    /// <summary>True in the Logic workspace, where the gate view exists.</summary>
    public bool IsAvailable => _workspace.IsLogic;

    /// <summary>True while the canvas draws gates and nets instead of the physical design.</summary>
    public bool IsShown => IsAvailable && IsGateViewPreferred;

    /// <summary>True while the Logic workspace shows the physical design.</summary>
    public bool IsPhysicalShown => IsAvailable && !IsGateViewPreferred;

    /// <summary>Tooltip of the toggle: what pressing it (or V) switches to.</summary>
    public string ToggleTip => LocalizationService.Instance.Translate(
        IsGateViewPreferred ? "GateView.ShowPhysicalTip" : "GateView.ShowGatesTip");

    /// <summary>Switches between gate and physical view (toolbar toggle, V key); no-op outside Logic.</summary>
    [RelayCommand]
    private void Toggle()
    {
        if (!IsAvailable)
            return;
        IsGateViewPreferred = !IsGateViewPreferred;
    }

    /// <summary>Shows the gate view (toolbar segment).</summary>
    [RelayCommand]
    private void ShowGates() => IsGateViewPreferred = true;

    /// <summary>Shows the physical view (toolbar segment).</summary>
    [RelayCommand]
    private void ShowPhysical() => IsGateViewPreferred = false;

    /// <summary>
    /// Jumps to the Layout workspace focused on the selected gate (double-click on a gate,
    /// "Show in Layout"). Uses <paramref name="target"/> when given, else the selection.
    /// </summary>
    [RelayCommand]
    private void ShowInLayout(ComponentViewModel? target)
    {
        var component = target ?? _canvas.SelectedComponent
                        ?? _canvas.Selection.SelectedComponents.FirstOrDefault();
        if (component != null)
            ShowInLayoutRequested?.Invoke(component);
    }

    /// <summary>
    /// A waveguide-level tool was picked while gates are shown: the gate view cannot edit
    /// waveguides, so the physical view takes over rather than ignoring the tool.
    /// </summary>
    /// <param name="mode">The interaction mode just selected.</param>
    /// <returns>True when the view switched to physical.</returns>
    public bool OnInteractionModeChanged(InteractionMode mode)
    {
        if (!IsShown || mode is not (InteractionMode.Cut
                or InteractionMode.Probe or InteractionMode.PickAnalysisOutput))
            return false;
        IsGateViewPreferred = false;
        return true;
    }

    partial void OnIsGateViewPreferredChanged(bool value)
    {
        if (value)
            RequestRebuild();
        RaiseChanged();
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceViewModel.Current))
            return;
        if (_workspace.IsLogic)
            IsGateViewPreferred = true;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsShown));
        OnPropertyChanged(nameof(IsPhysicalShown));
        if (IsShown)
            RequestRebuild();
        RaiseChanged();
    }

    private void DesignCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) => OnDesignChanged();
    private void RaiseChanged() { if (!_work.IsStopping) Changed?.Invoke(); }
}
