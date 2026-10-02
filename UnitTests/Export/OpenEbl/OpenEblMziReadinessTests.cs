using CAP.Avalonia.Services;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// openEBL (SiEPICfab EBeam) readiness spike (#1299): exports the shipped
/// Mach-Zehnder Interferometer example through the real nazca path and runs a
/// headless port of openEBL's automated submission checks
/// (<c>run_submission_checks.py</c> in the openEBL-2026-10 repository:
/// single top cell, floorplan bounding box 605 x 410 µm on layers (1,0)+(4,0),
/// black-box cell census, PDK layer conformity) against the produced GDS.
/// <para>
/// The assertions pin the CURRENT gap set on purpose: the MZI example is a
/// Demo-PDK teaching design, so its export uses demofab geometry and nazca's
/// default interconnect layer — none of which exist in the SiEPIC EBeam PDK
/// layer map, and the export carries neither a (99,0) floorplan nor (10,0)
/// <c>opt_in</c> measurement labels. When an EBeam layer map / floorplan /
/// label convention lands, this test is updated to expect zero errors.
/// </para>
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk (installed on
/// the CI runner by the "Install Nazca + KLayout + SiEPIC PDK" step); skips
/// cleanly elsewhere. Setting LUNIMA_OPENEBL_ARTIFACT_DIR copies the script,
/// GDS and checker output there for inspection (used by the readiness report).
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblMziReadinessTests
{
    private const string ExampleFileName = "Mach-Zehnder Interferometer.lun";

    [SkippableFact]
    public async Task MziExample_OpenEblSubmissionChecks_PinCurrentGaps()
    {
        var python = await FindOpenEblCheckPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk (expected on CI).");

        // ── 1. Load the shipped example through the real load path ──
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(examplePath);
        canvas.Components.Count.ShouldBe(7);
        canvas.Connections.Count.ShouldBe(7);

        // ── 2. Export with the app's own exporter; nothing may be skipped ──
        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas, skippedConnections: skippedConnections, exportWarnings: exportWarnings);
        skippedConnections.ShouldBeEmpty("every MZI route must be real, exportable geometry");
        exportWarnings.ShouldBeEmpty();

        // ── 3. Run the export under real nazca ──
        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var scriptPath = Path.Combine(dir, "mzi.py");
            await File.WriteAllTextAsync(scriptPath, script);
            var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, scriptPath);
            run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
            var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
            File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");

            // ── 4. Run the headless port of openEBL's submission checks ──
            var checkerPath = Path.Combine(dir, "openebl_submission_check.py");
            await File.WriteAllTextAsync(checkerPath, SubmissionCheckerScript);
            var check = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, checkerPath, gdsPath);
            check.ExitCode.ShouldBe(0, $"submission-check port crashed:\n{check.StdOut}\n{check.StdErr}");
            var output = check.StdOut;
            var errorCount = int.Parse(output.TrimEnd().Split('\n').Last().Trim());

            CopyArtifacts(dir, scriptPath, gdsPath, checkerPath, output);

            // ── 5. Pin the current readiness state ──
            output.ShouldContain("Top cell: ConnectAPIC_Design");
            output.ShouldNotContain("does not have 1 top cell");
            // Today's demofab export is ~620 µm wide on the checked layers — over
            // the 605 µm openEBL die width even before any re-layout for EBeam.
            output.ShouldContain("exceeds allowed size 605.000 um x 410.000 um");
            output.ShouldContain("Number of unreplaced BB cells: 0");

            // The gaps the readiness report lists, pinned one by one: the
            // representative non-PDK layers (nazca's default interconnect, the
            // demofab waveguide/pin layers, the parametric-straight bb_body frame),
            // the missing floorplan, the missing measurement labels, and the exact
            // current error count (15 layer errors + 1 floorplan-size error).
            output.ShouldContain("Error: the layer 1111/0 in the design is not defined in the PDK.");
            output.ShouldContain("Error: the layer 1/20 in the design is not defined in the PDK.");
            output.ShouldContain("Error: the layer 1003/0 in the design is not defined in the PDK.");
            output.ShouldContain("Error: the layer 501/1 in the design is not defined in the PDK.");
            output.ShouldContain("Floorplan (99/0) shapes: 0");
            output.ShouldContain("opt_in labels (10/0): 0");
            errorCount.ShouldBe(16,
                "the MZI export is NOT openEBL-ready today — this pin flips when the gap list is closed");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }

    /// <summary>
    /// When LUNIMA_OPENEBL_ARTIFACT_DIR is set, copies the export script, the GDS,
    /// the checker and its raw output there (the readiness report quotes them).
    /// </summary>
    internal static void CopyArtifacts(
        string workDir, string scriptPath, string gdsPath, string checkerPath, string checkerOutput)
    {
        var artifactDir = Environment.GetEnvironmentVariable("LUNIMA_OPENEBL_ARTIFACT_DIR");
        if (string.IsNullOrEmpty(artifactDir))
            return;
        Directory.CreateDirectory(artifactDir);
        File.Copy(scriptPath, Path.Combine(artifactDir, Path.GetFileName(scriptPath)), overwrite: true);
        File.Copy(gdsPath, Path.Combine(artifactDir, Path.GetFileName(gdsPath)), overwrite: true);
        File.Copy(checkerPath, Path.Combine(artifactDir, Path.GetFileName(checkerPath)), overwrite: true);
        File.WriteAllText(Path.Combine(artifactDir, "openebl_check_output.txt"), checkerOutput);
    }

    /// <summary>
    /// Locates a Python with nazca + klayout + siepic_ebeam_pdk: first a Lunima
    /// managed env (%LOCALAPPDATA%/Lunima/envs/*), then python/python3 on PATH.
    /// Same probe order as <see cref="SiepicRealGeometryExportTests"/>, but without
    /// gdsfactory — the openEBL check port does not need it, and the CI runner
    /// installs exactly these three packages.
    /// </summary>
    internal static async Task<string?> FindOpenEblCheckPythonAsync()
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
                    if (File.Exists(py) && await Probe(py))
                        return py;
                }
            }
        }

        foreach (var candidate in new[] { "python", "python3" })
        {
            if (await Probe(candidate))
                return candidate;
        }
        return null;
    }

    private static async Task<bool> Probe(string python)
    {
        try
        {
            var probe = await SiepicRealGeometryExportTests.RunPythonAsync(
                python, Path.GetTempPath(), "-c", "import klayout.db, siepic_ebeam_pdk, nazca");
            return probe.ExitCode == 0;
        }
        catch
        {
            return false;   // not on PATH at all
        }
    }

    /// <summary>
    /// Headless port of the openEBL-2026-10 <c>run_submission_checks.py</c>
    /// (https://github.com/SiEPIC/openEBL-2026-10/blob/main/run_submission_checks.py),
    /// reduced to klayout.db + siepic_ebeam_pdk so it runs without SiEPIC-Tools:
    /// same single-top-cell rule, same 605000 x 410000 dbu bbox over (1,0)+(4,0),
    /// same black-box census (allow-list, then leftover 998/0 shapes counted from the
    /// top cell down after clearing the allow-listed cells — the genuine script's
    /// replace-with-empty-cell + hierarchy walk), same layer-vs-PDK check against the
    /// EBeam.lyp layer properties. Adds a functional-readiness census (floorplan
    /// shapes, opt_in labels) that openEBL's <c>run_verification.py</c>
    /// (SiEPIC layout_check) requires. Last stdout line is the error count, like the
    /// original. Internal so <see cref="OpenEblEBeamSubmissionCheckTests"/> runs the
    /// identical port against an EBeam-only design.
    /// </summary>
    internal const string SubmissionCheckerScript = """
        import os, sys
        import xml.etree.ElementTree as ET
        import klayout.db as pya
        import siepic_ebeam_pdk

        # Allow-listed black-box cells, verbatim from run_submission_checks.py.
        BB_CELLS = [
            'ebeam_gc_te1550', 'ebeam_gc_tm1550',
            'GC_TE_1550_8degOxide_BB', 'GC_TM_1550_8degOxide_BB',
            'ebeam_gc_te1310', 'ebeam_gc_te1310_8deg',
            'GC_TE_1310_8degOxide_BB', 'ebeam_GC_TM_1310_8degOxide',
            'GC_TM_1310_8degOxide_BB', 'GC_TM_1310_8degOxide_BB$1',
            'ebeam_splitter_swg_assist_te1310', 'ebeam_splitter_swg_assist_te1550',
            'ebeam_dream_splitter_1x2_te1550_BB',
        ]

        def pdk_layers():
            lyp = os.path.join(os.path.dirname(siepic_ebeam_pdk.__file__), 'EBeam.lyp')
            layers = set()
            for source in ET.parse(lyp).getroot().iter('source'):
                text = source.text
                if not text:
                    continue
                parts = text.split('@')[0].split('/')
                if len(parts) >= 2:
                    try:
                        layers.add((int(parts[0]), int(parts[1])))
                    except ValueError:
                        continue
            return layers

        def recursive_shapes(layout, top, layer, dt):
            li = layout.find_layer(pya.LayerInfo(layer, dt))
            if li is None:
                return []
            shapes = []
            it = pya.RecursiveShapeIterator(layout, top, li)
            while not it.at_end():
                shapes.append(it.shape())
                it.next()
            return shapes

        gds_file = sys.argv[1]
        print('Running openEBL submission checks (Lunima headless port) for file %s' % gds_file)
        num_errors = 0
        layout = pya.Layout()
        layout.read(gds_file)

        tops = layout.top_cells()
        if len(tops) != 1:
            print('Error: layout does not have 1 top cell. It has %s.' % len(tops))
            print(' - cells: %s' % [c.name for c in layout.each_cell()])
            num_errors += 1
            print(num_errors)
            sys.exit(0)
        top = tops[0]
        print('Top cell: %s' % top.name)

        # Floorplan extent: bbox of (1,0)+(4,0) must fit 605 x 410 um (dbu 0.001).
        region = pya.Region()
        for ld in [(1, 0), (4, 0)]:
            li = layout.find_layer(pya.LayerInfo(*ld))
            if li is not None:
                region += pya.Region(top.bbox_per_layer(li))
        region.merge()
        if region:
            w = region.bbox().width() * layout.dbu
            h = region.bbox().height() * layout.dbu
            if w > 605.0 or h > 410.0:
                print('Error: Bounding box of selected layers (%.3f um x %.3f um) exceeds allowed size 605.000 um x 410.000 um' % (w, h))
                num_errors += 1
            else:
                print('Bounding box of selected layers is %.3f um x %.3f um' % (w, h))
        else:
            print('No shapes found in the specified layers.')
            num_errors += 1

        # Black-box census: allowed BB cells, plus leftover 998/0 shapes elsewhere.
        # Mirrors the genuine script's semantics: allow-listed BB cells are swapped
        # for an EMPTY cell (here: cleared in place), then leftover 998/0 shapes are
        # counted walking the hierarchy from the top cell — shapes inside a swapped
        # BB cell's own subtree (the PDK's GC cells carry 998/0 TEXT subcells) become
        # unreachable and do not count, exactly like SiEPIC's replace_cell +
        # cells_containing_bb_layers.
        bb_found = sorted({c.name.split('$')[0] for c in layout.each_cell()
                           if c.name.split('$')[0] in BB_CELLS})
        print('Performing Black Box cell replacement check')
        for name in bb_found:
            print(' - black box cell: %s' % name)
        print(' - Number of black box cells to be replaced: %s' % len(bb_found))
        for c in layout.each_cell():
            if c.name.split('$')[0] in BB_CELLS:
                c.clear()
        unreplaced = []
        li998 = layout.find_layer(pya.LayerInfo(998, 0))
        if li998 is not None:
            seen = set()
            it = pya.RecursiveShapeIterator(layout, top, li998)
            while not it.at_end():
                seen.add(it.cell().name)
                it.next()
            unreplaced = sorted(seen)
        print(' - Number of unreplaced BB cells: %s' % len(unreplaced))
        if unreplaced:
            print('ERROR: unidentified black box cells: %s' % unreplaced)
        num_errors += len(unreplaced)

        # Every design layer must be defined in the EBeam PDK.
        pdk = pdk_layers()
        for l in layout.layer_infos():
            if (l.layer, l.datatype) not in pdk:
                print('Error: the layer %s/%s in the design is not defined in the PDK.' % (l.layer, l.datatype))
                num_errors += 1

        # Functional-readiness census (what run_verification.py / layout_check needs).
        floorplans = recursive_shapes(layout, top, 99, 0)
        print('Floorplan (99/0) shapes: %s' % len(floorplans))
        texts = recursive_shapes(layout, top, 10, 0)
        opt_ins = [s.text_string for s in texts
                   if s.is_text() and s.text_string.startswith('opt_in')]
        print('opt_in labels (10/0): %s' % len(opt_ins))

        print(num_errors)
        """;
}
