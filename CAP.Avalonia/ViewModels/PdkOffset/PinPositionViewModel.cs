namespace CAP.Avalonia.ViewModels.PdkOffset;

/// <summary>
/// Display data for a single physical pin in the PDK Offset Editor.
/// Shows the pin's local-component coordinates and its position relative
/// to the Nazca origin (which depends on the current NazcaOriginOffset).
/// </summary>
public class PinPositionViewModel
{
    /// <summary>Pin name (e.g. "a0", "b1").</summary>
    public string PinName { get; }

    /// <summary>Pin X position in component-local µm (from the PDK JSON).</summary>
    public double LocalX { get; }

    /// <summary>Pin Y position in component-local µm (from the PDK JSON).</summary>
    public double LocalY { get; }

    /// <summary>
    /// Pin X position relative to the Nazca cell origin.
    /// Computed as: LocalX - NazcaOriginOffsetX
    /// </summary>
    public double NazcaRelX { get; }

    /// <summary>
    /// Pin Y position relative to the Nazca cell origin in Nazca coordinate space.
    /// Computed as: (ComponentHeight - LocalY) - NazcaOriginOffsetY
    /// Note: Y is flipped because Nazca uses a top-down Y axis vs Lunima's bottom-up.
    /// </summary>
    public double NazcaRelY { get; }

    /// <summary>Formatted string for table display.</summary>
    public string LocalXText => $"{LocalX:F3} µm";

    /// <summary>Formatted string for table display.</summary>
    public string LocalYText => $"{LocalY:F3} µm";

    /// <summary>Formatted string for table display.</summary>
    public string NazcaXText => $"{NazcaRelX:F3} µm";

    /// <summary>Formatted string for table display.</summary>
    public string NazcaYText => $"{NazcaRelY:F3} µm";

    /// <summary>
    /// Initializes pin position data given local coordinates and the component's
    /// current NazcaOriginOffset and height.
    /// </summary>
    /// <param name="pinName">Pin identifier.</param>
    /// <param name="localX">Pin X offset in µm from component origin.</param>
    /// <param name="localY">Pin Y offset in µm from component origin.</param>
    /// <param name="componentHeight">Component height in µm (for Y-flip).</param>
    /// <param name="nazcaOffsetX">Current NazcaOriginOffsetX in µm.</param>
    /// <param name="nazcaOffsetY">Current NazcaOriginOffsetY in µm.</param>
    public PinPositionViewModel(
        string pinName,
        double localX,
        double localY,
        double componentHeight,
        double nazcaOffsetX,
        double nazcaOffsetY)
    {
        PinName = pinName;
        LocalX = localX;
        LocalY = localY;
        NazcaRelX = localX - nazcaOffsetX;
        NazcaRelY = (componentHeight - localY) - nazcaOffsetY;
    }
}
