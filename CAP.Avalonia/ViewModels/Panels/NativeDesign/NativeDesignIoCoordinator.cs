using CAP_Core.Documents;
using CAP_DataAccess.Persistence.NativeDesign;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

internal sealed record NativeDesignSaveData(DesignFileData Data, IReadOnlyList<string> UnresolvedPins);

/// <summary>Owns native I/O until cleanup and guards every later editor update against document changes.</summary>
internal sealed class NativeDesignIoCoordinator(DocumentSession document, INativeDesignFileStore files)
{
    public async Task<bool> SaveAsync(string path, Func<NativeDesignSaveData> capture,
        Action<NativeDesignSaveData> publish, Action<Exception> reportFailure,
        CAP_Contracts.ICommitPermission? permission = null)
    {
        await using var operation = TryBegin(permission?.CancellationToken ?? default);
        if (operation == null) return false;
        try
        {
            NativeDesignSaveData saved = null!;
            if (!Publish(operation, permission, () => saved = capture())) return false;
            // Finish an accepted write even if editing continues. Retirement waits
            // for it; only presentation permission is revoked by document changes.
            await files.WriteAsync(path, saved.Data);
            Publish(operation, permission, () => publish(saved));
            return true;
        }
        catch (Exception error)
        {
            Publish(operation, permission, () => reportFailure(error));
            return false;
        }
    }

    public async Task<bool> LoadAsync(string path, Func<bool>? canProceed,
        Action<DesignFileData> apply, Action<string> reportStatus, Action<Exception> reportFailure,
        CAP_Contracts.ICommitPermission? permission = null)
    {
        await using var operation = TryBegin(permission?.CancellationToken ?? default);
        if (operation == null) return false;
        try
        {
            if (!files.Exists(path))
            {
                Publish(operation, permission, () => reportStatus($"File not found: {path}"));
                return false;
            }
            Task<string>? reading = null;
            if (!Publish(operation, permission, () => reading = files.ReadAsync(path, operation.CancellationToken))) return false;
            var json = await reading!;
            if (!operation.IsCurrent || canProceed?.Invoke() == false) return false;
            var data = await Task.Run(() => NativeDesignCodec.Parse(json), operation.CancellationToken);
            if (data == null)
            {
                Publish(operation, permission, () => reportStatus("Invalid design file"));
                return false;
            }
            var applied = false;
            Publish(operation, permission, () =>
            {
                // Applying replaces the document identity itself. Its synchronous
                // failures still belong to this admitted transition, not a stale read.
                try { apply(data); applied = true; }
                catch (Exception error) { reportFailure(error); }
            });
            return applied;
        }
        catch (Exception error)
        {
            Publish(operation, permission, () => reportFailure(error));
            return false;
        }
    }

    private static bool Publish(DocumentOperation operation, CAP_Contracts.ICommitPermission? permission, Action action)
    {
        if (permission == null) return operation.TryPublish(action);
        var entered = false;
        var published = false;
        try
        {
            // Admission lock precedes the document lock. No async work runs in either.
            permission.Commit(() => { entered = true; published = operation.TryPublish(action); });
            return published;
        }
        catch (OperationCanceledException) when (!entered) { return false; }
    }

    private DocumentOperation? TryBegin(CancellationToken cancellationToken = default)
    {
        try { return document.CaptureOperation(cancellationToken, scopeBound: false); }
        catch (OperationCanceledException) { return null; }
    }
}
