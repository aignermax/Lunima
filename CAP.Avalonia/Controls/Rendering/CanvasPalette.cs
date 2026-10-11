using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// The single source of colors for everything drawn on the design canvas. The canvas is
/// rendered in C#, so it cannot use <c>{DynamicResource}</c>; this class mirrors the neutrals,
/// accent and semantic colors of <c>CAP.Avalonia/Styles/Tokens.axaml</c> (keep both in sync)
/// and adds canvas-only roles (grid, chip, waveguides, device categories).
/// <para>
/// Color means state, as in the rest of the app: the accent marks selection, danger marks
/// blocked / unroutable geometry, warning marks DRC hints. Brushes are immutable and shared,
/// so renderers never allocate a brush per item per frame. Zoom-dependent pens live in
/// <see cref="CanvasPens"/>.
/// </para>
/// </summary>
public static class CanvasPalette
{
    // ── Token mirror (Tokens.axaml) ─────────────────────────────────────────
    /// <summary>Color.Gray950 — canvas background.</summary>
    public static readonly Color Gray950 = Color.Parse("#0B0D11");
    /// <summary>Color.Gray900.</summary>
    public static readonly Color Gray900 = Color.Parse("#111318");
    /// <summary>Color.Gray800.</summary>
    public static readonly Color Gray800 = Color.Parse("#1C2028");
    /// <summary>Color.Gray700.</summary>
    public static readonly Color Gray700 = Color.Parse("#2B313D");
    /// <summary>Color.Gray600.</summary>
    public static readonly Color Gray600 = Color.Parse("#3A4250");
    /// <summary>Color.Gray500.</summary>
    public static readonly Color Gray500 = Color.Parse("#5A6474");
    /// <summary>Color.Gray400 — Text.Tertiary.</summary>
    public static readonly Color Gray400 = Color.Parse("#7D8798");
    /// <summary>Color.Gray300 — Text.Secondary.</summary>
    public static readonly Color Gray300 = Color.Parse("#A3ACBB");
    /// <summary>Color.Gray100 — Text.Primary.</summary>
    public static readonly Color Gray100 = Color.Parse("#E6E9EF");
    /// <summary>Color.Accent — selection and focus.</summary>
    public static readonly Color Accent = Color.Parse("#5B8CFF");
    /// <summary>Color.AccentHover.</summary>
    public static readonly Color AccentHover = Color.Parse("#7AA3FF");
    /// <summary>Color.Success.</summary>
    public static readonly Color Success = Color.Parse("#3DBE84");
    /// <summary>Color.Warning — DRC hints.</summary>
    public static readonly Color Warning = Color.Parse("#F0B13E");
    /// <summary>Color.Danger — blocked / unroutable.</summary>
    public static readonly Color Danger = Color.Parse("#F2676E");
    /// <summary>Color.Info.</summary>
    public static readonly Color Info = Color.Parse("#4FB6EA");

    // ── Canvas roles ────────────────────────────────────────────────────────
    /// <summary>Area outside the chip.</summary>
    public static readonly Color Background = Gray950;
    /// <summary>Chip (die) area, one step lifted from the background.</summary>
    public static readonly Color ChipFill = Color.Parse("#0F1218");
    /// <summary>Minor grid lines.</summary>
    public static readonly Color GridMinor = Color.Parse("#0CFFFFFF");
    /// <summary>Major grid lines.</summary>
    public static readonly Color GridMajor = Color.Parse("#1AFFFFFF");
    /// <summary>Optical waveguides (routed connections and the waveguide layer of previews).</summary>
    public static readonly Color Waveguide = Color.Parse("#3CC7B6");
    /// <summary>Metal (electrical) traces and pins: copper, not a state color.</summary>
    public static readonly Color Copper = Color.Parse("#C98B4E");
    /// <summary>Component body fill without GDS geometry.</summary>
    public static readonly Color ComponentBody = Color.Parse("#181C24");
    /// <summary>Component body fill under a GDS preview: low, so the geometry stays the hero.</summary>
    public static readonly Color ComponentBodyUnderPreview = Color.Parse("#99151920");
    /// <summary>Component border.</summary>
    public static readonly Color ComponentBorder = Color.Parse("#343B48");
    /// <summary>Optical pin glyph.</summary>
    public static readonly Color PinOptical = Color.Parse("#7EDBCF");
    /// <summary>Pin direction stub.</summary>
    public static readonly Color PinDirection = Color.Parse("#99A3ACBB");
    /// <summary>Label backplate behind canvas text.</summary>
    public static readonly Color LabelBackplate = Color.Parse("#C80B0D11");

