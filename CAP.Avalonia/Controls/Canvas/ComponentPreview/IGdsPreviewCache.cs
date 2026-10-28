using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>Persistent preview geometry and interpreter activation identity.</summary>
public interface IGdsPreviewCache
{
    string ActivationIdentity { get; }
    void InvalidateEnvironment();
    bool TryRead(GdsPreviewKey key, out NazcaPreviewResult? result, string? environment = null);
    void Write(GdsPreviewKey key, NazcaPreviewResult result, string? environment = null);
    void WriteEmpty(GdsPreviewKey key, string? environment = null);
}
