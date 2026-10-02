using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CAP_Core.Analysis;
using AnimCanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Looping diagram of the Design Checks help's "Chiplet links" section (#1247): why a
/// lateral offset and a facet gap between two chiplet edge couplers lose light. A beam
/// leaves facet A and widens across the gap (Gaussian divergence); first the gap grows,
/// then facet B shifts sideways. The readout shows the coupled power fraction η — computed
/// with the real <see cref="ChipletEdgeCouplerCoupling.PowerCouplingForOffset"/> /
/// <see cref="ChipletEdgeCouplerCoupling.PowerCouplingForGap"/> at
/// <see cref="WavelengthNm"/>, never a hard-coded number, so the help can never drift from
/// the simulation. The beam's half-width at facet B is the physical one
/// (<c>w(z) = w0 / sqrt(η_gap)</c>), so the spillover past facet B's mode is what the
/// readout counts. Panel-specific, not a reusable primitive: it draws in its own fixed
/// 336×88 coordinate space. Every position and opacity is a pure function of
/// <see cref="HelpAnimationBase.Progress"/>; the last frame keeps the finished state
/// (full gap, full offset, lowest η) visible.
/// </summary>
public class ChipletLinkCouplingAnimation : HelpAnimationBase
{
    /// <summary>Width of the coordinate space the control draws in.</summary>
    public const double SceneWidth = 336;

    /// <summary>Height of the coordinate space the control draws in.</summary>
    public const double SceneHeight = 88;

    /// <summary>Wavelength the coupling readout is evaluated at (the standard C-band).</summary>
    public const double WavelengthNm = 1550;

    /// <summary>Facet gap at the end of the loop, in micrometers.</summary>
    public const double MaxGapMicrometers = 10;

    /// <summary>Lateral offset of facet B at the end of the loop, in micrometers.</summary>
    public const double MaxOffsetMicrometers = 2;

    // Cue windows: the gap grows first, then the lateral offset appears; both hold
    // afterwards so the last frame keeps the finished state visible (HELP-ANIMATIONS.md).
    private static readonly (double Start, double End) GapWindow = (0.05, 0.45);
    private static readonly (double Start, double End) OffsetWindow = (0.55, 0.95);

    /// <summary>Loop positions of the three showcase phases: butt-coupled, gap, gap+offset.</summary>
    public static readonly double[] ShowcasePhases = { 0.0, 0.5, 1.0 };

    private const double FacetWidth = 16;
    private const double FacetHeight = 28;
    private const double FacetTop = 32;
    private const double FacetALeft = 24;
    private const double FacetARight = FacetALeft + FacetWidth;
    private const double AxisY = FacetTop + FacetHeight / 2;
    private const double HalfHeight0 = FacetHeight / 2;
    private const double GapPixelsPerMicrometer = 9;
    private const double OffsetPixelsPerMicrometer = 10;
    private const double SpotOpacity = 0.18;
    private const double BeamBaseOpacity = 0.15;
    private const double BeamCouplingOpacity = 0.35;
    private const double LabelTop = 12;

    // The readout sits top-right, not below the scene: the (?) flyout caps its height and
    // scrolls, so a bottom readout would land below the fold of the Design Checks help.
    private const double ReadoutLeft = 190;
    private const double ReadoutTop = 10;

    private static readonly IBrush FacetBrush = new ImmutableSolidColorBrush(0xFF23232B);
    private static readonly IBrush FacetBorderBrush = new ImmutableSolidColorBrush(0xFF5A5A60);
    private static readonly IBrush BeamBrush = new ImmutableSolidColorBrush(0xFFFFF176);
    private static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(0xFFCFCFD6);

    private readonly Border _facetB;
    private readonly TextBlock _facetBLabel;
    private readonly Polygon _beam;
    private readonly Ellipse _spotAtFacetB;
    private readonly TextBlock _readout;

