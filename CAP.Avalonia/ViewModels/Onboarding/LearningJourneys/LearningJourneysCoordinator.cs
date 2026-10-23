using System.ComponentModel;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Home;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Onboarding.LearningPath;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Onboarding.LearningJourneys;

/// <summary>Owns chapter navigation, progress subscriptions and cancellation when learning closes.</summary>
internal sealed class LearningJourneysCoordinator : ObservableObject, IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly FileOperationsViewModel _files;
    private readonly HomeViewModel _home;
    private readonly WorkspaceViewModel _workspace;
    private readonly ViewportControlViewModel _viewport;
    private readonly LeftRailViewModel _rail;
    private readonly LearningChapterProgress _progress;
    private int _navigationVersion;
    private bool _disposed;

    public LearningJourneysCoordinator(LearningJourneyChapters chapters, DesignCanvasViewModel canvas,
        FileOperationsViewModel files, HomeViewModel home, ProcessRoutingContext process,
        WorkspaceViewModel workspace, ViewportControlViewModel viewport, LeftRailViewModel rail,
        UserPreferencesService preferences)
    {
        (Chapters, _canvas, _files, _home, _workspace, _viewport, _rail) = (chapters, canvas, files, home, workspace, viewport, rail);
        Path = new LearningPathViewModel(preferences);
        Manufacturing = new ManufacturingTourViewModel(files.GdsExport)
        {
            ActiveProcessProvider = () => files.ActiveProcess,
            ShowProcessDetails = () => rail.ShowPdksCommand.Execute(null)
        };
        Guidance = new LearningGuidanceController(canvas, files, process, chapters.Tutorial, home, workspace, preferences, StopChapters);
        Guidance.PropertyChanged += GuidanceChanged;
        chapters.Tutorial.PropertyChanged += TutorialChanged;
        _progress = new LearningChapterProgress(Path);
        _progress.Observe(chapters.Tutorial, 1, () => chapters.Tutorial.IsActive, () => chapters.Tutorial.IsCompleted);
        _progress.Observe(chapters.FirstGate, 2, () => chapters.FirstGate.IsActive, () => chapters.FirstGate.IsCompleted);
        _progress.Observe(chapters.Watch, 3, () => chapters.Watch.IsActive, () => chapters.Watch.IsCompleted);
        _progress.Observe(chapters.Memory, 4, () => chapters.Memory.IsActive, () => chapters.Memory.IsCompleted);
        _progress.Observe(chapters.Program, 5, () => chapters.Program.IsActive, () => chapters.Program.IsCompleted);
        _progress.Observe(chapters.Chiplets, 6, () => chapters.Chiplets.IsActive, () => chapters.Chiplets.IsCompleted);
        _progress.Observe(Manufacturing, 7, () => Manufacturing.IsActive, () => Manufacturing.IsCompleted);
        home.LearningPath = Path;
        Path.CloseRequested = Close;
        Path.StartChapterRequested = StartChapterAsync;
        home.LearnTutorialRequested = StartTutorialAsync;
        home.FirstGateTourRequested = StartFirstGateAsync;
        home.WatchComputeTourRequested = StartWatchAsync;
        home.StoreNumberTourRequested = StartMemoryAsync;
        home.RunProgramTourRequested = StartProgramAsync;
        home.ConnectChipletsTourRequested = StartChipletsAsync;
        home.ManufacturingTourRequested = StartManufacturingAsync;
    }

    public LearningJourneyChapters Chapters { get; }
    public LearningPathViewModel Path { get; }
    public ManufacturingTourViewModel Manufacturing { get; }
    public LearningGuidanceController Guidance { get; }
    public bool ShowSuccess => Guidance.ShowSuccess;

    private void GuidanceChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(LearningGuidanceController.ShowSuccess)) OnPropertyChanged(nameof(ShowSuccess));
    }

    private void TutorialChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(TutorialViewModel.CurrentTargetName) or nameof(TutorialViewModel.IsActive)
            && Chapters.Tutorial.IsActive) _rail.RevealTourTarget(Chapters.Tutorial.CurrentTargetName);
    }

    private void StopChapters()
    {
        Chapters.Stop();
        Manufacturing.Skip();
        Path.Pause();
    }

    private void Close()
    {
        _navigationVersion++;
        Guidance.Reset();
    }

    private Task StartTutorialAsync()
    {
        var navigation = _navigationVersion;
        return Guidance.StartAsync(() => !_disposed && navigation == _navigationVersion);
    }

    private Task StartChapterAsync(int number) => number switch
    {
        1 => StartTutorialAsync(), 2 => StartFirstGateAsync(), 3 => StartWatchAsync(),
        4 => StartMemoryAsync(), 5 => StartProgramAsync(), 6 => StartChipletsAsync(),
        7 => StartManufacturingAsync(), _ => Task.CompletedTask
    };

    private Task StartFirstGateAsync() => OpenExampleAsync(FirstGateTourViewModel.GateKitExampleFileName,
        Chapters.FirstGate.Start);
    private Task StartWatchAsync() => OpenExampleAsync(WatchComputeTourViewModel.CounterExampleFileName,
        Chapters.Watch.Start, () => Chapters.Watch.IsActive);
    private Task StartMemoryAsync() => OpenExampleAsync(StoreNumberTourViewModel.RamExampleFileName,
        Chapters.Memory.Start, () => Chapters.Memory.IsActive);
    private Task StartProgramAsync() => OpenExampleAsync(RunProgramTourViewModel.AdderExampleFileName,
        Chapters.Program.Start, () => Chapters.Program.IsActive);
    private Task StartChipletsAsync() => OpenExampleAsync(ConnectChipletsTourViewModel.ExampleFileName,
        Chapters.Chiplets.Start);

    private Task StartManufacturingAsync() => OpenExampleAsync(ManufacturingTourViewModel.ExampleFileName,
        () => { _workspace.Current = Workspace.Layout; Manufacturing.Start(); },
        () => Manufacturing.IsActive, reportMissing: true);

    private async Task OpenExampleAsync(string fileName, Action start, Func<bool>? isActive = null, bool reportMissing = false)
    {
        var navigation = _navigationVersion;
        var example = _home.Examples.FirstOrDefault(item => System.IO.Path.GetFileName(item.FilePath) == fileName);
        if (reportMissing)
        {
            _home.IsLearningExampleMissing = example == null || !File.Exists(example.FilePath);
            if (_home.IsLearningExampleMissing) return;
        }
        if (example == null || _disposed) return;
        bool IsCurrent() => !_disposed && navigation == _navigationVersion;
        if (!await _files.OpenDesignAsCopyAsync(example.FilePath, IsCurrent) || !IsCurrent()) return;
        start();
        if (isActive != null) FitStartedJourney(isActive);
    }

    private void FitStartedJourney(Func<bool> isActive)
    {
        var components = _canvas.Components.ToArray();
        var workspace = _workspace.Current;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !isActive() || _workspace.Current != workspace || !_canvas.Components.SequenceEqual(components)) return;
            var (width, height) = _viewport.GetViewportSize?.Invoke() ?? (900, 800);
            _viewport.ZoomToFit(width, height);
        }, DispatcherPriority.Loaded);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _progress.Dispose();
        Guidance.PropertyChanged -= GuidanceChanged;
        Guidance.Dispose();
        Chapters.Tutorial.PropertyChanged -= TutorialChanged;
        Chapters.Detach();
        if (_home.LearningPath == Path) _home.LearningPath = null;
        if (_home.LearnTutorialRequested == StartTutorialAsync) _home.LearnTutorialRequested = null;
        if (_home.FirstGateTourRequested == StartFirstGateAsync) _home.FirstGateTourRequested = null;
        if (_home.WatchComputeTourRequested == StartWatchAsync) _home.WatchComputeTourRequested = null;
        if (_home.StoreNumberTourRequested == StartMemoryAsync) _home.StoreNumberTourRequested = null;
        if (_home.RunProgramTourRequested == StartProgramAsync) _home.RunProgramTourRequested = null;
        if (_home.ConnectChipletsTourRequested == StartChipletsAsync) _home.ConnectChipletsTourRequested = null;
        if (_home.ManufacturingTourRequested == StartManufacturingAsync) _home.ManufacturingTourRequested = null;
        Path.CloseRequested = null;
        Path.StartChapterRequested = null;
        Manufacturing.ActiveProcessProvider = null;
        Manufacturing.ShowProcessDetails = null;
    }
}
