using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

public partial class ConnectChipletsTourViewModel
{
    private WorkspaceViewModel? _workspace;
    private LeftRailViewModel? _rail;

    public void AttachVisualContext(WorkspaceViewModel workspace, LeftRailViewModel rail)
    {
        DetachVisualContext();
        _workspace = workspace;
        _rail = rail;
        if (IsActive) ObserveVisualContext();
        NotifyVisualTargets();
    }

    public bool CanAdvance => IsActive && CurrentStepIndex is 0 or 6;

    private string? VisualTargetName => CurrentStepIndex switch
    {
        0 or 2 when CurrentCanvasPoint == null => null,
        2 when _workspace is { IsSystem: false } => "SystemWorkspaceButton",
        1 when _workspace is { IsOpticalWorkspace: false } => "SystemWorkspaceButton",
        5 when _workspace is { IsSystem: false } => "SystemWorkspaceButton",
        5 when _rail is { IsChipletsActive: false } => "ChipletsRailButton",
        5 => "NewDieButton",
        _ => CurrentStep.TargetName
    };

    public Point? CurrentCanvasPoint => IsActive && CurrentStepIndex is 0 or 2
        && _receiverTracker.Receiver is { } receiver
            ? new Point(receiver.X + receiver.Width / 2, receiver.Y + receiver.Height / 2) : null;

    public Point? CurrentCanvasDestinationPoint
    {
        get
        {
            if (CurrentStepIndex != MoveStepIndex || CurrentCanvasPoint is not { } start) return null;
            var link = _canvas.Connections.FirstOrDefault(c => c.Connection.IsCrossChipletFacetLink)?.Connection;
            if (link == null) return null;
            var angle = link.StartPin.GetAbsoluteAngle() * Math.PI / 180;
            return start + new Point(Math.Cos(angle) * MoveDistanceMicrometers, Math.Sin(angle) * MoveDistanceMicrometers);
        }
    }

    public string CurrentCue => LocalizationService.Instance.Translate(
        CurrentStepIndex is 0 or 2 && CurrentCanvasPoint == null ? "ConnectChipletsTour.MissingReceiverCue"
        : CurrentTargetName == "SystemWorkspaceButton" ? "ConnectChipletsTour.SystemCue"
        : CurrentTargetName == "ChipletsRailButton" ? "ConnectChipletsTour.RailCue"
        : $"ConnectChipletsTour.Action{CurrentStepIndex}Cue");

    private void ObserveVisualContext()
    {
        if (_workspace != null) _workspace.PropertyChanged += OnVisualContextChanged;
        if (_rail != null) _rail.PropertyChanged += OnVisualContextChanged;
        _canvas.Components.CollectionChanged += OnVisualCollectionChanged;
        _canvas.Connections.CollectionChanged += OnVisualCollectionChanged;
    }

    private void DetachVisualContext()
    {
        if (_workspace != null) _workspace.PropertyChanged -= OnVisualContextChanged;
        if (_rail != null) _rail.PropertyChanged -= OnVisualContextChanged;
        _canvas.Components.CollectionChanged -= OnVisualCollectionChanged;
        _canvas.Connections.CollectionChanged -= OnVisualCollectionChanged;
    }

    private void OnVisualContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.Current) or nameof(LeftRailViewModel.ActiveSection))
            NotifyVisualTargets();
    }

    private void OnVisualCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyVisualTargets();

    private void OnReceiverPositionChanged(object? sender, EventArgs e)
    {
        if (CurrentStepIndex == 3 && !ReceiverMoved() && !HasChipletIssue()) CurrentStepIndex = MoveStepIndex;
        NotifyVisualTargets();
        EvaluateCurrentStep();
    }

    private void OnDiesChanged()
    {
        if (IsActive && CurrentStepIndex == 6 && _canvas.Dies.RootDies.Count <= _diesAtStart) CurrentStepIndex = 5;
        NotifyVisualTargets();
        EvaluateCurrentStep();
    }

    private void NotifyVisualTargets()
    {
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(CurrentCue));
        OnPropertyChanged(nameof(CurrentCanvasPoint));
        OnPropertyChanged(nameof(CurrentCanvasDestinationPoint));
        OnPropertyChanged(nameof(CanAdvance));
        OnPropertyChanged(nameof(ShowMoveForMeButton));
        NextCommand.NotifyCanExecuteChanged();
    }
}