    /// <summary>Builds the two facets, the widening beam, the spillover spot and the readout.</summary>
    public ChipletLinkCouplingAnimation()
    {
        var canvas = new AnimCanvas
        {
            Width = SceneWidth,
            Height = SceneHeight,
            IsHitTestVisible = false,
        };

        _beam = new Polygon { Fill = BeamBrush, IsHitTestVisible = false };
        canvas.Children.Add(_beam);

        _spotAtFacetB = new Ellipse
        {
            Fill = BeamBrush,
            Opacity = SpotOpacity,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(_spotAtFacetB);

        canvas.Children.Add(CreateFacet(FacetALeft, FacetTop));
        canvas.Children.Add(CreateLabel("A", FacetALeft + 4, LabelTop));

        _facetB = CreateFacet(FacetARight, FacetTop);
        canvas.Children.Add(_facetB);
        _facetBLabel = CreateLabel("B", FacetARight + 4, LabelTop);
        canvas.Children.Add(_facetBLabel);

        _readout = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = LabelBrush,
            IsHitTestVisible = false,
            [AnimCanvas.LeftProperty] = ReadoutLeft,
            [AnimCanvas.TopProperty] = ReadoutTop,
        };
        canvas.Children.Add(_readout);

        Content = canvas;
        RenderFrame(0);
    }

    /// <summary>Facet gap (µm) at a loop position: grows during the gap window, then holds.</summary>
    public static double GapMicrometersAt(double progress) =>
        MaxGapMicrometers * Ramp(progress, GapWindow.Start, GapWindow.End);

    /// <summary>Lateral offset of facet B (µm) at a loop position: appears after the gap.</summary>
    public static double OffsetMicrometersAt(double progress) =>
        MaxOffsetMicrometers * Ramp(progress, OffsetWindow.Start, OffsetWindow.End);

    /// <summary>
    /// Coupled power fraction η at a loop position — the real core coupling functions of
    /// <see cref="ChipletEdgeCouplerCoupling"/> at <see cref="WavelengthNm"/>, so this number
    /// is exactly what the simulation loses on such a link.
    /// </summary>
    public static double PowerCouplingAt(double progress) =>
        ChipletEdgeCouplerCoupling.PowerCouplingForOffset(OffsetMicrometersAt(progress))
        * ChipletEdgeCouplerCoupling.PowerCouplingForGap(GapMicrometersAt(progress), WavelengthNm);

    /// <summary>The readout text shown under the diagram (η as an integer percentage).</summary>
    public static string ReadoutText(double progress) =>
        string.Create(CultureInfo.InvariantCulture, $"η = {PowerCouplingAt(progress) * 100:0} %");

    /// <inheritdoc/>
    protected override void RenderFrame(double progress)
    {
        double gapPixels = GapMicrometersAt(progress) * GapPixelsPerMicrometer;
        double offsetPixels = OffsetMicrometersAt(progress) * OffsetPixelsPerMicrometer;
        double facetBLeft = FacetARight + gapPixels;
        double coupling = PowerCouplingAt(progress);

        AnimCanvas.SetLeft(_facetB, facetBLeft);
        AnimCanvas.SetTop(_facetB, FacetTop + offsetPixels);
        AnimCanvas.SetLeft(_facetBLabel, facetBLeft + 4);

        // w(z) = w0·sqrt(1 + (z/z_R)²) = w0 / sqrt(η_gap): the beam half-width facet B
        // sees, so the spillover past its mode is the physical one.
        double etaGap = ChipletEdgeCouplerCoupling.PowerCouplingForGap(
            GapMicrometersAt(progress), WavelengthNm);
        double halfWidthAtB = HalfHeight0 / Math.Sqrt(etaGap);

        _beam.Points = new Points
        {
            new Point(FacetARight, AxisY - HalfHeight0),
            new Point(facetBLeft, AxisY - halfWidthAtB),
            new Point(facetBLeft, AxisY + halfWidthAtB),
            new Point(FacetARight, AxisY + HalfHeight0),
        };
        _beam.Opacity = BeamBaseOpacity + BeamCouplingOpacity * coupling;

        _spotAtFacetB.Width = 2 * halfWidthAtB;
        _spotAtFacetB.Height = 2 * halfWidthAtB;
        AnimCanvas.SetLeft(_spotAtFacetB, facetBLeft - halfWidthAtB);
        AnimCanvas.SetTop(_spotAtFacetB, AxisY - halfWidthAtB);

        _readout.Text = ReadoutText(progress);
    }

    private static Border CreateFacet(double left, double top) => new()
    {
        Width = FacetWidth,
        Height = FacetHeight,
        Background = FacetBrush,
        BorderBrush = FacetBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        IsHitTestVisible = false,
        [AnimCanvas.LeftProperty] = left,
        [AnimCanvas.TopProperty] = top,
    };

    private static TextBlock CreateLabel(string text, double left, double top) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = LabelBrush,
        IsHitTestVisible = false,
        [AnimCanvas.LeftProperty] = left,
        [AnimCanvas.TopProperty] = top,
    };
}
