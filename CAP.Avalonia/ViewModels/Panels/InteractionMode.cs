namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// Interaction mode for the canvas.
/// </summary>
public enum InteractionMode
{
    Select,
    PlaceComponent,
    PlaceGroupTemplate,
    Connect,
    Delete,
    Probe,

    /// <summary>
    /// Eyedropper-style picker (#754): the next click on a coupler designates it as
    /// THE analysis output for the Eye/BER and Transient tabs.
    /// </summary>
    PickAnalysisOutput,

    /// <summary>
    /// Cut tool: guide lines extend from component pins; clicking a highlighted
    /// guide-line/waveguide intersection inserts a crossing component there.
    /// </summary>
    Cut
}

