namespace CAP.Avalonia.Services.Lifetime;

/// <summary>Owns callback slots and restores them only while its installed callback remains current.</summary>
internal sealed class CallbackBindings : IDisposable
{
    private readonly List<Action> _release = new();
    public bool IsDisposed { get; private set; }

    public void Bind<T>(Func<T?> read, Action<T?> write, T callback) where T : Delegate
    {
        if (IsDisposed) return;
        var previous = read();
        write(callback);
        _release.Add(() => { if (Equals(read(), callback)) write(previous); });
    }

    public void BindReference<T>(Func<T?> read, Action<T?> write, T value) where T : class
    {
        if (IsDisposed) return;
        var previous = read();
        write(value);
        _release.Add(() => { if (ReferenceEquals(read(), value)) write(previous); });
    }

    public void Observe(System.ComponentModel.INotifyPropertyChanged source,
        System.ComponentModel.PropertyChangedEventHandler handler)
    {
        if (IsDisposed) return;
        source.PropertyChanged += handler;
        _release.Add(() => source.PropertyChanged -= handler);
    }

    public void Dispose()
    {
        IsDisposed = true;
        for (var index = _release.Count - 1; index >= 0; index--) _release[index]();
        _release.Clear();
    }
}
