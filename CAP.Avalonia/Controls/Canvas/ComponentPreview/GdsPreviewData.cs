using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Immutable snapshot of GDS polygon data ready for canvas rendering.
/// Holds the raw Nazca preview result together with the component dimensions
/// that were used to build the coordinate-space transform.
/// </summary>
/// <param name="Result">Raw result from the Nazca/KLayout preview script.</param>
/// <param name="WidthMicrometers">Component width in µm at the time of fetch.</param>
/// <param name="HeightMicrometers">Component height in µm at the time of fetch.</param>
public sealed record GdsPreviewData(
    NazcaPreviewResult Result,
    double WidthMicrometers,
    double HeightMicrometers);
