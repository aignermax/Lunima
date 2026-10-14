using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Gestures;

/// <summary>
/// The connect gesture across a die edge: a waveguide never crosses one. Dragging from an
/// edge-coupler facet to the facing facet on another die creates the die-to-die link
/// (aligning the far die when needed); any other cross-die drag is refused with a hint
/// shown where the wire was dropped.
/// </summary>
public static class DieWireGesture
{
    /// <summary>
    /// Handles a wire request from <paramref name="start"/> to <paramref name="target"/> when the
    /// pins sit on different dies (or one on none); returns false for an ordinary same-die wire.
    /// </summary>
    public static bool TryConnectAcrossDies(DesignCanvasViewModel canvas, MainViewModel? mainVm,
        CanvasInteractionState state, PhysicalPin start, PhysicalPin target)
    {
        if (DieWiringPolicy.Evaluate(start, target).Verdict == DieWiringVerdict.Waveguide)
            return false;

        var result = FacetWireCommandFactory.Create(canvas, start, target);
        var localization = LocalizationService.Instance;
        if (result.Command != null)
        {
            mainVm?.CommandManager.ExecuteCommand(result.Command);
            if (mainVm != null)
                mainVm.StatusText = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    localization.Translate("Dies.Status.Linked"), 1);
            return true;
        }

        var text = localization.Translate(result.RefusalKey ?? "Dies.Wiring.CouplersOnly");
        if (mainVm != null)
            mainVm.StatusText = text;
        var (x, y) = target.GetAbsolutePosition();
        state.WireRefusalHint = new WireRefusalHint(text, x, y, DateTime.UtcNow);
        return true;
    }
}
