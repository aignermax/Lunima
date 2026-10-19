using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.LearningPath;

/// <summary>Course navigation and earned progress, independent of the open design.</summary>
public partial class LearningPathViewModel : ObservableObject
{
    private readonly UserPreferencesService _preferences;
    private bool _active;
    private bool _starting;
    private bool _cancelPendingStart;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isCelebrating;
    public double PanelWidth => IsExpanded ? 244 : 44;
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(PanelWidth));
    public int CurrentNumber { get; private set; }
    public Func<int, Task>? StartChapterRequested { get; set; }
    public Action? CloseRequested { get; set; }
    public event Action? Pausing;
    public bool IsPaused => !_active && !IsCelebrating;
    public IReadOnlyList<LearningChapterViewModel> Chapters { get; }
    public int CompletedCount => Chapters.Count(chapter => chapter.IsCompleted);
    public string ProgressText => string.Format(LocalizationService.Instance.Translate("LearningPath.Progress"), CompletedCount, Chapters.Count);
    public string CurrentTitle => Chapters[CurrentNumber - 1].Title;
    public string RewardText => LocalizationService.Instance.Translate($"LearningPath.Reward{CurrentNumber}");
    public bool CanContinue => !_starting && CurrentNumber < Chapters.Count && Chapters[CurrentNumber - 1].IsCompleted;
    public string NextLabel => CurrentNumber < Chapters.Count
        ? LocalizationService.Instance.Translate("LearningPath.Next") + " " + Chapters[CurrentNumber].Title
        : LocalizationService.Instance.Translate("LearningPath.Completed");
    public string ResumeLabel => LocalizationService.Instance.Translate(CompletedCount == 0 ? "LearningPath.Start" : "LearningPath.Resume");

    public LearningPathViewModel(UserPreferencesService preferences)
    {
        _preferences = preferences;
        string[] titles = ["Home.Learn.FirstSteps", "Home.FirstGateTour", "Home.WatchComputeTour", "Home.StoreNumberTour",
            "Home.RunProgramTour", "Home.ConnectChipletsTour", "LearningPath.Manufacturing"];
        Chapters = titles.Select((title, index) => new LearningChapterViewModel(index + 1, title,
            new AsyncRelayCommand(() => StartChapterAsync(index + 1), () => !_starting))).ToArray();
        var completed = preferences.GetCompletedLearningChapters();
        foreach (var chapter in Chapters) chapter.IsCompleted = completed.Contains(chapter.Number);
        CurrentNumber = preferences.GetLastLearningChapter();
        Chapters[CurrentNumber - 1].IsCurrent = true;
        LocalizationWeakSubscription.Subscribe(this, static (self, _) => self.Refresh());
    }

    public void Activate(int number)
    {
        if (_starting && _cancelPendingStart) return;
        if (number < 1 || number > Chapters.Count) return;
        CurrentNumber = number;
        _active = true;
        IsVisible = true;
        IsCelebrating = false;
        foreach (var chapter in Chapters) chapter.IsCurrent = chapter.Number == number;
        _preferences.SetLearningProgress(Chapters.Where(c => c.IsCompleted).Select(c => c.Number), number);
        Refresh();
    }

    public void Complete(int number)
    {
        if (!_active || number != CurrentNumber) return;
        _active = false;
        Chapters[number - 1].IsCompleted = true;
        IsCelebrating = true;
        _preferences.SetLearningProgress(Chapters.Where(c => c.IsCompleted).Select(c => c.Number), number);
        Refresh();
    }

    public void Pause() { _active = false; Refresh(); }

    private async Task StartChapterAsync(int number)
    {
        if (_starting || StartChapterRequested == null) return;
        _starting = true;
        _cancelPendingStart = false;
        Refresh();
        try { await StartChapterRequested(number); }
        finally { _starting = false; Refresh(); }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private Task Continue() => StartChapterAsync(CurrentNumber + 1);
    [RelayCommand] private Task Resume() => StartChapterAsync(CanContinue ? CurrentNumber + 1 : CurrentNumber);
    [RelayCommand] private void Reveal() { IsVisible = true; IsExpanded = true; Refresh(); }
    [RelayCommand] private void Toggle() => IsExpanded = !IsExpanded;
    [RelayCommand] private void Close()
    {
        if (IsVisible) Pausing?.Invoke();
        _cancelPendingStart = true;
        CloseRequested?.Invoke();
        IsCelebrating = false;
        Pause();
        IsVisible = false;
    }

    private void Refresh()
    {
        foreach (var chapter in Chapters)
        {
            chapter.RefreshLanguage();
            ((IAsyncRelayCommand)chapter.StartCommand).NotifyCanExecuteChanged();
        }
        OnPropertyChanged(nameof(CurrentNumber));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(RewardText));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(ResumeLabel));
        OnPropertyChanged(nameof(IsPaused));
        ContinueCommand.NotifyCanExecuteChanged();
    }
}
