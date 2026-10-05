using System.Collections.ObjectModel;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using UnitTests.Components;
using UnitTests.Export;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1277 (rung 6 → 7, real GDS): the tape-out truth is the GDS file, not the
/// Nazca script text that <see cref="TwoChipletsGapExportJourneyTests"/> (#1268) proves
/// the fix on. This journey runs the same scenario — open the shipped
/// <c>Two Chiplets - Edge-Coupler Link</c> example, drag the receiver chiplet 5 µm to
/// the right — but exports through the real Nazca path with a real nazca Python into a
/// real GDS file, extracts the geometry with <c>scripts/extract_gds_coords.py</c>
/// (gdspy, or the gdstk fallback from #1088) and asserts on the flattened top-cell
/// polygons: no polygon on any layer reaches into the free-space gap between the two
/// facet planes, and each die's own waveguides are present on its own side. A second
/// scenario keeps the chiplets aligned (gap 0) and asserts no polygon spans both dies.
/// </summary>
[Trait("Category", "Slow")]
public class TwoChipletsGdsGapJourneyTests : IDisposable
{
    private const double GapMicrometers = 5.0;
    private const double Tolerance = 0.01;
    private const string TopCellName = "ConnectAPIC_Design";

    private const string ReaderProbe =
        "import nazca\n" +
        "try:\n" +
        "    import gdspy\n" +
        "except ImportError:\n" +
        "    import gdstk\n";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-gds-gap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [SkippableFact]
    public async Task ChipletsMovedApart_RealGds_HasNoPolygonInTheFreeSpaceGap()
    {
        var python = await FindNazcaPythonWithGdsReaderAsync();
        Skip.If(python == null, "No Python with nazca + gdspy/gdstk available (expected on CI).");

        // 1. Open the shipped example through the real Home example path.
        var canvas = await OpenExampleCanvasAsync();
        var link = canvas.ConnectionManager.Connections.ShouldHaveSingleItem(
            "the example's only connection is the cross-chiplet facet link");

        // 2. Drag chiplet B 5 µm to the right via the canvas move command (as a drag ends).
        var chipletB = Chiplet(canvas, ChipletEdgeCouplerJourneyDesign.ChipletBName);
        var chipletBViewModel = canvas.Components.Single(vm => ReferenceEquals(vm.Component, chipletB));
        new CommandManager().ExecuteCommand(new MoveComponentCommand(
            canvas, chipletBViewModel,
            chipletBViewModel.X, chipletBViewModel.Y,
            chipletBViewModel.X + GapMicrometers, chipletBViewModel.Y));

        // 3+4. Export through the real Nazca path to a real GDS file and extract the geometry.
        var gdsPath = await ExportToRealGdsAsync(python, canvas, "moved");
        var polygons = await FlattenedTopCellPolygonsAsync(python, gdsPath);
        polygons.ShouldNotBeEmpty("the exported GDS must contain the design's geometry");

        // 5. No polygon reaches into the open x-interval between the two facet planes.
        var facetA = link.StartPin!.GetAbsoluteNazcaPosition();
        var facetB = link.EndPin!.GetAbsoluteNazcaPosition();
        double gapLow = Math.Min(facetA.x, facetB.x) + Tolerance;
        double gapHigh = Math.Max(facetA.x, facetB.x) - Tolerance;
        gapHigh.ShouldBeGreaterThan(gapLow, "the +5 µm move must open a real gap between the facets");

        foreach (var polygon in polygons)
        {
            (polygon.MinX < gapHigh && polygon.MaxX > gapLow).ShouldBeFalse(
                $"no GDS geometry may enter the free-space gap {gapLow:F3}..{gapHigh:F3} µm " +
                $"between the dies, but a polygon on layer ({polygon.Layer},{polygon.DataType}) " +
                $"spans x {polygon.MinX:F3}..{polygon.MaxX:F3}");
        }

        // Each die's own waveguides are present on its own side of the gap.
        polygons.ShouldContain(p => p.MaxX <= gapLow + Tolerance,
            "the transmitter die's own waveguides must still be exported on its side");
        polygons.ShouldContain(p => p.MinX >= gapHigh - Tolerance,
            "the receiver die's own waveguides must still be exported on its side");
    }

