using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>Owns editor work and permission to publish or change the configured live gate.</summary>
internal sealed class MakeGateWorkSession
{
    private readonly ShellWorkScope _work = new();
    private DocumentSession? _standalone;
    private DocumentSession? _document;
    private DocumentIdentity? _identity;
    private Func<bool>? _isMember;
    private AnalysisSweepOperation? _active;
    private long _version;
    private Task? _retirement;

    internal bool IsStopping => _work.IsStopping;
    internal long Version => _version;
    internal bool IsCurrent(long version) => version == _version && IsConfiguredCurrent;
    internal bool IsConfiguredCurrent => !IsStopping && _identity is { } identity
        && _document!.IsCurrentIdentity(identity) && _isMember?.Invoke() == true;

    internal void Configure(DocumentSession? document, Func<bool> isMember)
    {
        Invalidate();
        _document = document ?? (_standalone ??= new DocumentSession());
        _isMember = isMember;
        try { _identity = _document.CaptureIdentity(); }
        catch (OperationCanceledException) { _identity = null; }
    }

    internal void Invalidate() { _version++; _active?.Cancel(); }
    internal void Cancel() => _active?.Cancel();
    internal Task RunAsync(Func<CancellationToken, Task> run) => _work.RunAsync(run);

    internal AnalysisSweepOperation? Begin(CancellationToken ownerToken)
    {
        if (!IsConfiguredCurrent) return null;
        var version = _version;
        return _active = new AnalysisSweepOperation(_work, _document!,
            () => version == _version && IsConfiguredCurrent, ownerToken, ownerToken, _identity);
    }

    internal async ValueTask CompleteAsync(AnalysisSweepOperation? operation)
    {
        if (ReferenceEquals(_active, operation)) _active = null;
        if (operation != null) await operation.DisposeAsync();
    }

    internal bool TryCommit(long version, Action commit)
    {
        if (!IsCurrent(version)) return false;
        try
        {
            using var operation = _document!.CaptureOperation(_identity!.Value);
            return operation.TryCommitAndAdvance(() =>
            {
                // The document checks its identity under the actual commit lock. During a commit,
                // IsCurrentIdentity intentionally rejects other observers, so check only this editor here.
                if (IsStopping || version != _version || _isMember?.Invoke() != true)
                    throw new OperationCanceledException();
                commit();
            });
        }
        catch (OperationCanceledException) { return false; }
    }

    internal Task StopAsync() => _retirement ??= RetireAsync();

    private async Task RetireAsync()
    {
        var draining = _work.StopAsync();
        Invalidate();
        try
        {
            await Task.WhenAll(draining, _standalone?.RetireAsync() ?? Task.CompletedTask);
        }
        finally { _isMember = null; _document = null; _identity = null; }
    }
}
