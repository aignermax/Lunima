using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Builds an EBeam Mach-Zehnder interferometer the way a student builds it on a blank
/// canvas (issue #1363): SiEPIC library templates placed at snap-grid positions with
/// <see cref="PlaceComponentCommand"/> — no hand-tuned coordinates, no manual path
/// points — and every connection routed by the real router via
/// <see cref="DesignCanvasViewModel.ConnectPinsAsync"/>.
/// <para>
/// Three grating couplers sit on the openEBL 127 µm pitch fiber-array column. The
/// spare coupler (gc_spare) is terminated with a <c>Terminator TE 1550</c>: pin parity
/// makes a fully-connected 3-GC + 2-Y-branch design impossible (9 optical pins), and
/// leaving the spare waveguide pin dangling is a proven openEBL verification error
/// ("Disconnected pin"), so a real submission terminates it.
/// </para>
/// <para>
/// The combiner is placed at the splitter's height and the arms are cross-connected
/// (lower output → upper input), so the second-routed arm must avoid the first and the
/// router is forced to draw unequal arm lengths — the ΔL the fringe measurement needs.
/// The combiner output loops down the right side to gc_out in the coupler column.
/// </para>
/// </summary>
internal static class EBeamFromScratchMziDesign
{
    internal const string EBeamPdkName = "SiEPIC EBeam PDK";
    internal const string GratingCouplerTemplate = "Grating Coupler TE 1550";
    internal const string YBranchTemplate = "Y-Branch 1550";
    internal const string TerminatorTemplate = "Terminator TE 1550";

    internal const double ChipWidthMicrometers = 605.0;
    internal const double ChipHeightMicrometers = 410.0;

    /// <summary>Strip-waveguide group index from the PDK materialDispersion (ng0).</summary>
    internal const double GroupIndex = 4.19088;

    private const double GratingCouplerX = 20.0;
    private const double GratingCouplerPitchMicrometers = 127.0;
    private const double GratingCouplerInY = 20.0;
    private const double GratingCouplerSpareY = GratingCouplerInY + GratingCouplerPitchMicrometers;
    private const double GratingCouplerOutY = GratingCouplerSpareY + GratingCouplerPitchMicrometers;

    private const double SplitterX = 110.0;
    private const double SplitterY = 30.0;
    private const double CombinerX = 290.0;
    private const double CombinerY = 30.0;
    private const double TerminatorX = 110.0;
    private const double TerminatorY = 160.0;

    /// <summary>Places all six components and routes every connection with the router.</summary>
    internal static async Task<DesignCanvasViewModel> BuildRoutedAsync(
        IReadOnlyList<ComponentTemplate> templates)
    {
        var canvas = new DesignCanvasViewModel();
        canvas.ChipMinX = 0;
        canvas.ChipMinY = 0;
        canvas.ChipMaxX = ChipWidthMicrometers;
        canvas.ChipMaxY = ChipHeightMicrometers;
        canvas.InitializeAStarRouting(0, 0, ChipWidthMicrometers, ChipHeightMicrometers);

        var gcIn = Place(canvas, templates, GratingCouplerTemplate, "gc_in",
            GratingCouplerX, GratingCouplerInY);
        var gcOut = Place(canvas, templates, GratingCouplerTemplate, "gc_out",
            GratingCouplerX, GratingCouplerOutY);
        var gcSpare = Place(canvas, templates, GratingCouplerTemplate, "gc_spare",
            GratingCouplerX, GratingCouplerSpareY);
        var splitter = Place(canvas, templates, YBranchTemplate, "mzi_splitter",
            SplitterX, SplitterY);
        var combiner = Place(canvas, templates, YBranchTemplate, "mzi_combiner",
            CombinerX, CombinerY, quarterTurnsCounterClockwise: 2);
        var terminator = Place(canvas, templates, TerminatorTemplate, "term_spare",
            TerminatorX, TerminatorY, quarterTurnsCounterClockwise: 2);

        // The spare coupler's waveguide pin is terminated, so its little sub-circuit is
        // a separate netlist component from the MZI — and SiEPIC's DFT check requires an
        // opt_in label on EVERY sub-circuit that contains a grating coupler.
        gcIn.LaserEnabled = true;
        gcOut.LaserEnabled = false;
        gcSpare.LaserEnabled = true;

        // Cross-connected arms: the descending arm routes first and takes the long way
        // around where the climbing arm will run, so the routed arm lengths differ.
        // The output route comes last: gc_out sits at the bottom of the coupler column,
        // so its corridor down the right side never competes with the arms.
        await canvas.ConnectPinsAsync(Pin(gcIn, "port 2"), Pin(splitter, "port 1"));
        await canvas.ConnectPinsAsync(Pin(splitter, "port 3"), Pin(combiner, "port 3"));
        await canvas.ConnectPinsAsync(Pin(splitter, "port 2"), Pin(combiner, "port 2"));
        await canvas.ConnectPinsAsync(Pin(gcSpare, "port 2"), Pin(terminator, "port 1"));
        await canvas.ConnectPinsAsync(Pin(combiner, "port 1"), Pin(gcOut, "port 2"));

        canvas.ConnectionManager.EnableCoherentPropagationPhase = true;
        await canvas.RecalculateRoutesAsync();
        return canvas;
    }

    private static Component Place(
        DesignCanvasViewModel canvas,
        IReadOnlyList<ComponentTemplate> templates,
        string templateName,
        string identifier,
        double x,
        double y,
        int quarterTurnsCounterClockwise = 0)
    {
        var template = templates.Single(
            t => t.Name == templateName && t.PdkSource == EBeamPdkName);
        var command = PlaceComponentCommand.CreateExact(
            canvas, template, x, y, quarterTurnsCounterClockwise);
        command.Execute();
        command.PlacedComponent!.Identifier = identifier;
        return command.PlacedComponent!;
    }

    private static PhysicalPin Pin(Component component, string pinName) =>
        component.PhysicalPins.Single(p => p.Name == pinName);
}
