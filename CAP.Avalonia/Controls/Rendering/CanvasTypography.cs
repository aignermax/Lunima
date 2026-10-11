using Avalonia.Media;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Typefaces for text drawn on the canvas: Inter, the app's UI font (<c>Font.Ui</c> in
/// Tokens.axaml), so canvas labels match the panels around them.
/// </summary>
public static class CanvasTypography
{
    private static readonly FontFamily InterFamily = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    /// <summary>Regular UI text (names, readouts, HUD).</summary>
    public static readonly Typeface Ui = new(InterFamily);

    /// <summary>Medium-weight UI text (group tags, selected names).</summary>
    public static readonly Typeface UiMedium = new(InterFamily, FontStyle.Normal, FontWeight.Medium);

    /// <summary>Semi-bold UI text (badges, style tags, mode indicator).</summary>
    public static readonly Typeface UiSemiBold = new(InterFamily, FontStyle.Normal, FontWeight.SemiBold);
}
