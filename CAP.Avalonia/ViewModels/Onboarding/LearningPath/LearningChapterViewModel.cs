using System.Windows.Input;
using CAP.Avalonia.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Onboarding.LearningPath;

public partial class LearningChapterViewModel(int number, string titleKey, ICommand startCommand) : ObservableObject
{
    public int Number { get; } = number;
    public string Title => LocalizationService.Instance.Translate(titleKey);
    public ICommand StartCommand { get; } = startCommand;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isCurrent;
    public string StatusText => LocalizationService.Instance.Translate(IsCompleted ? "LearningPath.Completed"
        : IsCurrent ? "LearningPath.Current" : "LearningPath.Available");
    partial void OnIsCompletedChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    internal void RefreshLanguage() { OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(StatusText)); }
}
