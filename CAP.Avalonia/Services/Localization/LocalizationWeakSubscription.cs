using System.ComponentModel;

namespace CAP.Avalonia.Services.Localization;

/// <summary>Observes localization without retaining view models that have no disposal lifecycle.</summary>
internal static class LocalizationWeakSubscription
{
    /// <summary>Subscribes a weak target; the callback must not capture that target strongly.</summary>
    public static IDisposable Subscribe<T>(T target, Action<T, PropertyChangedEventArgs> onChanged,
        LocalizationService? source = null) where T : class
    {
        source ??= LocalizationService.Instance;
        var weakTarget = new WeakReference<T>(target);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (weakTarget.TryGetTarget(out var liveTarget))
                onChanged(liveTarget, args);
            else
                source.PropertyChanged -= handler;
        };
        source.PropertyChanged += handler;
        return new Subscription(() => source.PropertyChanged -= handler);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