    // ── Shared brushes ──────────────────────────────────────────────────────
    /// <summary>Canvas background brush.</summary>
    public static readonly IBrush BackgroundBrush = Brush(Background);
    /// <summary>Chip area brush.</summary>
    public static readonly IBrush ChipFillBrush = Brush(ChipFill);
    /// <summary>Primary label text (selected / hovered names, readouts).</summary>
    public static readonly IBrush TextPrimaryBrush = Brush(Gray100);
    /// <summary>Secondary label text (component names at rest).</summary>
    public static readonly IBrush TextSecondaryBrush = Brush(Gray300);
    /// <summary>Tertiary text (HUD, chip dimensions, hints).</summary>
    public static readonly IBrush TextTertiaryBrush = Brush(Gray400);
    /// <summary>Label backplate brush.</summary>
    public static readonly IBrush LabelBackplateBrush = Brush(LabelBackplate);
    /// <summary>Accent brush.</summary>
    public static readonly IBrush AccentBrush = Brush(Accent);
    /// <summary>Accent hover brush.</summary>
    public static readonly IBrush AccentHoverBrush = Brush(AccentHover);
    /// <summary>Danger brush.</summary>
    public static readonly IBrush DangerBrush = Brush(Danger);
    /// <summary>Warning brush.</summary>
    public static readonly IBrush WarningBrush = Brush(Warning);
    /// <summary>Success brush.</summary>
    public static readonly IBrush SuccessBrush = Brush(Success);
    /// <summary>Copper brush (electrical).</summary>
    public static readonly IBrush CopperBrush = Brush(Copper);
    /// <summary>Raised dark surface for chips, badges and icon discs.</summary>
    public static readonly IBrush SurfaceBrush = Brush(WithAlpha(Gray800, 0xEB));
    /// <summary>Hairline border for chips and badges.</summary>
    public static readonly IBrush SurfaceBorderBrush = Brush(Gray600);
    /// <summary>Accent-tinted overlay (hover of a group's children, selection fill).</summary>
    public static readonly IBrush AccentSubtleBrush = Brush(WithAlpha(Accent, 0x24));
    /// <summary>Faint fill used for the selection rectangle.</summary>
    public static readonly IBrush SelectionBoxFillBrush = Brush(WithAlpha(Accent, 0x18));
    /// <summary>Danger-tinted fill (invalid placement, blocked areas).</summary>
    public static readonly IBrush DangerSubtleBrush = Brush(WithAlpha(Danger, 0x30));
    /// <summary>Pen-less white for glyph strokes on handles.</summary>
    public static readonly IBrush HandleStrokeBrush = Brush(Gray100);

    /// <summary>Returns <paramref name="color"/> with the given alpha.</summary>
    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>Blends <paramref name="color"/> toward white by <paramref name="amount"/> (0..1), keeping alpha.</summary>
    public static Color Lighten(Color color, double amount) => Color.FromArgb(color.A,
        (byte)(color.R + (255 - color.R) * amount),
        (byte)(color.G + (255 - color.G) * amount),
        (byte)(color.B + (255 - color.B) * amount));

    /// <summary>Creates an immutable, thread-safe brush.</summary>
    public static IImmutableSolidColorBrush Brush(Color color) => new ImmutableSolidColorBrush(color);
}
