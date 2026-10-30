using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.LengthEqualization;
using CAP_Core.Routing.MeanderGeneration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// "Equalize lengths" for a multi-selection of connections: one click gives every selected route
/// the same length — the longest one's unless a length is typed — by folding a meander into each
/// shorter route wherever it has room, with the largest radius that fits. The search runs off the
/// UI thread; the result lands as ONE undoable step, and every matched route keeps its target
/// through later routing passes like a single length match does.
/// </summary>
public partial class LengthEqualizationViewModel : ObservableObject
{
    private const double DefaultToleranceMicrometers = 0.1;
    private const int MinimumConnections = 2;
    /// <summary>Width differences below this (µm) are not reported as a change.</summary>
    private const double WidthChangeMicrometers = 1e-3;
    /// <summary>Suggested largest radius (µm): gentle loops by default; the search steps down where they do not fit.</summary>
    private const double SuggestedMaxRadiusMicrometers = 250.0;
    /// <summary>Suggested smallest radius (µm): loops tighter than this lose noticeable light.</summary>
    private const double SuggestedMinRadiusMicrometers = 25.0;

    private readonly DesignCanvasViewModel _canvas;
    private readonly LengthEqualizer _equalizer;
    private readonly CAP_Core.Routing.MeanderGeneration.ConnectionLengthMatcher _matcher;

    /// <summary>Common length input (µm); empty means "the longest selected route".</summary>
    [ObservableProperty]
    private string _targetLengthText = "";

    /// <summary>Largest meander radius to try (µm); smaller ones are used only where it does not fit.</summary>
    [ObservableProperty]
    private string _maxRadiusText = "";

    /// <summary>Re-lay a bundle of facing pins from scratch instead of stretching its existing routes.</summary>
    [ObservableProperty]
    private bool _rerouteAsBundle = true;

    /// <summary>Smallest radius any new loop may use (µm): below it a route is reported rather than coiled up.</summary>
    [ObservableProperty]
    private string _minRadiusText = "";

    /// <summary>True while the search runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EqualizeCommand))]
    private bool _isBusy;

    /// <summary>Localized outcome of the last run, empty while nothing ran.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuccessStatus))]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus))]
    private string _statusMessage = "";

    /// <summary>True when <see cref="StatusMessage"/> reports a problem.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuccessStatus))]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus))]
    private bool _isStatusError;

    /// <summary>True when the status reports success.</summary>
    public bool HasSuccessStatus => StatusMessage.Length > 0 && !IsStatusError;

    /// <summary>True when the status reports a problem.</summary>
    public bool HasErrorStatus => StatusMessage.Length > 0 && IsStatusError;

    /// <summary>Length-lock actions (groups, re-equalize, release) for the canvas padlocks.</summary>
    public LengthLockActions Locks { get; }

    /// <summary>Section visibility: at least two connections are selected.</summary>
    public bool HasSeveralConnections => Targets().Count >= MinimumConnections;

    /// <summary>"Release length lock" visibility: a selected route still carries a length lock.</summary>
    public bool HasLockedSelection => Targets().Any(t => t.Connection.TargetLengthMicrometers != null);

    /// <summary>"N connections · shortest … · longest …" for the selection.</summary>
    public string SummaryText
    {
        get
        {
            var targets = Targets();
            if (targets.Count == 0)
                return "";
            return string.Format(CultureInfo.CurrentCulture,
                LocalizationService.Instance.Translate("Routing.LengthEqualize.Summary"), targets.Count,
                targets.Min(t => t.Connection.PathLengthMicrometers), targets.Max(t => t.Connection.PathLengthMicrometers));
        }
    }

    /// <summary>Initializes a new instance bound to the design canvas.</summary>
    /// <param name="canvas">The design canvas providing the selection, the design and the router.</param>
    public LengthEqualizationViewModel(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
        _equalizer = new LengthEqualizer(canvas.Router);
        _matcher = new CAP_Core.Routing.MeanderGeneration.ConnectionLengthMatcher(canvas.Router);
        Locks = new LengthLockActions(canvas, this);
        // Never unsubscribed: this VM lives in the singleton BottomPanelViewModel as long as the canvas.
        _canvas.Selection.SelectedConnections.CollectionChanged += (_, _) => Refresh();
    }

