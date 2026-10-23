using System.ComponentModel;
using CAP.Avalonia.ViewModels.Onboarding.LearningPath;

namespace CAP.Avalonia.ViewModels.Onboarding.LearningJourneys;

/// <summary>Relays chapter lifecycle into the persistent learning path, with explicit subscription ownership.</summary>
internal sealed class LearningChapterProgress(LearningPathViewModel path) : IDisposable
{
    private readonly List<(INotifyPropertyChanged Chapter, PropertyChangedEventHandler Handler)> _subscriptions = [];

    public void Observe(INotifyPropertyChanged chapter, int number, Func<bool> active, Func<bool> completed)
    {
        void Changed(object? sender, PropertyChangedEventArgs change)
        {
            if (change.PropertyName == "IsActive" && active()) path.Activate(number);
            if (change.PropertyName == "IsCompleted" && completed()) path.Complete(number);
            if (change.PropertyName == "IsActive" && !active() && path.CurrentNumber == number) path.Pause();
        }
        chapter.PropertyChanged += Changed;
        _subscriptions.Add((chapter, Changed));
    }

    public void Dispose()
    {
        foreach (var (chapter, handler) in _subscriptions) chapter.PropertyChanged -= handler;
        _subscriptions.Clear();
    }
}
