namespace CAP.Avalonia.ViewModels.Canvas.ViewSettings;

/// <summary>
/// What the canvas puts in the foreground. Only the drawing changes — every component stays
/// selectable and editable in either emphasis.
/// </summary>
public enum CanvasEmphasis
{
    /// <summary>Geometry first: component bodies and pins at full strength (Layout).</summary>
    Geometry = 0,

    /// <summary>
    /// Light first: component bodies recede so the light paths read, while sources and
    /// detectors keep full strength and highlighted names (Optics).
    /// </summary>
    Light = 1,
}
