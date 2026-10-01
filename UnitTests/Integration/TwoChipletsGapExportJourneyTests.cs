using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.RegularExpressions;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.Export;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Moq;
using Shouldly;
using UnitTests.Components;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1268 (rung 6 → 7 seam): open the shipped
/// <c>Two Chiplets - Edge-Coupler Link</c> example, drag chiplet B 5 µm to the right, and
/// the Nazca/GDS export must NOT draw a waveguide across the resulting free-space gap —
/// two separate dies share no waveguide; the link's transmission is the
/// <see cref="ChipletEdgeCouplerCoupling"/> offset × gap model, not geometry. Each
/// chiplet's own waveguides must still be emitted on their own cross-section (#939/#960),
/// and a save → reload → re-export round trip must reproduce the identical script.
/// </summary>
public class TwoChipletsGapExportJourneyTests
{
    private const int WavelengthNm = 1550;
    private const double GapMicrometers = 5.0;
    private const double PowerTolerance = 1e-6;
    private const double CoordinateTolerance = 0.02; // script coordinates are F2-rounded

    private static readonly Regex StraightRegex = new(
        @"nd\.strt\(length=(-?[\d.]+).*?\)\.put\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)",
        RegexOptions.Compiled);
    private static readonly Regex BendPointRegex = new(
        @"nd\.bend\(.*?\)\.put\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)",
        RegexOptions.Compiled);
    private static readonly Regex PointToPointRegex = new(
        @"\w+_p2p\(\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\), \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)\)",
        RegexOptions.Compiled);

