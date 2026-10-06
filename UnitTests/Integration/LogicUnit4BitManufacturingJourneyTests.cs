using System.Diagnostics;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Export;
using UnitTests.Export.CornerstoneDrc;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// The rung 5→7 chain for the first chip that computes two ISA operations (issue #1298):
/// the shipped <c>examples/Logic Gate Logic Unit 4-bit.lun</c> — eight gate slices side by
/// side (four AND-from-NAND slices AND0–AND3, four NOT-NAND slices NOT0–NOT3), with the
/// operand word A0–A3 fanning out into both gate rows through persisted signal names — must
/// be tapeout-clean, not just logically correct. The journey loads the design through the
/// real load path, runs Design Validation (DRC-lite, <see cref="DesignValidator"/>) over the
/// loaded slices, proves at the logic layer that all 256 operand pairs give Y = A &amp; B and
/// all 16 A give N = ~A &amp; 0xF, exports through the app's own Nazca exporter (nothing
/// skipped, no warnings, one waveguide chain per routed path), runs real nazca → GDS plus the
/// vendored CORNERSTONE SiN pre-DRC deck headless (nazca/KLayout gated, same skip behavior as
/// the 4-bit adder's manufacturing journey #1036: skips locally, runs in CI), and pins the
/// empty violation set as the baseline. The GDS sanity check reads the export back with the
/// app's own <see cref="GdsReader"/>: one cell reference per leaf component, and no waveguide
/// polygon outside the slice footprints — the fan-out layout with eight slices side by side
/// is exactly where spacing/overlap violations hide, so a polygon escaping its slice fails
/// the journey here. Load, network assembly and the logic-layer input mapping are delegated
/// to the pinned logic-unit fixture (#1286/#1291) so this journey reuses — never forks — its
/// gate model.
/// </summary>
public class LogicUnit4BitManufacturingJourneyTests
    : IClassFixture<LogicUnit4BitManufacturingJourneyTests.ManufacturingJourneyFixture>
{
    private const int ExpectedSliceCount = 8;
    private const int ExpectedLeafComponentCount = 60;
    private const int ExpectedFrozenPathCount = 52;

    /// <summary>
    /// Slack for the polygon-inside-footprint check: covers the half waveguide width
    /// (0.25 µm) a waveguide overhangs an edge pin by plus the exporter's F2 coordinate
    /// rounding — far below the inter-slice spacing, so a polygon that escaped into a
    /// neighbouring slice still fails.
    /// </summary>
    private const double FootprintToleranceMicrometers = 1.0;

    private readonly ManufacturingJourneyFixture _journey;

    /// <summary>Attaches the shared journey fixture.</summary>
    public LogicUnit4BitManufacturingJourneyTests(ManufacturingJourneyFixture journey) => _journey = journey;

    [Fact]
    public void Step1_Load_ExampleArrivesAsSideBySideSlicesWithPersistedRoles()
    {
        _journey.Groups.Select(g => g.GroupName).ShouldBe(
            Enumerable.Range(0, 4).SelectMany(i => new[] { $"AND{i}", $"NOT{i}" }).ToArray(),
            ignoreOrder: true);
        _journey.Groups.Count.ShouldBe(ExpectedSliceCount,
            "one AND slice and one NOT slice per bit of the 4-bit words");
        _journey.Groups.ShouldAllBe(g => g.TruthTablePinAssignment != null,
            "every slice must carry its persisted pin roles for the manufacturing path to matter");
        _journey.Groups.ShouldAllBe(g => g.RotationDegrees == 0,
            "the shipped example places its eight slices unrotated — the GDS footprint check below " +
            "maps their axis-aligned rectangles one to one");
        _journey.Canvas.Connections.Count.ShouldBe(0,
            "the slices stand side by side without wires — the operand fan-out travels through " +
            "persisted signal names, not routed connections");
        LeafComponents().Count.ShouldBe(ExpectedLeafComponentCount,
            "nine gates per AND slice, six per NOT slice");
        _journey.Groups.Sum(g => g.InternalPaths.Count).ShouldBe(ExpectedFrozenPathCount,
            "the slices' internal wiring is frozen into the groups (8 paths per AND slice, 5 per NOT slice)");
        _journey.Groups.SelectMany(g => g.InternalPaths).ShouldAllBe(p => p.StartPin != null && p.EndPin != null,
            "every frozen path is a routed pin-to-pin connection — no pin-less imported outline rings");
    }

    [Fact]
    public void Step2_DesignValidation_ReportsZeroErrorsOnTheLoadedDesign()
    {
        var issues = new DesignValidator().Validate(
            _journey.Canvas.Connections.Select(c => c.Connection).ToList(),
            _journey.Groups);
        issues.ShouldBeEmpty(
            "DRC-lite must find the eight side-by-side slices clear of each other — moving one " +
            "slice onto its neighbour turns this step red (mutation check)");
    }

    [Fact]
    public void Step3_LogicBuild_AllOperandPairs_YieldTheBitwiseAndAndNot()
    {
        for (var a = 0; a < 16; a++)
        for (var b = 0; b < 16; b++)
        {
            var result = _journey.Network.Evaluate(_journey.LogicUnit.InputBits(a, b));
            var expectedAnd = a & b;
            var expectedNot = ~a & 0xF;
            for (var bit = 0; bit < 4; bit++)
            {
                result[$"Y{bit}"].ShouldBe(((expectedAnd >> bit) & 1) == 1,
                    $"Y{bit} of {a} AND {b} = {expectedAnd} — all 256 (A, B) pairs give Y = A & B");
                result[$"N{bit}"].ShouldBe(((expectedNot >> bit) & 1) == 1,
                    $"N{bit} of NOT {a} = {expectedNot} — all 16 A give N = ~A & 0xF");
            }
        }
    }

    [Fact]
    public void Step4_NazcaExport_EmitsOneWaveguideChainPerRoutedPath_WithoutSkipsOrWarnings()
    {
        _journey.NazcaScript.ShouldNotBeNullOrEmpty(
            "the real export path must produce a nazca script for the loaded logic unit");
        _journey.NazcaScript.ShouldContain("nd.export_gds(topcells=[design]",
            Case.Sensitive, "the exported GDS carries the design as its top cell");
        _journey.SkippedConnections.ShouldBeEmpty(
            "nothing routed may be left out of the export — a skipped frozen path would silently " +
            "blank out one slice's internal wiring");
        _journey.ExportWarnings.ShouldBeEmpty("the export of the shipped example must not warn");

        var expectedLines = _journey.Groups.Sum(ExpectedWaveguideLines);
        var body = DesignBody(_journey.NazcaScript);
        var actualLines = body.Count(line =>
            line.Contains("nd.strt(") || line.Contains("nd.bend(") || line.Contains(".sbend_p2p("));
        actualLines.ShouldBe(expectedLines,
            "one waveguide chain per routed path: the canvas routes no live wires, so every " +
            "emitted chain belongs to one of the 52 frozen in-slice paths — segment for segment, " +
            "none dropped, none duplicated");
        Console.WriteLine($"[scale] logic unit 4-bit nazca script ({ExpectedSliceCount} slices, "
            + $"{ExpectedFrozenPathCount} frozen paths): {_journey.NazcaScript.Length / 1024.0:F0} KiB, "
            + $"generated in {_journey.ScriptGenerationElapsed.TotalMilliseconds:F0} ms");
    }

    [Trait("Category", "Slow")]
    [SkippableFact]
    public async Task Step5_GdsExportAndFoundryDeck_EmptyViolationBaselineHolds()
    {
        var python = await GdsUserDesignFixture.FindNazcaPythonAsync();
        Skip.If(python == null, "No Python with nazca available — the GDS export needs the real engine.");

        var exportDir = Path.Combine(_journey.WorkDirectory, "export");
        Directory.CreateDirectory(exportDir);
        var scriptPath = Path.Combine(exportDir, "logic_unit_4bit_manufacturing.py");
        await File.WriteAllTextAsync(scriptPath, _journey.NazcaScript);

        var watch = Stopwatch.StartNew();
        var export = await SiepicRealGeometryExportTests.RunPythonAsync(python, exportDir, scriptPath);
        watch.Stop();
        export.ExitCode.ShouldBe(0, $"the nazca export of the logic unit must succeed:\n{export.StdOut}\n{export.StdErr}");

        var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
        File.Exists(gdsPath).ShouldBeTrue($"the export script must write {gdsPath}:\n{export.StdOut}");
        Console.WriteLine($"[scale] logic unit 4-bit GDS export ({ExpectedSliceCount} slices): "
            + $"{watch.Elapsed.TotalSeconds:F1} s wall clock, GDS {new FileInfo(gdsPath).Length / 1024.0:F0} KiB");

        // Step 6 — GDS sanity check with the app's own GdsReader.
        GdsLibrary library;
        await using (var stream = File.OpenRead(gdsPath))
            library = await new GdsReader().ReadAsync(stream);
        library.TopCellCandidates.ShouldContain("ConnectAPIC_Design");
        var designCell = library.Cells["ConnectAPIC_Design"];
        designCell.Elements.OfType<GdsReference>().Count().ShouldBe(ExpectedLeafComponentCount,
            "one cell reference per component: the 60 leaf gates of the eight slices are placed " +
            "as cell references");
        var polygons = designCell.Elements.OfType<GdsPolygon>().ToList();
        polygons.ShouldNotBeEmpty("the slices' frozen in-group wiring flattens into real top-cell geometry");
        var footprints = _journey.Groups.Select(FootprintOf).ToList();
        polygons.ShouldAllBe(polygon => footprints.Any(f => Contains(f, polygon)),
            "no waveguide polygon outside component footprints and routed paths: this design routes " +
            "nothing on the canvas, so every polygon is in-slice wiring and must lie inside one " +
            "slice's footprint — an escaped polygon is exactly the overlap the Cornerstone deck " +
            "would miss on layers it does not inspect");

        var klayout = await ExternalToolProbes.FindKlayoutAsync();
        Skip.If(klayout == null, "No KLayout on PATH/$KLAYOUT — the foundry-deck proof needs the real engine.");

        var reportPath = Path.Combine(exportDir, "logic_unit_4bit.lyrdb");
        var (exitCode, output, error) = await ExternalToolProbes.RunToolAsync(
            python, CornerstoneDrcPaths.RunnerScript, gdsPath,
            "--klayout", klayout, "--report", reportPath);

        exitCode.ShouldBe(0,
            $"the vendored foundry deck must complete.\nstdout:\n{output}\nstderr:\n{error}");
        output.ShouldContain("PASSED: 0 DRC violations.",
            Case.Sensitive,
            "the empty violation set is the pinned baseline at the logic unit's scale too (the export " +
            "targets none of the deck's layers, same as the full adder's and 4-bit adder's journeys); " +
            "a new violation class must fail this journey here — do not relax the deck");
    }

    private List<Component> LeafComponents() =>
        _journey.Groups.SelectMany(g => g.GetAllComponentsRecursive())
            .Where(c => !c.IsAnalysisTool)
            .ToList();

    /// <summary>
    /// The waveguide lines one group's frozen paths emit, mirroring the exporter's rules
    /// (<see cref="SimpleNazcaExporter"/>): a skipped path emits nothing, a routeless path
    /// falls back to one pin-to-pin line, a single straight between pins emits one line,
    /// anything else one line per segment. Nested groups recurse like the exporter does.
    /// </summary>
    private static int ExpectedWaveguideLines(ComponentGroup group)
    {
        var lines = group.InternalPaths.Sum(WaveguideLinesOf);
        foreach (var nested in group.ChildComponents.OfType<ComponentGroup>())
            lines += ExpectedWaveguideLines(nested);
        return lines;
    }

    private static int WaveguideLinesOf(FrozenWaveguidePath path)
    {
        if (!path.Path.IsExportable())
            return 0;
        var segments = path.Path?.Segments;
        if (segments == null || segments.Count == 0)
            return 1;
        if (segments.Count == 1 && segments[0] is StraightSegment && path.StartPin != null && path.EndPin != null)
            return 1;
        return segments.Count;
    }

    /// <summary>The lines of the generated script's <c>create_design()</c> body.</summary>
    private static IReadOnlyList<string> DesignBody(string script)
    {
        var lines = script.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(lines, l => l.StartsWith("def create_design():", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, "the export script must define create_design()");
        var end = Array.FindIndex(lines, start, l => l == "    return design");
        end.ShouldBeGreaterThan(start, "create_design() must return the design cell");
        return lines[(start + 1)..end];
    }

    /// <summary>
    /// A slice's axis-aligned footprint in GDS coordinates: app space is Y-down, GDS/Nazca
    /// Y-up, so the app rect [X, X+W] × [Y, Y+H] maps to [X, X+W] × [−(Y+H), −Y] — valid
    /// because Step 1 pins every slice unrotated.
    /// </summary>
    private static GdsBoundingBox FootprintOf(ComponentGroup group) =>
        new(group.PhysicalX, -(group.PhysicalY + group.HeightMicrometers),
            group.PhysicalX + group.WidthMicrometers, -group.PhysicalY);

    private static bool Contains(GdsBoundingBox footprint, GdsPolygon polygon) =>
        polygon.Points.All(p =>
            p.X >= footprint.MinX - FootprintToleranceMicrometers
            && p.X <= footprint.MaxX + FootprintToleranceMicrometers
            && p.Y >= footprint.MinY - FootprintToleranceMicrometers
            && p.Y <= footprint.MaxY + FootprintToleranceMicrometers);

    /// <summary>
    /// Shared journey fixture: performs the journey's stateful steps once (load → assemble →
    /// export-script with the skip/warning collectors the app's export report uses) so each
    /// fact asserts one step of the same continuous journey and the DRC step drives exactly
    /// what the export produced.
    /// </summary>
    public class ManufacturingJourneyFixture : IAsyncLifetime
    {
        /// <summary>The pinned logic-unit fixture carrying canvas, groups, network and input mapping.</summary>
        public LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture LogicUnit { get; } = new();

        /// <summary>Temp working directory for the GDS export and the DRC report.</summary>
        public string WorkDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "logic-unit-4bit-manufacturing-" + Guid.NewGuid().ToString("N"));

        /// <summary>The canvas the shipped example loaded onto.</summary>
        public DesignCanvasViewModel Canvas => LogicUnit.Canvas;

        /// <summary>The loaded top-level gate slices, in file order.</summary>
        public List<ComponentGroup> Groups => LogicUnit.Groups;

        /// <summary>The logic network assembled from the loaded design.</summary>
        public LogicNetworkEvaluator Network => LogicUnit.Network;

        /// <summary>The nazca export script of the loaded design.</summary>
        public string NazcaScript { get; private set; } = null!;

        /// <summary>Connections/frozen paths the export left out (must stay empty).</summary>
        public List<string> SkippedConnections { get; } = new();

        /// <summary>Warnings the export raised (must stay empty).</summary>
        public List<string> ExportWarnings { get; } = new();

        /// <summary>Wall clock of the export-script generation on the loaded design.</summary>
        public TimeSpan ScriptGenerationElapsed { get; private set; }

        /// <summary>Loads the shipped example, assembles its logic network, generates its export script.</summary>
        public async Task InitializeAsync()
        {
            await LogicUnit.InitializeAsync();
            var watch = Stopwatch.StartNew();
            NazcaScript = new SimpleNazcaExporter().Export(
                Canvas, skippedConnections: SkippedConnections, exportWarnings: ExportWarnings);
            watch.Stop();
            ScriptGenerationElapsed = watch.Elapsed;
        }

        /// <summary>Removes the temp working directory.</summary>
        public Task DisposeAsync()
        {
            try
            {
                if (Directory.Exists(WorkDirectory)) Directory.Delete(WorkDirectory, recursive: true);
            }
            catch
            {
                // temp cleanup is best effort
            }
            return Task.CompletedTask;
        }
    }
}
