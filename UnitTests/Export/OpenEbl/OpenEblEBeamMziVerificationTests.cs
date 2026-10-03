using CAP.Avalonia.Services;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// openEBL readiness gate for gap #4 (#1336): exports the shipped
/// <c>EBeam Mach-Zehnder Interferometer.lun</c> through the real nazca path and runs
/// a vendored headless port of openEBL's <c>run_verification.py</c>
/// (SiEPIC-Tools <c>layout_check</c>) against the produced GDS — the same top-cell
/// pick, the same EBeam technology attach, the same call, plus a per-category census
/// parsed from the .lyrdb report.
/// <para>
/// The pinned state is the gap #4 fix: every routed connection exports as its own
/// SiEPIC-conformant <c>Waveguide_&lt;n&gt;</c> cell (Si 1/0 polygons + Waveguide
/// (1/99) guide-outline polygon + DevRec (68/0) + PinRec (1/10) pins that are exact
/// reversed copies of the foundry pins, <see cref="SiepicWaveguideCellWriter"/>), so
/// the run completes with ZERO layout errors: no "Shapes outside component" (the
/// routes no longer flatten into the top cell), no "Disconnected pin" (every optical
/// pin is netted to a waveguide pin — identical centre, 180°-opposite direction, the
/// two conditions <c>identify_nets</c> requires), and no new category (no
/// "Overlapping component", no "Waveguide: Path" — the guide is stored as the path's
/// outline polygon exactly like the SiEPIC Waveguide PCell).
/// </para>
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (installed
/// on the CI runner by the "Install Nazca + KLayout + SiEPIC PDK" step); skips
/// cleanly elsewhere. Setting LUNIMA_OPENEBL_ARTIFACT_DIR copies the script, GDS,
/// runner, .lyrdb and raw output there for inspection.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblEBeamMziVerificationTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";

    [SkippableFact]
    public async Task EBeamMziExample_OpenEblVerification_CompletesAndPinsRemainingGaps()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        // ── 1. Load the shipped example through the real load path ──
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(examplePath);
        canvas.Components.Count.ShouldBe(4);
        canvas.Connections.Count.ShouldBe(4);

        // ── 2. Export with the app's own exporter; nothing may be skipped ──
        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas, skippedConnections: skippedConnections, exportWarnings: exportWarnings);
        skippedConnections.ShouldBeEmpty("every MZI route must be real, exportable geometry");
        exportWarnings.ShouldBeEmpty();

        // ── 3. Run the export under real nazca (klayout upgrade + DevRec pass included) ──
        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-verify-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var scriptPath = Path.Combine(dir, "ebeam_mzi_verify.py");
            await File.WriteAllTextAsync(scriptPath, script);
            var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, scriptPath);
            run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
            var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
            File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");

            // ── 4. Run the vendored port of openEBL's run_verification.py ──
            var runnerPath = Path.Combine(dir, "openebl_verification.py");
            await File.WriteAllTextAsync(runnerPath, VerificationRunnerScript);
            var check = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, runnerPath, gdsPath);
            check.ExitCode.ShouldBe(0, $"verification port crashed:\n{check.StdOut}\n{check.StdErr}");
            var output = check.StdOut;
            var errorCount = int.Parse(output.TrimEnd().Split('\n').Last().Trim());

            CopyArtifacts(dir, scriptPath, gdsPath, runnerPath, output);

            // ── 5. Pin the gap #4 result: verification passes with zero errors ──
            output.ShouldContain("Top cell: ConnectAPIC_Design");
            output.ShouldNotContain("Unknown error occurred");
            // Gap #4 closed (#1336): every routed connection is a SiEPIC-conformant
            // Waveguide_<n> cell, so no route polygon sits outside a component and
            // every optical pin is netted to a waveguide pin.
            output.ShouldContain("category Shapes outside component: 0");
            output.ShouldContain("category Disconnected pin: 0");
            errorCount.ShouldBe(0,
                $"the EBeam MZI verification must pass cleanly after gap #4:\n{output}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }

    /// <summary>
    /// When LUNIMA_OPENEBL_ARTIFACT_DIR is set, copies the export script, the GDS, the
    /// verification runner, its raw output and the .lyrdb report there.
    /// </summary>
    private static void CopyArtifacts(
        string workDir, string scriptPath, string gdsPath, string runnerPath, string runnerOutput)
    {
        var artifactDir = Environment.GetEnvironmentVariable("LUNIMA_OPENEBL_ARTIFACT_DIR");
        if (string.IsNullOrEmpty(artifactDir))
            return;
        Directory.CreateDirectory(artifactDir);
        File.Copy(scriptPath, Path.Combine(artifactDir, Path.GetFileName(scriptPath)), overwrite: true);
        File.Copy(gdsPath, Path.Combine(artifactDir, Path.GetFileName(gdsPath)), overwrite: true);
        File.Copy(runnerPath, Path.Combine(artifactDir, Path.GetFileName(runnerPath)), overwrite: true);
        var lyrdb = Path.ChangeExtension(gdsPath, ".lyrdb");
        if (File.Exists(lyrdb))
            File.Copy(lyrdb, Path.Combine(artifactDir, Path.GetFileName(lyrdb)), overwrite: true);
        File.WriteAllText(Path.Combine(artifactDir, "openebl_verification_output.txt"), runnerOutput);
    }

    /// <summary>
    /// Vendored headless port of openEBL-2026-10 <c>run_verification.py</c>
    /// (https://github.com/SiEPIC/openEBL-2026-10/blob/main/run_verification.py):
    /// same top-cell pick (single top, else the one with most subcells), same EBeam
    /// technology attach (<c>get_technology_by_name('EBeam')</c> — headless has no GUI
    /// technology), same <c>SiEPIC.verification.layout_check</c> call writing the .lyrdb
    /// next to the GDS, same last-stdout-line error count and the same
    /// "Unknown error occurred" catch-all (the #1321 blocker: <c>find_components</c>
    /// raising when no DevRec exists). Adds one thing: a per-category error census
    /// parsed from the .lyrdb via klayout.rdb, so the pinned result names the failing
    /// rules instead of just counting them.
    /// </summary>
    internal const string VerificationRunnerScript = """
        import os
        import sys

        gds_file = sys.argv[1]
        print('Running SiEPIC-Tools automated verification (Lunima headless port) for file %s' % gds_file)

        import klayout.db as pya
        import SiEPIC  # noqa: F401
        from SiEPIC.verification import layout_check
        from SiEPIC.utils import get_technology_by_name
        import siepic_ebeam_pdk  # noqa: F401

        num_errors = 1
        try:
            layout = pya.Layout()
            layout.read(gds_file)
        except Exception:
            print('Error loading layout')

        try:
            top_cells = layout.top_cells()
            top_cell = layout.top_cell() if len(top_cells) == 1 else max(
                top_cells, key=lambda c: sum(1 for _ in c.each_child_cell()), default=None)
            if not top_cell:
                print('No top cell in the layout')
            else:
                print('Top cell: %s' % top_cell.name)

            # The technology is empty in a headless read; layout_check needs it (the
            # genuine script attaches it the same way — GUI get_technology() is N/A here).
            layout.TECHNOLOGY = get_technology_by_name('EBeam')
            file_lyrdb = os.path.splitext(gds_file)[0] + '.lyrdb'
            num_errors = layout_check(cell=top_cell, verbose=False, file_rdb=file_lyrdb)
        except Exception as exc:
            print('Unknown error occurred')
            print('# underlying %s: %s' % (type(exc).__name__, exc))
            num_errors = 1

        # Per-category census from the report database (klayout.rdb reads the lyrdb).
        # The two gap-#4 categories are ALWAYS printed (0 when absent or when the
        # run was clean enough to skip writing items), so the pinned assertion
        # names the fixed rules instead of relying on a missing line.
        counts = {}
        if os.path.exists(file_lyrdb):
            try:
                import klayout.rdb as rdb
                db = rdb.ReportDatabase()
                db.load(file_lyrdb)
                for item in db.each_item():
                    cat = db.category_by_id(item.category_id()).name()
                    counts[cat] = counts.get(cat, 0) + 1
            except Exception as exc:
                print('# category census failed: %s' % exc)
        for name in sorted(set(counts) | {'Disconnected pin', 'Shapes outside component'}):
            print('category %s: %d' % (name, counts.get(name, 0)))

        print(num_errors)
        """;
}