    [Fact]
    public async Task ChipletsMovedApart_Export_DrawsNoWaveguideAcrossTheFreeSpaceGap()
    {
        // 1. Open the shipped example through the real Home example path.
        var canvas = new DesignCanvasViewModel();
        var fileOps = CreateFileOperations(canvas);
        string? migrationWarning = null;
        fileOps.OnProcessMigrationWarning = w => migrationWarning = w;
        (await fileOps.OpenDesignAsCopyAsync(ExamplePath())).ShouldBeTrue("the shipped example must open");
        await fileOps.PostLoadRouting;
        migrationWarning.ShouldBeNull("the shipped bindings describe the design completely");

        var chipletA = Chiplet(canvas, ChipletEdgeCouplerJourneyDesign.ChipletAName);
        var chipletB = Chiplet(canvas, ChipletEdgeCouplerJourneyDesign.ChipletBName);
        var link = canvas.ConnectionManager.Connections.ShouldHaveSingleItem(
            "the example's only connection is the cross-chiplet facet link");
        double aligned = await SimulateOutputAmplitudeAsync(canvas, chipletA, chipletB);
        aligned.ShouldBeGreaterThan(0, "the aligned link transmits light across the chiplet boundary");

        // 2. Drag chiplet B 5 µm to the right via the canvas move command (the user-drag path).
        var chipletBViewModel = canvas.Components.Single(vm => ReferenceEquals(vm.Component, chipletB));
        new CommandManager().ExecuteCommand(new MoveComponentCommand(
            canvas, chipletBViewModel,
            chipletBViewModel.X, chipletBViewModel.Y,
            chipletBViewModel.X + GapMicrometers, chipletBViewModel.Y));

        // 3. Sanity: the output drops by exactly the gap-coupling loss.
        double gapped = await SimulateOutputAmplitudeAsync(canvas, chipletA, chipletB);
        double eta = ChipletEdgeCouplerCoupling.PowerCouplingForGap(GapMicrometers, WavelengthNm);
        (gapped * gapped / (aligned * aligned)).ShouldBe(eta, PowerTolerance,
            $"a {GapMicrometers} µm facet gap must cost exactly η_gap of coupled power");

        // 4+5. Export through the real Nazca export service (no nazca runtime needed):
        // no interconnect waveguide may span the gap between the two facet pins.
        string script = new SimpleNazcaExporter().Export(canvas);
        // The export must state that the facet link is deliberately not drawn as a waveguide.
        script.ShouldContain("# Cross-chiplet facet link");
        var facetA = link.StartPin!.GetAbsoluteNazcaPosition();
        var facetB = link.EndPin!.GetAbsoluteNazcaPosition();
        var waveguides = WaveguideSegmentsOf(script);
        foreach (var segment in waveguides)
        {
            TouchesBothFacets(segment, facetA, facetB).ShouldBeFalse(
                $"no waveguide segment may have one end on each chiplet: {segment}");
            EntersGap(segment, facetA.x, facetB.x).ShouldBeFalse(
                $"no waveguide geometry may enter the free-space gap between the dies: {segment}");
        }

        // Each chiplet's own waveguides are still emitted, on their own side of the gap.
        waveguides.ShouldContain(s => s.MaxX <= facetA.x + CoordinateTolerance,
            "chiplet A's internal waveguides must still be emitted");
        waveguides.ShouldContain(s => s.MinX >= facetB.x - CoordinateTolerance,
            "chiplet B's internal waveguides must still be emitted");
        // the process cross-section interconnects must still be emitted (#939/#960).
        script.ShouldContain("Interconnect(");

        // 6. Save → reload → export again → identical script.
        var tempFile = Path.Combine(Path.GetTempPath(), $"issue1268_{Guid.NewGuid():N}.lun");
        try
        {
            await SaveToFile(fileOps, tempFile);
            var reloadCanvas = new DesignCanvasViewModel();
            var reloadFileOps = CreateFileOperations(reloadCanvas);
            await LoadFromFile(reloadFileOps, tempFile);
            await reloadFileOps.PostLoadRouting;
            new SimpleNazcaExporter().Export(reloadCanvas).ShouldBe(script,
                "save → reload → export must reproduce the identical script");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── Script geometry assertions ─────────────────────────────────────────────

    /// <summary>One emitted waveguide segment (or a bend's anchor point) in Nazca coordinates.</summary>
    private readonly record struct WaveguideSegment(double StartX, double StartY, double EndX, double EndY)
    {
        public double MinX => Math.Min(StartX, EndX);
        public double MaxX => Math.Max(StartX, EndX);
    }

    /// <summary>
    /// Parses every waveguide-geometry line of the script (straights, bend anchors and
    /// pin-to-pin interconnect fallbacks) into segments with both endpoints resolved, so a
    /// <c>nd.strt</c> starting exactly on a facet is still caught by its far endpoint.
    /// </summary>
    private static List<WaveguideSegment> WaveguideSegmentsOf(string script)
    {
        var segments = new List<WaveguideSegment>();
        foreach (var rawLine in script.Split('\n'))
        {
            var line = rawLine.Trim();
            var straight = StraightRegex.Match(line);
            if (straight.Success)
            {
                double length = Number(straight.Groups[1].Value);
                double x = Number(straight.Groups[2].Value);
                double y = Number(straight.Groups[3].Value);
                double angleRadians = Number(straight.Groups[4].Value) * Math.PI / 180.0;
                segments.Add(new WaveguideSegment(
                    x, y,
                    x + length * Math.Cos(angleRadians),
                    y + length * Math.Sin(angleRadians)));
                continue;
            }

            var bend = BendPointRegex.Match(line);
            if (bend.Success)
            {
                double x = Number(bend.Groups[1].Value);
                double y = Number(bend.Groups[2].Value);
                segments.Add(new WaveguideSegment(x, y, x, y));
                continue;
            }

            var p2p = PointToPointRegex.Match(line);
            if (p2p.Success)
            {
                segments.Add(new WaveguideSegment(
                    Number(p2p.Groups[1].Value), Number(p2p.Groups[2].Value),
                    Number(p2p.Groups[4].Value), Number(p2p.Groups[5].Value)));
            }
        }
        return segments;
    }

    private static bool TouchesBothFacets(
        WaveguideSegment segment, (double x, double y) facetA, (double x, double y) facetB) =>
        (Near(segment.StartX, segment.StartY, facetA) && Near(segment.EndX, segment.EndY, facetB))
        || (Near(segment.StartX, segment.StartY, facetB) && Near(segment.EndX, segment.EndY, facetA));

    private static bool Near(double x, double y, (double x, double y) point) =>
        Math.Abs(x - point.x) <= CoordinateTolerance && Math.Abs(y - point.y) <= CoordinateTolerance;

    /// <summary>True when the segment's x-range reaches into the open interval between the two facet planes.</summary>
    private static bool EntersGap(WaveguideSegment segment, double facetAx, double facetBx)
    {
        double gapLow = Math.Min(facetAx, facetBx) + CoordinateTolerance;
        double gapHigh = Math.Max(facetAx, facetBx) - CoordinateTolerance;
        return segment.MinX < gapHigh && segment.MaxX > gapLow;
    }

    private static double Number(string value) =>
        double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    // ── Loading / simulation / persistence helpers (recipes of TwoChipletsExampleTests) ──

    private static string ExamplePath() => Path.Combine(
        ExampleDesignFilesTests.ExamplesDirectory(), TwoChipletsExampleAuthoringTests.ExampleFileName);

    private static ComponentGroup Chiplet(DesignCanvasViewModel canvas, string name) =>
        canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>().Single(g => g.GroupName == name);

    private static async Task<double> SimulateOutputAmplitudeAsync(
        DesignCanvasViewModel canvas, ComponentGroup chipletA, ComponentGroup chipletB)
    {
        var fields = await SimulateAsync(canvas, InjectLight(
            ChipletEdgeCouplerJourneyDesign.ExposedPin(chipletA, "a_gc_fiber")));
        var outFlow = ChipletEdgeCouplerJourneyDesign.ExposedPin(chipletB, "b_wg_b0").LogicalPin!.IDOutFlow;
        return fields.TryGetValue(outFlow, out var value)
            ? value.Magnitude
            : throw new ShouldAssertException($"pin flow {outFlow} missing from simulated fields");
    }

    private static (ExternalInput Input, Guid PinIdInFlow) InjectLight(PhysicalPin pin) =>
        (new ExternalInput("source", new LaserType(LightColor.Red), 0, new Complex(1.0, 0), true),
         pin.LogicalPin!.IDInFlow);

    /// <summary>Runs the S-matrix field propagation over everything currently on the canvas.</summary>
    private static async Task<Dictionary<Guid, Complex>> SimulateAsync(
        DesignCanvasViewModel canvas, params (ExternalInput Input, Guid PinIdInFlow)[] inputs)
    {
        var portManager = new PhysicalExternalPortManager();
        foreach (var (input, pinIdInFlow) in inputs)
        {
            portManager.AddLightSource(input, pinIdInFlow);
        }

        var tileManager = new ComponentListTileManager();
        foreach (var viewModel in canvas.Components)
        {
            tileManager.AddComponent(viewModel.Component);
        }

        var grid = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);
        var calculator = new GridLightCalculator(new SystemMatrixBuilder(grid), grid);
        return await calculator.CalculateFieldPropagationAsync(new CancellationTokenSource(), WavelengthNm);
    }

    private static FileOperationsViewModel CreateFileOperations(DesignCanvasViewModel canvas)
    {
        var demoPdk = MultiProcessChipletJourneyDesign.LoadPdk(ChipletEdgeCouplerJourneyDesign.DemoPdkFile);
        var templates = new List<ComponentTemplate>
        {
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Grating Coupler"),
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Straight Waveguide 100µm"),
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Edge Coupler"),
        };
        var catalog = ProcessCatalog.BuildGroups(new[]
        {
            new PdkProcessEntry(demoPdk.Name, ProcessFingerprintFactory.From(demoPdk)),
        });

        var fileOps = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(templates),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new ErrorConsoleService());
        fileOps.ProcessCatalogProvider = () => catalog;
        return fileOps;
    }

    private static async Task SaveToFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue();
    }

    private static async Task LoadFromFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.LoadDesignCommand.ExecuteAsync(null);
    }
}