    private List<WaveguideConnectionViewModel> Targets() =>
        _canvas.Selection.SelectedConnections.Where(c => c.Connection.RoutedPath != null).Distinct().ToList();

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasSeveralConnections));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasLockedSelection));
        if (IsBusy)
            return;
        StatusMessage = "";
        var targets = Targets();
        if (targets.Count < MinimumConnections)
            return;
        double radius = Math.Max(SuggestedMaxRadiusMicrometers,
            targets.Max(t => Math.Max(t.Connection.BendRadiusMicrometers, _matcher.ResolveProcessFloor(t.Connection))));
        MaxRadiusText = radius.ToString("0.###", CultureInfo.InvariantCulture);
        double floor = targets.Max(t => _matcher.ResolveProcessFloor(t.Connection));
        MinRadiusText = Math.Max(SuggestedMinRadiusMicrometers, floor).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private bool CanEqualize() => !IsBusy;

    /// <summary>
    /// Removes the length lock of every selected route that has one — target, meander and bend
    /// radius — as one undoable step; the router then routes them normally again.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEqualize))]
    private void ReleaseLengthLock()
    {
        int released = Locks.Release(Targets());
        OnPropertyChanged(nameof(HasLockedSelection));
        SetStatus(false, released == 0
            ? LocalizationService.Instance.Translate("Routing.LengthEqualize.NoneLocked")
            : LengthEqualizationStatus.Format("Routing.LengthEqualize.Released", released));
    }

    /// <summary>Gives every selected connection the common length.</summary>
    [RelayCommand(CanExecute = nameof(CanEqualize))]
    private async Task Equalize()
    {
        // ExecuteAsync (padlock re-equalize) bypasses CanExecute, so a second run must stop here.
        if (IsBusy)
            return;
        var targets = Targets();
        if (targets.Count < MinimumConnections)
            return;
        if (!TryReadMaxRadius(out double maxRadius) || !TryReadMinRadius(maxRadius, out double? minRadius))
            return;

        IsBusy = true;
        var replacement = LengthLockReplacement.Release(targets);
        bool applied = false;
        try
        {
            if (replacement.HasReleased)
                await _canvas.RecalculateRoutesAsync();
            targets = Targets();
            if (targets.Count < MinimumConnections || !TryReadTarget(targets, out double? targetLength))
                return;
            SetStatus(false, string.Format(CultureInfo.CurrentCulture,
                LocalizationService.Instance.Translate("Routing.LengthEqualize.Busy"), targets.Count));
            var plan = _equalizer.Prepare(targets.Select(t => t.Connection).ToList(),
                _canvas.ConnectionManager.Connections.ToList(), _canvas.Components.Select(c => c.Component).ToList(),
                targetLength, maxRadius, DefaultToleranceMicrometers, RerouteAsBundle, minRadius);
            var result = await Task.Run(() => LengthEqualizer.Solve(plan));
            double width = LengthEqualizer.CommonWidth(targets.Select(t => t.Connection));
            bool widthChanged = Apply(result, targets, width, replacement);
            applied = true;
            Report(result, widthChanged ? width : null);
        }
        finally
        {
            if (!applied)
                replacement.Restore();
            IsBusy = false;
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(HasLockedSelection));
        }
    }

    /// <summary>An empty field allows loops down to the process minimum.</summary>
    private bool TryReadMinRadius(double maxRadius, out double? minRadius)
    {
        if (LengthInput.TryParseOptionalMinRadius(MinRadiusText, maxRadius, out minRadius))
            return true;
        SetStatus(true, LocalizationService.Instance.Translate("Routing.LengthEqualize.Error.MinRadius"));
        return false;
    }

    private bool TryReadMaxRadius(out double maxRadius)
    {
        if (LengthInput.TryParse(MaxRadiusText, out maxRadius) && maxRadius > 0)
            return true;
        SetStatus(true, LocalizationService.Instance.Translate("Routing.LengthEqualize.Error.InvalidInput"));
        return false;
    }

    /// <summary>The typed target, checked against the routes as they are once old locks are dropped.</summary>
    private bool TryReadTarget(List<WaveguideConnectionViewModel> targets, out double? targetLength)
    {
        targetLength = null;
        if (string.IsNullOrWhiteSpace(TargetLengthText))
            return true;
        if (!LengthInput.TryParse(TargetLengthText, out double typed) || typed <= 0)
        {
            SetStatus(true, LocalizationService.Instance.Translate("Routing.LengthEqualize.Error.InvalidInput"));
            return false;
        }
        double longest = targets.Max(t => t.Connection.PathLengthMicrometers);
        if (!RerouteAsBundle && typed < longest - DefaultToleranceMicrometers)
        {
            SetStatus(true, string.Format(CultureInfo.CurrentCulture,
                LocalizationService.Instance.Translate("Routing.LengthEqualize.Error.TargetBelowLongest"), longest));
            return false;
        }
        targetLength = typed;
        return true;
    }

    /// <summary>
    /// Records the run as one undoable step, at the common width: matched routes get their meander,
    /// routes without room keep their route but carry the target as an unmet lock (a warning
    /// padlock, so the miss is visible), and released old locks come back on undo. A route the user
    /// changed while the search ran is left alone: its result was computed for geometry that no
    /// longer exists.
    /// </summary>
    /// <returns>True when a matched route's width changed.</returns>
    private bool Apply(LengthEqualizationResult result, List<WaveguideConnectionViewModel> targets, double width,
        LengthLockReplacement replacement)
    {
        bool widthChanged = false;
        var steps = new List<IUndoableCommand>();
        var recorded = new HashSet<WaveguideConnectionViewModel>();
        foreach (var outcome in result.Outcomes)
        {
            var vm = targets.FirstOrDefault(t => ReferenceEquals(t.Connection, outcome.Connection));
            if (vm == null || !ReferenceEquals(vm.Connection.RoutedPath, outcome.BasePath)
                || !(outcome.IsSuccess || MissedForLackOfRoom(outcome)))
                continue;
            var current = LengthMatchState.Capture(vm.Connection);
            var after = current with
            {
                TargetLengthMicrometers = result.TargetLengthMicrometers, ToleranceMicrometers = result.ToleranceMicrometers,
            };
            if (outcome.IsSuccess)
            {
                after = after with
                {
                    Path = outcome.Path, IsRouteFrozen = true, BendRadiusMicrometers = outcome.RadiusMicrometers,
                    WidthMicrometers = width,
                };
                widthChanged |= Math.Abs(vm.Connection.WidthMicrometers - width) > WidthChangeMicrometers;
            }
            steps.Add(new LengthMatchCommand(vm, replacement.BeforeOf(vm), after, "Match route length"));
            recorded.Add(vm);
        }
        steps.AddRange(replacement.UnrecordedSteps(recorded));
        if (steps.Count == 0)
            return false;
        var command = new CompositeCommand($"Equalize route lengths ({steps.Count})", steps);
        if (_canvas.CommandManager is { } commands)
            commands.ExecuteCommand(command);
        else
            command.Execute();
        return widthChanged;
    }

    /// <summary>
    /// A route that only lacked room for its loops; a target below its direct length or a route
    /// left out of a bundle is not a lock it should carry.
    /// </summary>
    private static bool MissedForLackOfRoom(LengthEqualizationOutcome outcome) =>
        !outcome.IsSuccess && !outcome.NotInBundle && outcome.Failure != MeanderFailureReason.TargetShorterThanDirectPath;

    private void Report(LengthEqualizationResult result, double? unifiedWidth) =>
        SetStatus(result.FailedCount > 0, LengthEqualizationStatus.Describe(result, unifiedWidth));

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
    }
}
