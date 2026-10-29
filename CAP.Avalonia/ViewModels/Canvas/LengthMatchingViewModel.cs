using System.ComponentModel;
using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Routing.MeanderGeneration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// ViewModel for the "Length matching" section of the Properties panel: when exactly one
/// waveguide connection is selected, the user can stretch its route to an exact target
/// length (± tolerance) via a meander (<see cref="ConnectionLengthMatcher"/>) with a chosen bend
/// radius, or clear the target again. The meander keeps clear of every other route and component.
/// A matched route stays frozen so later routing passes keep its length; clearing unfreezes it
/// and hands it back to the normal router. Apply and Clear are undoable. Typed matcher failures
/// are mapped to readable localized messages — the raw enum names never reach the UI.
/// </summary>
public partial class LengthMatchingViewModel : ObservableObject
{
    private const double DefaultToleranceMicrometers = 0.1;
    private const double RadiusComparisonSlackMicrometers = 1e-9;

    private readonly DesignCanvasViewModel _canvas;
    private readonly ConnectionLengthMatcher _matcher;

    private WaveguideConnectionViewModel? _selectedConnection;
    private WaveguideConnectionViewModel? _observedConnection;
    private bool _reroutingItself;

    /// <summary>The currently selected connection (single-click selection), fed by MainViewModel.</summary>
    public WaveguideConnectionViewModel? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (SetProperty(ref _selectedConnection, value))
                UpdateFromSelection();
        }
    }

    /// <summary>Actual geometric length (µm) of the target connection's current route.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentLengthText))]
    private double _currentLengthMicrometers;

    /// <summary>Target length input (µm) as typed; defaults to the current route length.</summary>
    [ObservableProperty]
    private string _targetLengthText = "";

    /// <summary>Tolerance input (µm) as typed; defaults to 0.1 µm.</summary>
    [ObservableProperty]
    private string _toleranceText = "0.1";

    /// <summary>Meander bend radius input (µm); defaults to the connection's effective bend radius.</summary>
    [ObservableProperty]
    private string _bendRadiusText = "";

    /// <summary>Localized status line shown after Apply/Clear, empty while nothing ran.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(HasSuccessStatus))]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus))]
    private string _statusMessage = "";

    /// <summary>True when <see cref="StatusMessage"/> reports a failure (shown red), false for success (green).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuccessStatus))]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus))]
    private bool _isStatusError;

    /// <summary>True when a status message is present.</summary>
    public bool HasStatus => StatusMessage.Length > 0;

    /// <summary>True when the status reports success.</summary>
    public bool HasSuccessStatus => HasStatus && !IsStatusError;

    /// <summary>True when the status reports an error.</summary>
    public bool HasErrorStatus => HasStatus && IsStatusError;

    /// <summary>
    /// Panel visibility: exactly one connection is targeted — either the single clicked
    /// connection (batch selection empty) or a batch selection holding exactly one.
    /// </summary>
    public bool HasExactlyOneConnection => TargetConnection() != null;

    /// <summary>Formatted current length for the read-only row.</summary>
    public string CurrentLengthText => string.Format(CultureInfo.CurrentCulture,
        LocalizationService.Instance.Translate("Routing.LengthMatch.CurrentLengthFormat"),
        CurrentLengthMicrometers);

    /// <summary>Initializes a new instance bound to the design canvas.</summary>
    /// <param name="canvas">The design canvas providing the selection, obstacles and router.</param>
    public LengthMatchingViewModel(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
        _matcher = new ConnectionLengthMatcher(canvas.Router);
        // Never unsubscribed: although registered transient, this VM is only ever resolved into
        // the singleton BottomPanelViewModel, so it lives as long as the canvas it observes.
        // If it ever becomes truly transient, add IDisposable and detach this handler.
        _canvas.Selection.SelectedConnections.CollectionChanged += (_, _) => UpdateFromSelection();
    }

    /// <summary>
    /// The connection length matching applies to: the batch selection when it holds exactly
    /// one connection, otherwise the single clicked connection. A multi-connection batch is
    /// deliberately not supported — a meander is a per-route geometry, and stamping one
    /// target onto several different routes would silently mean different lengths.
    /// </summary>
    private WaveguideConnectionViewModel? TargetConnection()
    {
        var batch = _canvas.Selection.SelectedConnections;
        if (batch.Count == 1)
            return batch[0];
        if (batch.Count > 1)
            return null;
        return SelectedConnection;
    }

    private void UpdateFromSelection()
    {
        OnPropertyChanged(nameof(HasExactlyOneConnection));
        StatusMessage = "";
        var target = TargetConnection();
        ObserveLength(target);
        if (target == null)
            return;

        CurrentLengthMicrometers = target.Connection.PathLengthMicrometers;
        BendRadiusText = Math.Max(target.Connection.BendRadiusMicrometers, _matcher.ResolveProcessFloor(target.Connection))
            .ToString("0.###", CultureInfo.InvariantCulture);
        // Pre-fill with the recorded intent when the route was matched before, so the
        // inputs round-trip a saved design; otherwise mirror the current geometry.
        TargetLengthText = (target.Connection.TargetLengthMicrometers ?? CurrentLengthMicrometers)
            .ToString("0.###", CultureInfo.InvariantCulture);
        ToleranceText = (target.Connection.LengthToleranceMicrometers ?? DefaultToleranceMicrometers)
            .ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Keeps <see cref="CurrentLengthMicrometers"/> live: any later path change (bend handle,
    /// routing pass, undo) updates the read-only length row immediately.
    /// </summary>
    private void ObserveLength(WaveguideConnectionViewModel? target)
    {
        if (ReferenceEquals(_observedConnection, target))
            return;
        if (_observedConnection != null)
            _observedConnection.PropertyChanged -= OnObservedConnectionChanged;
        _observedConnection = target;
        if (target != null)
            target.PropertyChanged += OnObservedConnectionChanged;
    }

    private void OnObservedConnectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WaveguideConnectionViewModel.PathLength) && _observedConnection != null)
            CurrentLengthMicrometers = _observedConnection.Connection.PathLengthMicrometers;
    }

    /// <summary>Stretches the target connection's route to the entered target length.</summary>
    [RelayCommand]
    private void Apply()
    {
        if (TargetConnection() is not { } target)
            return;

        if (!LengthInput.TryParse(TargetLengthText, out double targetLength) || targetLength <= 0
            || !LengthInput.TryParse(ToleranceText, out double tolerance) || tolerance < 0
            || !LengthInput.TryParse(BendRadiusText, out double radius) || radius <= 0)
        {
            SetError("Routing.LengthMatch.Error.InvalidInput");
            return;
        }

        double floor = _matcher.ResolveProcessFloor(target.Connection);
        if (radius < floor - RadiusComparisonSlackMicrometers)
        {
            IsStatusError = true;
            StatusMessage = string.Format(CultureInfo.CurrentCulture,
                LocalizationService.Instance.Translate("Routing.LengthMatch.Error.RadiusBelowProcessMinimum"), floor);
            return;
        }

        var before = LengthMatchState.Capture(target.Connection);
        var components = _canvas.Components.Select(vm => vm.Component).ToList();
        var result = _matcher.ApplyTargetLength(target.Connection, components, targetLength, tolerance,
            _canvas.ConnectionManager.Connections, radius);
        if (!result.IsSuccess)
        {
            SetError(MapFailureKey(result.FailureReason));
            return;
        }

        Record(new LengthMatchCommand(target, before, LengthMatchState.Capture(target.Connection),
            "Match route length", AfterApply));
        CurrentLengthMicrometers = target.Connection.PathLengthMicrometers;
        IsStatusError = false;
        StatusMessage = string.Format(CultureInfo.CurrentCulture,
            LocalizationService.Instance.Translate("Routing.LengthMatch.Success"),
            CurrentLengthMicrometers);
    }

    /// <summary>
    /// Removes the length intent: drops target/tolerance, unfreezes the route and lets the
    /// normal router rebuild it (the stale meander geometry must be discarded first, or the
    /// incremental router would keep it). A connection without a length target is left
    /// untouched: routes frozen for other reasons (e.g. GDS-imported geometry) must not be
    /// unfrozen and re-routed — that would silently discard their path. The button stays usable
    /// while that reroute is still running, so the next route can be cleared right away.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Clear()
    {
        if (TargetConnection() is not { } target)
            return;

        var connection = target.Connection;
        if (connection.TargetLengthMicrometers is null)
            return;

        var before = LengthMatchState.Capture(connection);
        _reroutingItself = true;
        try { Record(new LengthMatchCommand(target, before, before.Cleared(), "Clear route length target", AfterApply)); }
        finally { _reroutingItself = false; }
        await _canvas.RecalculateRoutesAsync();
        target.NotifyPathChanged();

        CurrentLengthMicrometers = connection.PathLengthMicrometers;
        IsStatusError = false;
        StatusMessage = LocalizationService.Instance.Translate("Routing.LengthMatch.Cleared");
    }

    /// <summary>Executes through the undo history when one is wired, otherwise directly.</summary>
    private void Record(LengthMatchCommand command)
    {
        if (_canvas.CommandManager is { } commands)
            commands.ExecuteCommand(command);
        else
            command.Execute();
    }

    /// <summary>Undo/redo of a Clear (or of an Apply over a cleared route) needs a fresh route.</summary>
    private void AfterApply(bool routeCleared)
    {
        if (routeCleared && !_reroutingItself)
            _ = _canvas.RecalculateRoutesAsync();
        if (TargetConnection() is { } target)
            CurrentLengthMicrometers = target.Connection.PathLengthMicrometers;
    }

    private void SetError(string localizationKey)
    {
        IsStatusError = true;
        StatusMessage = LocalizationService.Instance.Translate(localizationKey);
    }

    /// <summary>
    /// Maps the typed matcher failure to its localization key. Unknown reasons fall back to
    /// a generic message so a new enum value never surfaces as a raw identifier in the UI.
    /// </summary>
    private static string MapFailureKey(MeanderFailureReason? reason) => reason switch
    {
        MeanderFailureReason.TargetShorterThanDirectPath => "Routing.LengthMatch.Error.TargetShorterThanDirectPath",
        MeanderFailureReason.BoundsTooSmallForMeander => "Routing.LengthMatch.Error.BoundsTooSmallForMeander",
        MeanderFailureReason.EndpointsNotRoutableAtMinRadius => "Routing.LengthMatch.Error.EndpointsNotRoutableAtMinRadius",
        MeanderFailureReason.CollidesWithRoutes => "Routing.LengthMatch.Error.CollidesWithRoutes",
        _ => "Routing.LengthMatch.Error.Unknown",
    };
}