    [SkippableFact]
    public async Task ChipletsAligned_RealGds_HasNoPolygonSpanningBothDies()
    {
        var python = await FindNazcaPythonWithGdsReaderAsync();
        Skip.If(python == null, "No Python with nazca + gdspy/gdstk available (expected on CI).");

        // Gap 0: the shipped example abuts the two edge-coupler facets exactly.
        var canvas = await OpenExampleCanvasAsync();
        var link = canvas.ConnectionManager.Connections.ShouldHaveSingleItem();

        var gdsPath = await ExportToRealGdsAsync(python, canvas, "aligned");
        var polygons = await FlattenedTopCellPolygonsAsync(python, gdsPath);
        polygons.ShouldNotBeEmpty();

        var facetA = link.StartPin!.GetAbsoluteNazcaPosition();
        var facetB = link.EndPin!.GetAbsoluteNazcaPosition();
        Math.Abs(facetA.x - facetB.x).ShouldBeLessThan(Tolerance,
            "the shipped example abuts the two facets (gap 0)");
        double facetX = (facetA.x + facetB.x) / 2.0;

        // No polygon crosses the shared facet plane — the dies only touch, never overlap.
        foreach (var polygon in polygons)
        {
            (polygon.MinX < facetX - Tolerance && polygon.MaxX > facetX + Tolerance).ShouldBeFalse(
                $"no GDS geometry may span both dies across the facet plane x={facetX:F3} µm, " +
                $"but a polygon on layer ({polygon.Layer},{polygon.DataType}) " +
                $"spans x {polygon.MinX:F3}..{polygon.MaxX:F3}");
        }

        polygons.ShouldContain(p => p.MaxX <= facetX + Tolerance,
            "the transmitter die's own waveguides must still be exported on its side");
        polygons.ShouldContain(p => p.MinX >= facetX - Tolerance,
            "the receiver die's own waveguides must still be exported on its side");
    }

    // ── Real-GDS export and extraction ─────────────────────────────────────────

