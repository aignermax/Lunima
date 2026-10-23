namespace UnitTests.Integration;

/// <summary>Shared coordinate expectations and script diagnostics for exporter tests.</summary>
internal static class GdsCoordinateVerificationFixture
{
    internal const double PinAlignmentTolerance = 0.01; // µm — tight, 10 nm
    internal const double GcHeight = 19.0;              // µm — Grating Coupler height
    internal const double GcOriginOffsetY = 9.5;        // µm — NazcaOriginOffsetY for GC
    internal const double GcPinOffsetY = 9.5;           // µm — GC waveguide pin Y offset in editor

    internal static string ExcerptScript(string script, int lineCount)
        => string.Join('\n', script.Split('\n').Take(lineCount));
}
