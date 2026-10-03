using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Integration;

/// <summary>
/// Builds the EBeam add-drop ring composition (issue #1359) from the bundled SiEPIC
/// EBeam PDK: three grating couplers in a vertical 127 µm openEBL test array
/// (gc_in / gc_through / gc_drop, all 0°) and two <c>DC Halfring-Straight</c>
/// couplers (gap 100 nm, R = 3 µm) facing each other so the ring closes through
/// two straight vertical waveguide segments.
/// <para>
/// Pin math (halfring, 21.52 × 11.46 µm, canvas Y pointing DOWN): unrotated, the
/// ring ports 2/4 sit at the top edge (y ≈ 0, angle 270° = north) and the bus
/// ports 1/3 at the bottom row (y ≈ 10.71). The TOP halfring is therefore rotated
/// 180° (bus row on top at y+0.75, ring ports at the bottom edge pointing south);
/// the BOTTOM halfring stays unrotated (ring ports on top pointing north, bus row
/// at y+10.71). With both at the same x origin, ring_top.port 4 aligns vertically
/// with ring_bottom.port 2 and ring_top.port 2 with ring_bottom.port 4.
/// </para>
/// </summary>
public sealed class EBeamAddDropRingJourneyDesign
{
    /// <summary>Grating-coupler array pitch mandated by openEBL design-for-test.</summary>
    public const double OpenEblPitchMicrometers = 127.0;

    /// <summary>Vertical distance between the two halfrings' ring-port rows (the routed ring segments).</summary>
    public const double RingGapMicrometers = 20.0;

    /// <summary>Ring radius of the ebeam_dc_halfring_straight cells (nazca parameter radius=3E-6).</summary>
    public const double RingRadiusMicrometers = 3.0;

    /// <summary>openEBL die bounds the example is sized for.</summary>
    public const double DieWidthMicrometers = 605.0;
    public const double DieHeightMicrometers = 410.0;

    private const string GratingTemplateName = "Grating Coupler TE 1550";
    private const string HalfringTemplateName = "DC Halfring-Straight";
    private const string EBeamPdkName = "SiEPIC EBeam PDK";

    // Halfring local pin geometry (from siepic-ebeam-pdk.json).
    private const double HalfringHeight = 11.46;
    private const double HalfringBusRowOffsetY = 10.71;
    private const double HalfringRingRowOffsetY = 0.01;

    // GC local pin geometry: port 2 (waveguide) sits at (39.969, 13.669), pointing right.
    private const double GcPort2OffsetY = 13.669;

    private EBeamAddDropRingJourneyDesign(DesignCanvasViewModel canvas, List<ComponentTemplate> templates)
    {
        Canvas = canvas;
        Templates = templates;
    }

    public DesignCanvasViewModel Canvas { get; }
    public List<ComponentTemplate> Templates { get; }

    /// <summary>
    /// Nominal ring circumference: both halfring arcs (πR each) plus the two routed
    /// vertical ring segments. The E2E test re-measures the routed lengths from the
    /// loaded design instead of trusting this constant.
    /// </summary>
    public static double NominalRingLengthMicrometers =>
        2.0 * Math.PI * RingRadiusMicrometers + 2.0 * RingGapMicrometers;

