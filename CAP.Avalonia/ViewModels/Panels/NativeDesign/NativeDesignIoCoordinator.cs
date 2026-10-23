using CAP_Core.Documents;
using CAP_DataAccess.Persistence.NativeDesign;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

internal sealed record NativeDesignSaveData(DesignFileData Data, IReadOnlyList<string> UnresolvedPins);

/// <summary>Owns native I/O until cleanup and guards every later editor update against document changes.</summary>
internal sealed class NativeDesignIoCoordinator(DocumentSession document, INativeDesignFileStore files)
{
    public async Task<bool> SaveAsync(string path, Func<NativeDesignSaveData> capture,
        Action<NativeDesignSaveData> publish, Action<Exception> reportFailure)
    {
        using var operation = TryBegin();
        if (operation == null) return false;
        try
        {
            var saved = capture();
            // Finish an accepted write even if editing continues. Retirement waits
            // for it; only presentation permission is revoked by document changes.
            await files.WriteAsync(path, saved.Data);
            operation.TryPublish(() => publish(saved));
            return true;
        }
        catch (Exception error)
        {
            operation.TryPublish(() => reportFailure(error));
            return false;
        }
    }

    public async Task<bool> LoadAsync(string path, Func<bool>? canProceed,
        Action<DesignFileData> apply, Action<string> reportStatus, Action<Exception> reportFailure)
    {
        using var operation = TryBegin();
        if (operation == null) return false;
        try
        {
            if (!files.Exists(path))
            {
                operation.TryPublish(() => reportStatus($"File not found: {path}"));
                return false;
            }
            var json = await files.ReadAsync(path, operation.CancellationToken);
            if (!operation.IsCurrent || canProceed?.Invoke() == false) return false;
            var data = NativeDesignCodec.Parse(json);
            if (data == null)
            {
                operation.TryPublish(() => reportStatus("Invalid design file"));
                return false;
            }
            var applied = false;
            operation.TryPublish(() =>
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
            operation.TryPublish(() => reportFailure(error));
            return false;
        }
    }

    private DocumentOperation? TryBegin()
    {
        try { return document.CaptureOperation(scopeBound: false); }
        catch (OperationCanceledException) { return null; }
    }
}
