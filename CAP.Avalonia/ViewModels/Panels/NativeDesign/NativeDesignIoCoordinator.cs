using CAP_Core.Documents;
using CAP_DataAccess.Persistence.NativeDesign;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

internal sealed record NativeDesignSaveData(DesignFileData Data, IReadOnlyList<string> UnresolvedPins);

/// <summary>Owns native I/O until cleanup and guards every later editor update against document changes.</summary>
internal sealed class NativeDesignIoCoordinator(DocumentSession document, INativeDesignFileStore files)
{
    /// <summary>
    /// Captures and writes the design. <paramref name="publish"/> receives whether the document is
    /// still exactly what was written, so edits made during the write stay marked as unsaved.
    /// </summary>
    public async Task<bool> SaveAsync(string path, Func<NativeDesignSaveData> capture,
        Action<NativeDesignSaveData, bool> publish, Action<Exception> reportFailure,
        CAP_Contracts.ICommitPermission? permission = null)
    {
        await using var operation = TryBegin(permission?.CancellationToken ?? default);
        if (operation == null) return false;
        try
        {
            NativeDesignSaveData saved = null!;
            DocumentRevisionStamp written = default;
            if (!Publish(operation, permission, () => { saved = capture(); written = document.CaptureRevision(); }))
                return false;
            // Finish an accepted write even if editing continues. Retirement waits
            // for it; only document replacement revokes the follow-up presentation.
            await files.WriteAsync(path, saved.Data);
            Publish(operation, permission, () => publish(saved, document.IsUnchangedSince(written)));
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
        // Revision-bound: edits made while the file is read must not be overwritten by it.
        await using var operation = TryBegin(permission?.CancellationToken ?? default, revisionBound: true);
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

    private DocumentOperation? TryBegin(CancellationToken cancellationToken = default, bool revisionBound = false)
    {
        try { return document.CaptureOperation(cancellationToken, scopeBound: false, revisionBound); }
        catch (OperationCanceledException) { return null; }
    }
}