    /// <summary>Composes the add-drop ring on a fresh canvas, routed through the real router.</summary>
    public static EBeamAddDropRingJourneyDesign BuildComposed()
    {
        var templates = TestPdkLoader.LoadAllTemplates();
        var gratingTemplate = TemplateFor(templates, GratingTemplateName);
        var halfringTemplate = TemplateFor(templates, HalfringTemplateName);

        var canvas = new DesignCanvasViewModel();
        canvas.ChipMinX = 0;
        canvas.ChipMinY = 0;
        canvas.ChipMaxX = DieWidthMicrometers;
        canvas.ChipMaxY = DieHeightMicrometers;
        canvas.InitializeAStarRouting(0, 0, DieWidthMicrometers, DieHeightMicrometers);

        // Grating-coupler test array on the left edge: Drop (top), In, Through (bottom).
        var gcDrop = Place(canvas, "gc_drop", gratingTemplate, 20, 40);
        var gcIn = Place(canvas, "gc_in", gratingTemplate, 20, 40 + OpenEblPitchMicrometers);
        var gcThrough = Place(canvas, "gc_through", gratingTemplate, 20, 40 + 2 * OpenEblPitchMicrometers);

        // Top halfring (rotated 180°): its bus row lands at y+0.75, level with
        // gc_in's waveguide port, so the input link is a straight horizontal route.
        double gcInPort2Y = gcIn.PhysicalY + GcPort2OffsetY;
        double ringTopY = gcInPort2Y - (HalfringHeight - HalfringBusRowOffsetY);
        var ringTop = Place(canvas, "ring_top", halfringTemplate, 110, ringTopY);
        ComponentPoseTransform.Rotate90CounterClockwise(ringTop);
        ComponentPoseTransform.Rotate90CounterClockwise(ringTop);

        // Bottom halfring (unrotated): ring-port row RingGapMicrometers below the
        // top halfring's ring-port row.
        double ringBottomY = ringTopY + (HalfringHeight - HalfringRingRowOffsetY)
            + RingGapMicrometers - HalfringRingRowOffsetY;
        var ringBottom = Place(canvas, "ring_bottom", halfringTemplate, 110, ringBottomY);

        // The ring loop: two straight vertical segments between the facing ring ports.
        Route(canvas, Pin(ringTop, "port 4"), Pin(ringBottom, "port 2"));
        Route(canvas, Pin(ringTop, "port 2"), Pin(ringBottom, "port 4"));

        // Buses: In → top bus left, Through ← top bus right, Drop ← bottom bus left.
        Route(canvas, Pin(gcIn, "port 2"), Pin(ringTop, "port 3"));
        Route(canvas, Pin(ringTop, "port 1"), Pin(gcThrough, "port 2"));
        Route(canvas, Pin(ringBottom, "port 1"), Pin(gcDrop, "port 2"));

        canvas.ConnectionManager.EnableCoherentPropagationPhase = true;
        return new EBeamAddDropRingJourneyDesign(canvas, templates);
    }

    /// <summary>
    /// Measures the actual ring circumference of a loaded design: the two halfring arcs
    /// plus the routed lengths of the two ring-segment connections.
    /// </summary>
    public static double MeasureRingLength(DesignCanvasViewModel canvas)
    {
        double segments = FindRingSegment(canvas, "port 4").PathLengthMicrometers
            + FindRingSegment(canvas, "port 2").PathLengthMicrometers;
        return 2.0 * Math.PI * RingRadiusMicrometers + segments;
    }

    /// <summary>Returns one of the two routed ring-segment connections (top-halfring start pin).</summary>
    public static WaveguideConnection FindRingSegment(DesignCanvasViewModel canvas, string startPinName) =>
        canvas.Connections.Single(c =>
            c.Connection.StartPin?.ParentComponent.Identifier == "ring_top"
            && c.Connection.StartPin?.Name == startPinName).Connection;

    private static ComponentTemplate TemplateFor(List<ComponentTemplate> templates, string name) =>
        templates.Single(t => t.Name == name && t.PdkSource == EBeamPdkName);

    private static Component Place(
        DesignCanvasViewModel canvas, string identifier, ComponentTemplate template, double x, double y)
    {
        var component = ComponentTemplates.CreateFromTemplate(template, x, y);
        component.Identifier = identifier;
        canvas.AddComponent(component, template.Name, template.PdkSource);
        return component;
    }

    /// <summary>Routes two pins through the real router and connects them with the cached route.</summary>
    private static void Route(DesignCanvasViewModel canvas, PhysicalPin from, PhysicalPin to)
    {
        RoutedPath path = canvas.Router.Route(from, to);
        path.IsBlockedFallback.ShouldBeFalse(
            $"route {from.ParentComponent.Identifier}.{from.Name} -> {to.ParentComponent.Identifier}.{to.Name} must not be blocked");
        path.IsValid.ShouldBeTrue("the route must be valid geometry");
        var connection = canvas.ConnectPinsWithCachedRoute(from, to, path);
        connection.ShouldNotBeNull("the connection must be created");
    }

    private static PhysicalPin Pin(Component component, string pinName) =>
        component.PhysicalPins.Single(p => p.Name == pinName);
}
