using CAP.Avalonia.ViewModels.Canvas.ViewSettings;

namespace CAP.Avalonia.ViewModels.Workspaces.Presentation;

/// <summary>
/// How a workspace presents the (shared) design: overlay defaults, drawing emphasis and which
/// tools sit in the overflow instead of the primary toolbar. Pure data — applied on every
/// workspace switch by <c>MainViewModel</c>; it never changes the design or locks an edit.
/// </summary>
/// <param name="PowerFlow">How the CW simulation / power overlay starts in this workspace.</param>
/// <param name="DesignCheckMarkers">How the design-check markers start in this workspace.</param>
/// <param name="Emphasis">What the canvas draws in the foreground.</param>
/// <param name="OverflowTools">Tools moved from the primary toolbar into "More tools".</param>
public sealed record WorkspacePresentation(
    OverlayDefault PowerFlow,
    OverlayDefault DesignCheckMarkers,
    CanvasEmphasis Emphasis,
    IReadOnlyList<WorkspaceTool> OverflowTools)
{
    /// <summary>Layout: geometry and fabrication — checks marked, light hidden, every geometry tool primary.</summary>
    public static readonly WorkspacePresentation Layout = new(
        OverlayDefault.Off, OverlayDefault.On, CanvasEmphasis.Geometry, []);

    /// <summary>Optics: light and analysis — light shown when simulated, geometry receded, Cut in the overflow.</summary>
    public static readonly WorkspacePresentation Optics = new(
        OverlayDefault.OnWhenAvailable, OverlayDefault.Off, CanvasEmphasis.Light, [WorkspaceTool.Cut]);

    /// <summary>Logic and System keep the overlays as they are; Probe is one click away in the overflow.</summary>
    public static readonly WorkspacePresentation Neutral = new(
        OverlayDefault.Keep, OverlayDefault.Keep, CanvasEmphasis.Geometry, [WorkspaceTool.Probe]);

    /// <summary>True when <paramref name="tool"/> sits in the primary toolbar.</summary>
    public bool IsPrimary(WorkspaceTool tool) => !OverflowTools.Contains(tool);
}
