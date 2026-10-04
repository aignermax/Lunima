using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using UnitTests.Components;

namespace UnitTests.Integration;

/// <summary>
/// Builds the two-chiplet edge-coupler composition for the rung-6 kill-review
/// journey (issue #1208) from the bundled demo PDK: chiplet A is grating coupler
/// (source) → straight waveguide → edge coupler whose fiber port sits on A's
/// right edge (rotated 180° so the fiber port faces outward); chiplet B is edge
/// coupler (fiber port on B's left edge) → straight waveguide (output). The two
/// edge couplers abut fiber-to-fiber across the chiplet boundary, routed through
/// the real router (#923 abutment), the same headless recipe as
/// <see cref="MultiProcessChipletJourneyDesign"/> (#929/#933).
/// </summary>
public sealed class ChipletEdgeCouplerJourneyDesign
{
    public const string DemoPdkFile = "demo-pdk.json";
    public const string ChipletAName = "Transmitter Chiplet";
    public const string ChipletBName = "Receiver Chiplet";

    // S-matrix magnitudes at 1550 nm, straight from demo-pdk.json.
    public const double GratingCoupling = 0.55;    // Grating Coupler fiber→waveguide
    public const double EdgeCoupling = 0.7;        // Edge Coupler, both directions
    public const double WaveguideInsertionLossDb = 0.087; // Straight Waveguide 100µm default

    /// <summary>One 100 µm waveguide's field transmission from the PDK's magnitude formula (10^(-loss/20)).</summary>
    public static readonly double WaveguideThrough = Math.Pow(10, -WaveguideInsertionLossDb / 20.0);

    private ChipletEdgeCouplerJourneyDesign(
        DesignCanvasViewModel canvas,
        ComponentGroup chipletA,
        ComponentGroup chipletB,
        List<ComponentTemplate> templates,
        PdkDraft demoPdk,
        double expectedOutputAmplitude)
    {
        Canvas = canvas;
        ChipletA = chipletA;
        ChipletB = chipletB;
        Templates = templates;
        DemoPdk = demoPdk;
        ExpectedOutputAmplitude = expectedOutputAmplitude;
    }

    public DesignCanvasViewModel Canvas { get; }
    public ComponentGroup ChipletA { get; }
    public ComponentGroup ChipletB { get; }
    public List<ComponentTemplate> Templates { get; }
    public PdkDraft DemoPdk { get; }

    /// <summary>
    /// Expected field amplitude at chiplet B's output: the exact product of the
    /// component S-matrix magnitudes and the routed wires' transmission coefficients
    /// (every wire carries the 0.5 dB/cm default propagation loss of
    /// <c>WaveguideConnection</c> — the wires are short but NOT lossless).
    /// </summary>
    public double ExpectedOutputAmplitude { get; }

    /// <summary>The connectable canvas-side pin behind a group's exposed pin.</summary>
    public static PhysicalPin ExposedPin(ComponentGroup group, string pinName) =>
        group.ExternalPins.Single(p => p.Name == pinName).InternalPin!;

