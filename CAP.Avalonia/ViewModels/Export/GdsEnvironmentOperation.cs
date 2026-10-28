using CAP.Avalonia.Services.Lifetime;

namespace CAP.Avalonia.ViewModels.Export;

/// <summary>One settings operation's cancellation and publication permission, shared by its nested steps.</summary>
internal sealed class GdsEnvironmentOperation : IAsyncDisposable
{
    private readonly ShellWorkScope _owner;
    private readonly CancellationToken _caller;
    private readonly AsyncCancellationScope _cancellation;
    public CancellationToken Token => _cancellation.Token;
    public bool IsCancellationRequested => _owner.IsStopping || _caller.IsCancellationRequested || Token.IsCancellationRequested;

    public GdsEnvironmentOperation(ShellWorkScope owner, CancellationToken lifetime, CancellationToken caller)
    {
        _owner = owner;
        _caller = caller;
        _cancellation = new(lifetime, caller);
    }

    public void Check()
    {
        _owner.ThrowIfStopping();
        _caller.ThrowIfCancellationRequested();
        Token.ThrowIfCancellationRequested();
    }

    public void Publish(Action action)
    {
        Check();
        action();
        Check();
    }

    public ValueTask DisposeAsync() => _cancellation.DisposeAsync();
}
