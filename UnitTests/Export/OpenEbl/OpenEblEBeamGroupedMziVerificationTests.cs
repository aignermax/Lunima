using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration;
using Xunit;
using static UnitTests.Integration.MziFringeAnalysis;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// openEBL honesty gate for issue #1351 — the export-side twin of
/// <see cref="EBeamMziGroupedFringeHonestyTests"/>: a student who groups the EBeam
/// MZI body ("Make component") must still submit the design. Loads the shipped
/// <c>EBeam Mach-Zehnder Interferometer.lun</c>, groups splitter + combiner through
/// the real <c>CreateGroupCommand</c> (both arms freeze into group-internal paths),
/// exports through the real nazca path and runs BOTH vendored openEBL ports against
/// the produced GDS — the submission checks (<see cref="OpenEblMziReadinessTests"/>)
/// and the functional verification (<see cref="OpenEblEBeamMziVerificationTests"/>).
/// The pinned state: frozen group paths export as SiEPIC-conformant
/// <c>Waveguide_&lt;n&gt;</c> cells exactly like routed connections
/// (<see cref="SiepicWaveguideCellWriter"/>), so the grouped design passes with the
/// same zero-error census as the flat one — hierarchy is transparent to
/// manufacturability. A nested variant (MZI group grouped again with the output
/// coupler) and a save → load round-trip pin the same 0/0.
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (installed
/// on the CI runner by the "Install Nazca + KLayout + SiEPIC PDK" step); skips
/// cleanly elsewhere. Setting LUNIMA_OPENEBL_ARTIFACT_DIR copies the script, GDS,
/// checkers and raw outputs there for inspection.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblEBeamGroupedMziVerificationTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";

    [SkippableFact]
    public async Task GroupedMzi_SubmissionCheckAndVerification_ZeroErrors()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        GroupMziBody(canvas);
        await canvas.RecalculateRoutesAsync();

        await RunOpenEblPortsAsync(python, canvas, "ebeam_mzi_grouped");
    }

    [SkippableFact]
    public async Task NestedGroupedMzi_SubmissionCheckAndVerification_ZeroErrors()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        GroupMziBody(canvas);
        await canvas.RecalculateRoutesAsync();

        // Group the MZI group again, together with the output grating coupler: the
        // combiner→GC connection freezes inside the outer group (nested hierarchy).
        var mziGroupVm = canvas.Components.Single(c => c.Component is ComponentGroup);
        var gcOutVm = canvas.Components.Single(c => c.Component.Identifier == "gc_out");
        new CreateGroupCommand(canvas, new List<ComponentViewModel> { mziGroupVm, gcOutVm }).Execute();
        await canvas.RecalculateRoutesAsync();

        var outer = (ComponentGroup)canvas.Components.Single(c => c.Component is ComponentGroup).Component;
        outer.ChildComponents.OfType<ComponentGroup>().Single().InternalPaths.Count.ShouldBe(2,
            "the MZI arms must stay frozen inside the nested group");

        await RunOpenEblPortsAsync(python, canvas, "ebeam_mzi_nested");
    }

    [SkippableFact]
    public async Task GroupedMzi_AfterSaveLoadRoundTrip_SubmissionCheckAndVerification_ZeroErrors()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        var (canvas, fileOps, _) = await LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;

        GroupMziBody(canvas);
        await canvas.RecalculateRoutesAsync();

        var tempFile = Path.Combine(Path.GetTempPath(), $"mzi_grouped_openebl_{Guid.NewGuid():N}.lun");
        try
        {
            await SaveToFileAsync(fileOps, tempFile);

            var (loadCanvas, loadFileOps, _) = await LoadDesignFromPath(tempFile);
            await loadFileOps.PostLoadRouting;
            loadCanvas.Components.Count(c => c.Component is ComponentGroup).ShouldBe(1,
                "the group must survive the .lun round-trip");

            await RunOpenEblPortsAsync(python, loadCanvas, "ebeam_mzi_grouped_reloaded");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Exports the canvas through the app's own exporter, renders the script under
    /// real nazca (klayout foundry-cell upgrade + waveguide spine pass included) and
    /// runs both vendored openEBL ports against the GDS: the submission checks and
    /// the functional verification. Both must complete with zero errors — the same
    /// census the flat MZI pins (<see cref="OpenEblEBeamMziReadinessTests"/>,
    /// <see cref="OpenEblEBeamMziVerificationTests"/>).
    /// </summary>
    private static async Task RunOpenEblPortsAsync(string python, DesignCanvasViewModel canvas, string fileStem)
    {
        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas, skippedConnections: skippedConnections, exportWarnings: exportWarnings);
        skippedConnections.ShouldBeEmpty("every grouped MZI route must stay exportable geometry");
        exportWarnings.ShouldBeEmpty();

        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-grouped-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var scriptPath = Path.Combine(dir, fileStem + ".py");
            await File.WriteAllTextAsync(scriptPath, script);
            var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, scriptPath);
            run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
            var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
            File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");
            CopyArtifact(scriptPath);
            CopyArtifact(gdsPath);

            // ── Submission checks (klayout-only port, same as the flat readiness gate) ──
            var checkerPath = Path.Combine(dir, fileStem + "_submission_check.py");
            await File.WriteAllTextAsync(checkerPath, OpenEblMziReadinessTests.SubmissionCheckerScript);
            var check = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, checkerPath, gdsPath);
            check.ExitCode.ShouldBe(0, $"submission-check port crashed:\n{check.StdOut}\n{check.StdErr}");
            var checkOutput = check.StdOut;
            var checkErrors = int.Parse(checkOutput.TrimEnd().Split('\n').Last().Trim());
            CopyArtifact(checkerPath);
            WriteArtifact(fileStem + "_check_output.txt", checkOutput);
            checkOutput.ShouldContain("Top cell: ConnectAPIC_Design");
            checkErrors.ShouldBe(0, $"the grouped EBeam MZI must pass the submission checks:\n{checkOutput}");

            // ── Functional verification (SiEPIC layout_check port) ──
            var runnerPath = Path.Combine(dir, fileStem + "_verification.py");
            await File.WriteAllTextAsync(runnerPath, OpenEblEBeamMziVerificationTests.VerificationRunnerScript);
            var verify = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, runnerPath, gdsPath);
            verify.ExitCode.ShouldBe(0, $"verification port crashed:\n{verify.StdOut}\n{verify.StdErr}");
            var verifyOutput = verify.StdOut;
            var verifyErrors = int.Parse(verifyOutput.TrimEnd().Split('\n').Last().Trim());
            CopyArtifact(runnerPath);
            CopyArtifact(Path.ChangeExtension(gdsPath, ".lyrdb"));
            WriteArtifact(fileStem + "_verification_output.txt", verifyOutput);

            verifyOutput.ShouldContain("Top cell: ConnectAPIC_Design");
            verifyOutput.ShouldNotContain("Unknown error occurred");
            // Grouped frozen paths are SiEPIC waveguide cells like routed connections:
            // no route polygon sits outside a component and every optical pin is netted.
            verifyOutput.ShouldContain("category Shapes outside component: 0");
            verifyOutput.ShouldContain("category Disconnected pin: 0");
            verifyErrors.ShouldBe(0,
                $"the grouped EBeam MZI verification must pass cleanly:\n{verifyOutput}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }

    /// <summary>
    /// When LUNIMA_OPENEBL_ARTIFACT_DIR is set, copies one artifact file there.
    /// Called incrementally (before the assertions), so a failing run still leaves
    /// its script, GDS and checker outputs behind for inspection.
    /// </summary>
    private static void CopyArtifact(string path)
    {
        var artifactDir = Environment.GetEnvironmentVariable("LUNIMA_OPENEBL_ARTIFACT_DIR");
        if (string.IsNullOrEmpty(artifactDir) || !File.Exists(path))
            return;
        Directory.CreateDirectory(artifactDir);
        File.Copy(path, Path.Combine(artifactDir, Path.GetFileName(path)), overwrite: true);
    }

    private static void WriteArtifact(string fileName, string content)
    {
        var artifactDir = Environment.GetEnvironmentVariable("LUNIMA_OPENEBL_ARTIFACT_DIR");
        if (string.IsNullOrEmpty(artifactDir))
            return;
        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, fileName), content);
    }
}
