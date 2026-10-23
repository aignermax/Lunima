namespace CAP.Avalonia.Services.Lifetime;

/// <summary>Sequences terminal admission, shell retirement, and release of the application container.</summary>
internal sealed class ApplicationLifetimeCoordinator(
    IApplicationShellLifetime shell, Func<Task> retireAdmission, Func<ValueTask> disposeServices,
    ApplicationWorkCoordinator? features = null)
{
    private bool _stopping;
    private Task? _retirement;

    public Task ActivateAsync() => _stopping ? Task.CompletedTask : shell.ActivateAsync();

    public Task StopAsync()
    {
        _stopping = true;
        return _retirement ??= RetireAsync();
    }

    private async Task RetireAsync()
    {
        // Admission closes synchronously before cancellation begins. Both owners then drain
        // concurrently, while accepted bridge work still has a live shell and container.
        var admission = Invoke(retireAdmission);
        var featureWork = features?.StopAsync() ?? Task.CompletedTask;
        var work = Invoke(shell.StopAsync);
        try { await Task.WhenAll(admission, work, featureWork); }
        finally
        {
            try { await shell.DisposeAsync(); }
            finally { await disposeServices(); }
        }
    }

    private static Task Invoke(Func<Task> operation)
    {
        try { return operation(); }
        catch (Exception error) { return Task.FromException(error); }
    }
}
