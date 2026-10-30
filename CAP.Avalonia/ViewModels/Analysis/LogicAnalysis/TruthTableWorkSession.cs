using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;

/// <summary>Owns extraction work and the configured group's document publication permission.</summary>
internal sealed class TruthTableWorkSession
{
    private readonly ShellWorkScope _work = new();
    private DocumentSession? _standalone;
    private DocumentSession? _document;
    private DocumentIdentity? _identity;
    private Func<bool>? _isMember;
    private AnalysisSweepOperation? _active;
    private Task? _retirement;

    internal long Version { get; private set; }
    internal bool IsStopping => _work.IsStopping;
    internal bool IsVersionCurrent(long version) => !IsStopping && version == Version;
    internal bool IsConfiguredCurrent => !IsStopping && _identity is { } identity
        && _document!.IsCurrentIdentity(identity) && _isMember?.Invoke() == true;

    internal void RegisterWith(ApplicationWorkCoordinator? application, Func<Task> stop) =>
        _work.RegisterWith(application, stop);

    internal void Configure(DocumentSession? document, Func<bool> isMember)
    {
        Invalidate();
        _document = document ?? (_standalone ??= new DocumentSession());
        _isMember = isMember;
        try { _identity = _document.CaptureIdentity(); }
        catch (OperationCanceledException) { _identity = null; }
    }

    internal void Invalidate() { Version++; _active?.Cancel(); }
    internal void Cancel() => _active?.Cancel();
    internal Task RunAsync(Func<CancellationToken, Task> run) => _work.RunAsync(run);

    internal AnalysisSweepOperation? Begin(CancellationToken ownerToken)
    {
        if (!IsConfiguredCurrent || _active != null) return null;
        var version = Version;
        return _active = new AnalysisSweepOperation(_work, _document!,
            () => IsVersionCurrent(version) && IsConfiguredCurrent, ownerToken, ownerToken, _identity);
    }

    internal async ValueTask CompleteAsync(AnalysisSweepOperation operation)
    {
        try { await operation.DisposeAsync(); }
        finally { if (ReferenceEquals(_active, operation)) _active = null; }
    }

    internal Task StopAsync()
    {
        if (_retirement != null) return _retirement;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retirement = completion.Task;
        _ = RetireAsync(completion);
        return completion.Task;
    }

    private async Task RetireAsync(TaskCompletionSource completion)
    {
        try
        {
            var draining = _work.StopAsync();
            Invalidate();
            try { await Task.WhenAll(draining, _standalone?.RetireAsync() ?? Task.CompletedTask); }
            finally { _isMember = null; _document = null; _identity = null; }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