    /// <summary>Exports the canvas through <see cref="SimpleNazcaExporter"/> and runs the script with real nazca.</summary>
    private async Task<string> ExportToRealGdsAsync(string python, DesignCanvasViewModel canvas, string stem)
    {
        var script = new SimpleNazcaExporter().Export(canvas);
        script.ShouldContain("# Cross-chiplet facet link");

        var dir = Path.Combine(_root, stem);
        Directory.CreateDirectory(dir);
        var scriptPath = Path.Combine(dir, stem + ".py");
        await File.WriteAllTextAsync(scriptPath, script);

        var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, scriptPath);
        run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
        var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
        File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");
        return gdsPath;
    }

    /// <summary>
    /// Runs <c>scripts/extract_gds_coords.py</c> on the GDS file and flattens the design
    /// top cell: every polygon of the top cell and of the cells it (transitively)
    /// references, transformed into absolute top-cell coordinates.
    /// </summary>
    private static async Task<List<FlatPolygon>> FlattenedTopCellPolygonsAsync(string python, string gdsPath)
    {
        var scriptPath = GdsCoordinateExtractor.FindScriptPath();
        scriptPath.ShouldNotBeNull("scripts/extract_gds_coords.py must be locatable from the test run");
        var dir = Path.GetDirectoryName(gdsPath)!;
        var jsonPath = Path.Combine(dir, "coords.json");

        var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, scriptPath, gdsPath, jsonPath);
        run.ExitCode.ShouldBe(0, $"GDS coordinate extraction failed:\n{run.StdOut}\n{run.StdErr}");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var cells = doc.RootElement.GetProperty("cells").EnumerateArray()
            .ToDictionary(ParseCellName, ParseCell);
        cells.ShouldContainKey(TopCellName);

        var polygons = new List<FlatPolygon>();
        FlattenCell(TopCellName, AffineTransform.Identity, cells, polygons);
        return polygons;
    }

    private static string ParseCellName(JsonElement cell) => cell.GetProperty("name").GetString()!;

    private static CellGeometry ParseCell(JsonElement cell)
    {
        var polygons = cell.GetProperty("polygons").EnumerateArray()
            .Select(p => new CellPolygon(
                p.GetProperty("layer").GetInt32(),
                p.GetProperty("datatype").GetInt32(),
                ParsePoints(p.GetProperty("vertices"))))
            .Concat(cell.GetProperty("paths").EnumerateArray()
                .Select(p => new CellPolygon(
                    p.GetProperty("layer").GetInt32(),
                    p.GetProperty("datatype").GetInt32(),
                    ParsePoints(p.GetProperty("points")))))
            .ToList();
        var refs = cell.GetProperty("refs").EnumerateArray()
            .Select(r => new CellReference(
                r.GetProperty("ref_cell").GetString()!,
                r.GetProperty("origin")[0].GetDouble(),
                r.GetProperty("origin")[1].GetDouble(),
                r.GetProperty("rotation").GetDouble(),
                r.GetProperty("magnification").GetDouble(),
                r.GetProperty("x_reflection").GetBoolean()))
            .ToList();
        return new CellGeometry(polygons, refs);
    }

    private static List<(double X, double Y)> ParsePoints(JsonElement points) =>
        points.EnumerateArray()
            .Select(v => (v[0].GetDouble(), v[1].GetDouble()))
            .ToList();

    private static void FlattenCell(
        string cellName,
        AffineTransform transform,
        Dictionary<string, CellGeometry> cells,
        List<FlatPolygon> output)
    {
        var cell = cells[cellName];
        foreach (var polygon in cell.Polygons)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            foreach (var (x, y) in polygon.Vertices)
            {
                var (ax, _) = transform.Apply(x, y);
                minX = Math.Min(minX, ax);
                maxX = Math.Max(maxX, ax);
            }
            output.Add(new FlatPolygon(polygon.Layer, polygon.DataType, minX, maxX));
        }

        foreach (var reference in cell.References)
        {
            FlattenCell(
                reference.CellName,
                transform.Then(AffineTransform.ForReference(
                    reference.OriginX, reference.OriginY,
                    reference.RotationDegrees, reference.Magnification, reference.XReflection)),
                cells,
                output);
        }
    }

    // ── GDS geometry model (mirrors the extract_gds_coords.py JSON schema) ─────

    private sealed record CellPolygon(int Layer, int DataType, IReadOnlyList<(double X, double Y)> Vertices);

    private sealed record CellReference(
        string CellName, double OriginX, double OriginY,
        double RotationDegrees, double Magnification, bool XReflection);

    private sealed record CellGeometry(List<CellPolygon> Polygons, List<CellReference> References);

    /// <summary>One polygon flattened into absolute top-cell coordinates, x-extent only.</summary>
    private sealed record FlatPolygon(int Layer, int DataType, double MinX, double MaxX);

    /// <summary>
    /// 2D affine transform (x' = A·x + B·y + Tx; y' = C·x + D·y + Ty) for flattening
    /// GDS cell references. A reference first mirrors y when x_reflection is set
    /// (gdstk/gdspy convention: reflection across the horizontal axis), then scales,
    /// rotates counterclockwise by its angle in degrees and translates to its origin.
    /// </summary>
    private readonly record struct AffineTransform(
        double A, double B, double C, double D, double Tx, double Ty)
    {
        public static readonly AffineTransform Identity = new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) =>
            (A * x + B * y + Tx, C * x + D * y + Ty);

        /// <summary>The transform that first applies <paramref name="inner"/>, then this one.</summary>
        public AffineTransform Then(AffineTransform inner) => new(
            A * inner.A + B * inner.C,
            A * inner.B + B * inner.D,
            C * inner.A + D * inner.C,
            C * inner.B + D * inner.D,
            A * inner.Tx + B * inner.Ty + Tx,
            C * inner.Tx + D * inner.Ty + Ty);

        public static AffineTransform ForReference(
            double originX, double originY, double rotationDegrees, double magnification, bool xReflection)
        {
            double radians = rotationDegrees * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double scaleY = xReflection ? -magnification : magnification;
            return new AffineTransform(
                magnification * cos, -scaleY * sin,
                magnification * sin, scaleY * cos,
                originX, originY);
        }
    }

    // ── Python discovery (gating of the GDS round-trip tests + a GDS reader) ───

    /// <summary>
    /// Locates a Python that can both run the export (nazca importable) and read the
    /// produced GDS (gdspy or gdstk — the readers <c>scripts/extract_gds_coords.py</c>
    /// supports). Same search order as <c>GdsUserDesignFixture.FindNazcaPythonAsync</c>:
    /// Lunima managed envs first, then python/python3 on PATH.
    /// </summary>
    private static async Task<string?> FindNazcaPythonWithGdsReaderAsync()
    {
        var envs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lunima", "envs");
        if (Directory.Exists(envs))
        {
            foreach (var root in Directory.GetDirectories(envs))
            {
                foreach (var rel in new[] { Path.Combine("Scripts", "python.exe"), Path.Combine("bin", "python") })
                {
                    var py = Path.Combine(root, rel);
                    if (File.Exists(py) && await ProbeAsync(py))
                        return py;
                }
            }
        }

        foreach (var candidate in new[] { "python", "python3" })
        {
            if (await ProbeAsync(candidate))
                return candidate;
        }
        return null;
    }

    private static async Task<bool> ProbeAsync(string python)
    {
        try
        {
            var probe = await SiepicRealGeometryExportTests.RunPythonAsync(
                python, Path.GetTempPath(), "-c", ReaderProbe);
            return probe.ExitCode == 0;
        }
        catch
        {
            return false;   // not on PATH at all
        }
    }

    // ── Example loading (recipe of TwoChipletsGapExportJourneyTests) ───────────

    private static async Task<DesignCanvasViewModel> OpenExampleCanvasAsync()
    {
        var canvas = new DesignCanvasViewModel();
        var fileOps = CreateFileOperations(canvas);
        string? migrationWarning = null;
        fileOps.OnProcessMigrationWarning = w => migrationWarning = w;
        (await fileOps.OpenDesignAsCopyAsync(ExamplePath())).ShouldBeTrue("the shipped example must open");
        await fileOps.PostLoadRouting;
        migrationWarning.ShouldBeNull("the shipped bindings describe the design completely");
        return canvas;
    }

    private static string ExamplePath() => Path.Combine(
        ExampleDesignFilesTests.ExamplesDirectory(), TwoChipletsExampleAuthoringTests.ExampleFileName);

    private static ComponentGroup Chiplet(DesignCanvasViewModel canvas, string name) =>
        canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>().Single(g => g.GroupName == name);

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
}
