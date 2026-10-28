namespace CAP.Avalonia.Services.Lifetime;

/// <summary>Owns terminal retirement of lazily created application features and dialog scopes.</summary>
public sealed class ApplicationWorkCoordinator
{
    private readonly object _gate = new();
    private readonly HashSet<Registration> _features = new();
    private Task? _retirement;

    public IDisposable Register(Func<Task> stopAsync)
    {
        ArgumentNullException.ThrowIfNull(stopAsync);
        lock (_gate)
        {
            if (_retirement != null)
                throw new InvalidOperationException("The application is retiring; new feature activation is closed.");
            var registration = new Registration(this, stopAsync);
            _features.Add(registration);
            return registration;
        }
    }

    public Task StopAsync()
    {
        TaskCompletionSource completion;
        Func<Task>[] features;
        lock (_gate)
        {
            if (_retirement != null) return _retirement;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _retirement = completion.Task;
            features = _features.Select(feature => feature.StopAsync).ToArray();
            _features.Clear();
        }
        _ = RetireAsync(features, completion);
        return completion.Task;
    }

    private static async Task RetireAsync(Func<Task>[] features, TaskCompletionSource completion)
    {
        try
        {
            // Start every retirement even if another owner's synchronous shutdown fails.
            await Task.WhenAll(features.Select(Invoke));
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private static Task Invoke(Func<Task> stop)
    {
        try { return stop(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private sealed class Registration(ApplicationWorkCoordinator owner, Func<Task> stop) : IDisposable
    {
        private ApplicationWorkCoordinator? _owner = owner;
        public Func<Task> StopAsync { get; } = stop;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current == null) return;
            lock (current._gate) current._features.Remove(this);
        }
    }
}
