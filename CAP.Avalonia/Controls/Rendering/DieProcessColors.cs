using Avalonia.Media;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// One color per process family, used for a die's frame on the canvas and its color chip in
/// the Chiplets panel, so a SiN die and an InP die are told apart at a glance. Colors come
/// from the canvas design tokens; unknown or unbound processes stay neutral gray.
/// </summary>
public static class DieProcessColors
{
    /// <summary>The frame color for a die bound to <paramref name="process"/>.</summary>
    public static Color For(ActiveProcessSelection? process)
    {
        if (process == null || process.IsPlayground)
            return CanvasPalette.Gray400;
        var material = process.Fingerprint?.CoreMaterial ?? process.DisplayName;
        if (Contains(material, "InP") || Contains(material, "InGaAsP"))
            return CanvasPalette.Warning;
        if (Contains(material, "SiN") || Contains(material, "Si3N4") || Contains(material, "nitride"))
            return CanvasPalette.Success;
        if (Contains(material, "LN") || Contains(material, "niobate"))
            return CanvasPalette.Copper;
        if (Contains(material, "Si") || Contains(material, "SOI"))
            return CanvasPalette.Accent;
        return CanvasPalette.Info;
    }

    /// <summary>Brush form of <see cref="For"/>.</summary>
    public static IBrush BrushFor(ActiveProcessSelection? process) => CanvasPalette.Brush(For(process));

    private static bool Contains(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
