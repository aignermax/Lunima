namespace CAP.Avalonia.Services.Lifetime;

/// <summary>The shell's activation and retirement contract at the application composition boundary.</summary>
public interface IApplicationShellLifetime : IAsyncDisposable
{
    Task ActivateAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