    /// <summary>
    /// Builds the full composition: both chiplets grouped, chiplet B aligned so its
    /// edge coupler's fiber port coincides with chiplet A's, and the coincident pin
    /// pair routed through the real router.
    /// </summary>
    public static ChipletEdgeCouplerJourneyDesign BuildComposed()
    {
        var demo = MultiProcessChipletJourneyDesign.LoadPdk(DemoPdkFile);
        var gratingTemplate = MultiProcessChipletJourneyDesign.TemplateFor(demo, "Grating Coupler");
        var waveguideTemplate = MultiProcessChipletJourneyDesign.TemplateFor(demo, "Straight Waveguide 100µm");
        var edgeTemplate = MultiProcessChipletJourneyDesign.TemplateFor(demo, "Edge Coupler");

        var canvas = new DesignCanvasViewModel();

        // Chiplet A: grating at the origin, waveguide 5 µm behind it, edge coupler
        // rotated 180° (two exact cardinal quarter-turns) so its fiber port faces
        // right — out of chiplet A's right edge. Pin math from the demo PDK:
        // grating waveguide (100,9.5); waveguide a0/b0 (0,5)/(100,5); edge coupler
        // fiber/waveguide (0,9.5)/(100,9.5), swapped by the 180° rotation.
        var grating = Place(canvas, "a_gc", gratingTemplate, 0, 0);
        var waveguideA = Place(canvas, "a_wg", waveguideTemplate, 105, 4.5);
        var edgeA = Place(canvas, "a_ec", edgeTemplate, 210, 0);
        ComponentPoseTransform.Rotate90CounterClockwise(edgeA);
        ComponentPoseTransform.Rotate90CounterClockwise(edgeA);
        Wire(canvas, Pin(grating, "waveguide"), Pin(waveguideA, "a0"));
        Wire(canvas, Pin(waveguideA, "b0"), Pin(edgeA, "waveguide"));

        // Chiplet B, far away: unrotated edge coupler (fiber port on its left edge)
        // feeding the output waveguide.
        var edgeB = Place(canvas, "b_ec", edgeTemplate, 1000, 0);
        var waveguideB = Place(canvas, "b_wg", waveguideTemplate, 1105, 4.5);
        Wire(canvas, Pin(edgeB, "waveguide"), Pin(waveguideB, "a0"));

        var chipletA = Group(canvas, ChipletAName, grating, waveguideA, edgeA);
        var chipletB = Group(canvas, ChipletBName, edgeB, waveguideB);

        // Align chiplet B so the edge couplers' fiber ports coincide exactly.
        var aFiber = ExposedPin(chipletA, "a_ec_fiber");
        var bFiber = ExposedPin(chipletB, "b_ec_fiber");
        var (ax, ay) = aFiber.GetAbsolutePosition();
        var (bx, by) = bFiber.GetAbsolutePosition();
        chipletB.MoveGroup(ax - bx, ay - by);

        // The cross-chiplet edge-coupler link: #923 abutment of coincident opposing pins.
        var path = canvas.Router.Route(aFiber, bFiber);
        path.IsBlockedFallback.ShouldBeFalse("the edge-coupler abutment must not fall back to a blocked route");
        path.IsValid.ShouldBeTrue("the edge-coupler abutment must be a valid route");
        var link = canvas.ConnectPinsWithCachedRoute(aFiber, bFiber, path);
        link.ShouldNotBeNull("the cross-chiplet edge-coupler link must be created");
        link!.Connection.IsRouteFrozen = true;

        double expected = GratingCoupling * WaveguideThrough * EdgeCoupling * EdgeCoupling * WaveguideThrough
            * chipletA.InternalPaths.Aggregate(1.0, (acc, p) => acc * p.TransmissionCoefficient.Magnitude)
            * chipletB.InternalPaths.Aggregate(1.0, (acc, p) => acc * p.TransmissionCoefficient.Magnitude)
            * link.Connection.TransmissionCoefficient.Magnitude;

        return new ChipletEdgeCouplerJourneyDesign(
            canvas,
            chipletA,
            chipletB,
            new List<ComponentTemplate> { gratingTemplate, waveguideTemplate, edgeTemplate },
            demo,
            expected);
    }

    private static Component Place(
        DesignCanvasViewModel canvas, string identifier, ComponentTemplate template, double x, double y)
    {
        var component = ComponentTemplates.CreateFromTemplate(template, x, y);
        component.Identifier = identifier;
        canvas.AddComponent(component, template.Name, template.PdkSource);
        return component;
    }

    /// <summary>Groups the given components (Ctrl+G equivalent) and returns the group.</summary>
    private static ComponentGroup Group(DesignCanvasViewModel canvas, string name, params Component[] children)
    {
        var command = new CreateGroupCommand(
            canvas,
            children.Select(c => canvas.Components.Single(vm => vm.Component == c)).ToList(),
            name);
        command.Execute();
        return command.CreatedGroup.ShouldNotBeNull($"grouping '{name}' must succeed");
    }

    /// <summary>Connects two pins inside one chiplet with an explicit frozen straight route.</summary>
    private static void Wire(DesignCanvasViewModel canvas, PhysicalPin from, PhysicalPin to)
    {
        var connection = canvas.ConnectPinsWithCachedRoute(
            from, to, MultiProcessChipletJourneyDesign.StraightPath(from, to));
        connection.ShouldNotBeNull($"route {from.Name} -> {to.Name} must be created");
        connection!.Connection.IsRouteFrozen = true;
    }

    private static PhysicalPin Pin(Component component, string pinName) =>
        component.PhysicalPins.Single(p => p.Name == pinName);
}
