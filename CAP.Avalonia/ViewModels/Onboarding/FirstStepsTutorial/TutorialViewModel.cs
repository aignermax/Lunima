using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>Observes actual optical construction actions; guidance never changes the design.</summary>
public partial class TutorialViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private CanvasInteractionViewModel? _interaction;
    private ObservableCollection<ComponentTemplate>? _templates;
    private readonly List<INotifyPropertyChanged> _observed = new();
    private OpticalTutorialState _state = new();
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _currentStepIndex;
    [ObservableProperty] private bool _isCompleted;

    public TutorialViewModel(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
        string[] actions = ["SelectSource", "PlaceSource", "SelectReceiver", "PlaceReceiver", "ConnectMode", "ConnectPins", "Simulate"];
        string[] targets = ["ComponentLibraryList", "DesignCanvasControl", "ComponentLibraryList", "DesignCanvasControl", "ConnectModeButton", "DesignCanvasControl", "RunSimulationButton"];
        Steps = actions.Select((action, i) => new TutorialStep($"Tutorial.Action{action}.Title",
            $"Tutorial.Action{action}.Body", () => _state.Stage > i || (i == 6 && _state.Connected && _canvas.ShowPowerFlow), targets[i])).ToList();
    }

    /// <summary>Attaches to the actual editor and its visible library; subscriptions live only during guidance.</summary>
    public void AttachInteraction(CanvasInteractionViewModel interaction, ObservableCollection<ComponentTemplate> visibleTemplates)
    {
        Detach();
        _interaction = interaction;
        _templates = visibleTemplates;
        if (IsActive) { Observe(); Evaluate(); }
    }

    public IReadOnlyList<TutorialStep> Steps { get; }
    public TutorialStep CurrentStep => Steps[CurrentStepIndex];
    public string CurrentTitle => LocalizationService.Instance.Translate(CurrentStep.TitleKey);
    public string CurrentBody => LocalizationService.Instance.Translate(CurrentStep.BodyKey);
    public string? CurrentTargetName => CurrentStepIndex is 0 or 2 && _state.Template == null
        || CurrentStepIndex is 1 or 3 && _state.PlacementPoint == null ? null : CurrentStep.TargetName;
    public object? CurrentTargetItem => CurrentStepIndex is 0 or 2 ? _state.Template : null;
    public PhysicalPin? CurrentTargetPin => CurrentStepIndex == 5 ? _state.StartPin : null;
    public PhysicalPin? ConnectionTargetPin => CurrentStepIndex == 5 ? _state.EndPin : null;
    public Point? CurrentCanvasPoint => CurrentStepIndex is 1 or 3 ? _state.PlacementPoint
        : CurrentTargetPin is { } pin ? new Point(pin.GetAbsolutePosition().x, pin.GetAbsolutePosition().y) : null;
    public string CurrentCue => LocalizationService.Instance.Translate(
        CurrentStepIndex is 0 or 2 && _state.Template == null ? "Tutorial.NoCompatibleTemplate"
            : CurrentStepIndex is 1 or 3 && _state.PlacementPoint == null ? "Tutorial.NoPlacementSpace"
            : CurrentStep.TitleKey.Replace(".Title", ".Cue", StringComparison.Ordinal));
    public string ProgressText => string.Format(CultureInfo.InvariantCulture, "{0} {1}/{2}",
        LocalizationService.Instance.Translate("Tutorial.Step"), CurrentStepIndex + 1, Steps.Count);

    public void Start()
    {
        Detach();
        IsCompleted = false;
        IsActive = true;
        Observe();
        Evaluate();
    }

    /// <summary>Rechecks the action, never bypasses it.</summary>
    [RelayCommand] public void Next() => Evaluate();
    [RelayCommand] public void Skip() { IsActive = false; Detach(); }

    private void Observe()
    {
        _canvas.Components.CollectionChanged += OnCollectionChanged;
        _canvas.Connections.CollectionChanged += OnCollectionChanged;
        _canvas.PropertyChanged += OnChanged;
        _canvas.DiesChanged += OnDiesChanged;
        if (_interaction != null) _interaction.PropertyChanged += OnChanged;
        if (_templates != null) _templates.CollectionChanged += OnCollectionChanged;
        ObserveChildren();
    }

    private void ObserveChildren()
    {
        foreach (var item in _observed) item.PropertyChanged -= OnChanged;
        _observed.Clear();
        _observed.AddRange(_canvas.Components);
        _observed.AddRange(_canvas.Surface.Items);
        _observed.AddRange(_canvas.Connections);
        foreach (var item in _observed) item.PropertyChanged += OnChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { ObserveChildren(); Evaluate(); }
    private void OnDiesChanged() { ObserveChildren(); Evaluate(); }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Evaluate();
    private void Evaluate()
    {
        if (!IsActive) return;
        _state = OpticalTutorialState.Read(_canvas, _interaction, _templates);
        CurrentStepIndex = _state.Stage;
        NotifyCurrent();
        if (_state.Connected && _canvas.ShowPowerFlow)
        {
            IsCompleted = true;
            IsActive = false;
            Detach();
        }
    }

    private void Detach()
    {
        _canvas.Components.CollectionChanged -= OnCollectionChanged;
        _canvas.Connections.CollectionChanged -= OnCollectionChanged;
        _canvas.PropertyChanged -= OnChanged;
        _canvas.DiesChanged -= OnDiesChanged;
        if (_interaction != null) _interaction.PropertyChanged -= OnChanged;
        if (_templates != null) _templates.CollectionChanged -= OnCollectionChanged;
        foreach (var item in _observed) item.PropertyChanged -= OnChanged;
        _observed.Clear();
    }

    partial void OnCurrentStepIndexChanged(int value) => NotifyCurrent();
    private void NotifyCurrent()
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentBody));
        OnPropertyChanged(nameof(CurrentCue));
        OnPropertyChanged(nameof(CurrentTargetName));
        OnPropertyChanged(nameof(CurrentTargetItem));
        OnPropertyChanged(nameof(CurrentTargetPin));
        OnPropertyChanged(nameof(ConnectionTargetPin));
        OnPropertyChanged(nameof(CurrentCanvasPoint));
        OnPropertyChanged(nameof(ProgressText));
    }
}
