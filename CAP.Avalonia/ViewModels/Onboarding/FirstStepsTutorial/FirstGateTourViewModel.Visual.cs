using Avalonia;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

public partial class FirstGateTourViewModel
{
    private readonly HashSet<Component> _kitParts = new();
    private WorkspaceViewModel? _workspace;
    private AnalysisDockViewModel? _dock;
    private CanvasInteractionViewModel? _interaction;

    public Action<ComponentGroup>? RevealGateRequested { get; set; }

    public bool ShowRevealGate => IsActive && CurrentStepIndex >= 2 && FindKitGroup() is { } group &&
        (!IsKitGroupSelected(group) || !ReferenceEquals(_makeGate.ConfiguredGroup, group));

    [RelayCommand]
    private void RevealGate()
    {
        if (ShowRevealGate && FindKitGroup() is { } group)
            RevealGateRequested?.Invoke(group);
    }

    private bool IsKitGroupSelected(ComponentGroup group) => _canvas.Selection.SelectedComponents.Count == 1 &&
        ReferenceEquals(_canvas.Selection.SelectedComponents[0].Component, group);

    private bool IsKitEditorOpen() => FindKitGroup() is { } group && IsKitGroupSelected(group) &&
        ReferenceEquals(_makeGate.ConfiguredGroup, group) && _makeGate.IsActive;

    private bool HasSavedKit() => _makeGate.IsSaved && FindKitGroup()?.TruthTablePinAssignment != null;

    public bool IsIntroduction => CurrentStepIndex == 0;
    public string CurrentCue => LocalizationService.Instance.Translate(CurrentStepIndex switch
    {
        0 => "FirstGateTour.Cue.Start",
        _ when ShowRevealGate => "FirstGateTour.RevealGate",
        _ when CurrentTargetName == "LogicWorkspaceButton" => "FirstGateTour.Cue.Logic",
        1 when CurrentTargetName == "LayoutWorkspaceButton" => "Workspace.Layout",
        1 when CurrentTargetName == "SelectModeButton" => "FirstGateTour.Cue.SelectMode",
        1 when HasKitSelection() => "FirstGateTour.Cue.Group",
        1 => "FirstGateTour.Cue.SelectParts",
        2 => "MakeGate.Open",
        3 => "MakeGate.Simulate",
        4 => "FirstGateTour.Cue.Nand",
        5 => "MakeGate.Save",
        _ when CurrentTargetName == "LogicBuildButton" => "FirstGateTour.Cue.Build",
        _ => "MakeGate.OpenInLogic"
    });

    public Point? CurrentCanvasPoint => SelectionBounds() is { } bounds ? bounds.TopLeft : null;
    public Point? CurrentCanvasDestinationPoint => SelectionBounds() is { } bounds ? bounds.BottomRight : null;

    public void AttachUi(WorkspaceViewModel workspace, AnalysisDockViewModel dock, CanvasInteractionViewModel interaction)
    {
        _workspace = workspace;
        _dock = dock;
        _interaction = interaction;
    }

    private void CaptureKit()
    {
        _kitParts.Clear();
        foreach (var vm in _canvas.Components)
        {
            if (vm.Component is ComponentGroup { IsDie: false } group)
                _kitParts.UnionWith(group.ChildComponents);
            else if (vm.Component is not ComponentGroup)
                _kitParts.Add(vm.Component);
        }
    }

    private ComponentGroup? FindKitGroup() => _kitParts.Count < 2 ? null :
        _canvas.Components.Select(c => c.Component).OfType<ComponentGroup>()
            .FirstOrDefault(g => !g.IsDie && _kitParts.IsSubsetOf(g.ChildComponents));

    private bool HasKitSelection() => _kitParts.Count >= 2 &&
        _kitParts.IsSubsetOf(_canvas.Selection.SelectedComponents.Select(c => c.Component));

    private string? ResolveTargetName()
    {
        if (!IsActive || IsIntroduction || ShowRevealGate) return null;
        if (CurrentStepIndex == 1)
        {
            if (_workspace?.IsLogic == true) return "LayoutWorkspaceButton";
            if (_interaction != null && _interaction.CurrentMode != InteractionMode.Select) return "SelectModeButton";
            return HasKitSelection() ? "CreateGroupButton" : "Canvas";
        }
        if (CurrentStepIndex is >= 2 and <= 5 && _workspace?.ShowsGateTools == false)
            return "LogicWorkspaceButton";
        if (CurrentStepIndex == 6 && _dock?.IsVisible == true &&
            _dock.SelectedTabIndex == (int)AnalysisDockTab.Logic)
            return "LogicBuildButton";
        return CurrentStep.TargetName;
    }

    private Rect? SelectionBounds()
    {
        if (!IsActive || CurrentStepIndex != 1 || CurrentTargetName != "Canvas") return null;
        var parts = _canvas.Components.Where(c => _kitParts.Contains(c.Component)).ToArray();
        if (parts.Length == 0) return null;
        var left = parts.Min(c => c.X);
        var top = parts.Min(c => c.Y);
        return new Rect(left - 16, top - 16, parts.Max(c => c.X + c.Width) - left + 32,
            parts.Max(c => c.Y + c.Height) - top + 32);
    }

    private void AttachVisualObservers()
    {
        _canvas.Selection.SelectedComponents.CollectionChanged += OnCanvasChanged;
        if (_workspace != null) _workspace.PropertyChanged += OnObservedPropertyChanged;
        if (_dock != null) _dock.PropertyChanged += OnObservedPropertyChanged;
        if (_interaction != null) _interaction.PropertyChanged += OnObservedPropertyChanged;
    }

    private void DetachVisualObservers()
    {
        _canvas.Selection.SelectedComponents.CollectionChanged -= OnCanvasChanged;
        if (_workspace != null) _workspace.PropertyChanged -= OnObservedPropertyChanged;
        if (_dock != null) _dock.PropertyChanged -= OnObservedPropertyChanged;
        if (_interaction != null) _interaction.PropertyChanged -= OnObservedPropertyChanged;
        _kitParts.Clear();
    }

    private void NotifyVisualTargets()
    {
        OnPropertyChanged(nameof(IsIntroduction));
        OnPropertyChanged(nameof(ShowRevealGate));
        OnPropertyChanged(nameof(CurrentCue));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(CurrentCanvasPoint));
        OnPropertyChanged(nameof(CurrentCanvasDestinationPoint));
    }
}
