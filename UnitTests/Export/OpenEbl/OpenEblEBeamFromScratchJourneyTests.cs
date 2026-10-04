using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.OpenEblCheck;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using Moq;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Issue #1363 — the first openEBL proof for a design a student builds THEMSELVES:
/// blank canvas, SiEPIC library templates dropped at snap-grid positions, pins clicked
/// together (<see cref="DesignCanvasViewModel.ConnectPinsAsync"/>, router draws the
/// arms — no manual path points), coherent fringes measured against the routed ΔL,
/// export through the real nazca path and <see cref="OpenEblSubmissionChecker"/> (the
/// product service) — before and after a save/load round trip.
/// <para>
/// Layout: three GCs on the 127 µm openEBL fiber-array pitch (laser on top — at most
/// one GC above and two below the opt_in label), splitter ~100 µm right of the column,
/// combiner at the splitter's height with the arms cross-connected so the router must
/// draw unequal arms. The routed ΔL is 31.7 µm (deterministic): six deep nulls in the
/// 1500–1600 nm sweep, FSR matching λ²/(n_g·ΔL) with the PDK group index. The spare
/// coupler is terminated (a dangling GC waveguide pin is a proven openEBL
/// "Disconnected pin" error, and 3 GCs + 2 Y-branches have odd pin parity) and carries
/// the second opt_in label — SiEPIC's DFT check requires a label on every sub-circuit
/// that contains a grating coupler.
/// </para>
/// <para>
/// PINNED DEFECT (the journey's finding): the cross-connected arms are a non-planar
/// two-wire crossover, and the router leaves the second arm CROSSING the first. Since
/// the router-honesty fix (#1381) the crossing arm keeps its blocked-fallback stamp, so
/// the canvas and DRC-lite report it. The exported GDS still has exactly one SiEPIC
/// verification error ("Overlapping component" where the two arm waveguide cells
/// intersect near (267, 36) µm) — the stamp reports the crossing, it does not remove
/// the geometry. The test pins that single error with its category; every other
/// journey step — routing, fringes, submission check, opt_in labels, die size,
/// save/load — is asserted green. When the router learns to untangle the pair, this
/// test must flip to 0/0.
/// </para>
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (installed
/// on the CI runner by the "Install Nazca + KLayout + SiEPIC PDK" step); skips
/// cleanly elsewhere.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblEBeamFromScratchJourneyTests
{
    private const int SweepStepCount = 1001;
    private const double MaxFsrRelativeError = 0.10;
    private const double MinArmLengthDifferenceMicrometers = 25.0;

    /// <summary>The pinned router defect: exactly one verification error, the arm crossing.</summary>
    private const int PinnedVerificationErrorCount = 1;

    [SkippableFact]
    public async Task StudentBuiltEBeamMzi_FullJourney_PinsRemainingRouterDefect()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        // ── 1-3. Blank canvas, library placement, router-drawn connections ──
        var templates = TestPdkLoader.LoadAllTemplates()
            .Where(t => t.PdkSource == EBeamFromScratchMziDesign.EBeamPdkName).ToList();
        var canvas = await EBeamFromScratchMziDesign.BuildRoutedAsync(templates);

        // ── 4. Every connection routed for real; DRC-lite reports only the crossing ──
        AssertFullyRouted(canvas);
        // Fully qualified: CAP_Core.Analysis is a sibling feature namespace the Export
        // slice may not import (VerticalSliceConventionTests).
        var validator = new CAP_Core.Analysis.DesignValidator();
        var components = canvas.Components.Select(c => c.Component).ToList();
        var externalPortPins = components
            .Where(c => c.Identifier is "gc_in" or "gc_out" or "gc_spare")
            .SelectMany(c => c.PhysicalPins.Where(p => p.Name == "port 1"))
            .ToList();
        var drcIssues = validator.Validate(
            canvas.ConnectionManager.Connections, components, externalPortPins);
        var blockedIssue = drcIssues.ShouldHaveSingleItem(
            "DRC-lite must report the crossing arm's blocked fallback — and nothing else");
        blockedIssue.Description.ShouldContain("no free lane");
        validator.ValidateComponentBounds(components,
                EBeamFromScratchMziDesign.ChipWidthMicrometers,
                EBeamFromScratchMziDesign.ChipHeightMicrometers)
            .ShouldBeEmpty("every component must sit inside the 605 x 410 µm floorplan");

        // The pinned defect, canvas level: the two arms cross, and since #1381 the
        // crossing arm KEEPS its blocked-fallback stamp — the crossing is reported on
        // the canvas instead of rendering as a clean route.
        var upperArm = MziFringeAnalysis.FindConnection(canvas, "mzi_splitter", "port 2");
        var lowerArm = MziFringeAnalysis.FindConnection(canvas, "mzi_splitter", "port 3");
        PathIntersectionDetector.Crosses(upperArm.RoutedPath!, lowerArm.RoutedPath!)
            .ShouldBeTrue("pinned defect #1363: the router leaves the cross-connected arms overlapping");
        var stampedArms = new[] { upperArm, lowerArm }.Where(c => c.IsBlockedFallback).ToList();
        stampedArms.Count.ShouldBe(1,
            "router honesty (#1381): the crossing arm must keep its blocked-fallback stamp");

        // ── 5. Coherent fringes against the routed ΔL and the PDK group index ──
        var outputPin = MziFringeAnalysis.FindPin(
            MziFringeAnalysis.FindComponent(canvas, "gc_out"), "port 2");
        // Since #1381 the stamped crossing arm is the LONG one (the repair routes it
        // first and the clean arm takes the direct lane), so the meander is port 2 —
        // the fringe physics only needs |ΔL|.
        double deltaL = Math.Abs(
            upperArm.PathLengthMicrometers - lowerArm.PathLengthMicrometers);
        deltaL.ShouldBeGreaterThanOrEqualTo(MinArmLengthDifferenceMicrometers,
            "the cross-connected arms must force the router into an unequal-arm layout");

        var coherent = await MziFringeAnalysis.SweepOutputPowerAsync(
            canvas, outputPin, coherent: true, sweepStepCount: SweepStepCount);
        var minima = MziFringeAnalysis.FindFringeMinimaIndices(coherent.Power, window: 5);
        minima.Count.ShouldBeGreaterThanOrEqualTo(3,
            "an unequal-arm MZI must show interference nulls in a 1500-1600 nm sweep");

        var nulls = minima
            .Select(i => MziFringeAnalysis.RefineMinimumWavelength(coherent.WavelengthsNm, coherent.Power, i))
            .ToList();
        for (int i = 1; i < nulls.Count; i++)
        {
            double fsr = nulls[i] - nulls[i - 1];
            double expected = MziFringeAnalysis.ExpectedFsrNm(
                (nulls[i] + nulls[i - 1]) / 2.0, EBeamFromScratchMziDesign.GroupIndex, deltaL);
            Math.Abs(fsr - expected).ShouldBeLessThanOrEqualTo(MaxFsrRelativeError * expected,
                $"FSR {fsr:F2} nm must match λ²/(n_g·ΔL) = {expected:F2} nm " +
                $"(ΔL = {deltaL:F2} µm from the routed geometry)");
        }

        var incoherent = await MziFringeAnalysis.SweepOutputPowerAsync(
            canvas, outputPin, coherent: false, sweepStepCount: SweepStepCount);
        MziFringeAnalysis.FindFringeMinimaIndices(incoherent.Power, window: 5)
            .ShouldBeEmpty("without coherent propagation phase there must be no interference nulls");

        // ── 6. Real nazca export + product submission checker ──
        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-scratch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var checker = new OpenEblSubmissionChecker(ProcessLaunchFactory.CreateDefault(), pythonPath: python);
            var gdsPath = await ExportToGdsAsync(canvas, python, dir, "from_scratch_mzi");
            AssertCheckReport(await checker.CheckAsync(gdsPath));

            // ── 7. Save → load → re-export: same result ──
            var lunPath = Path.Combine(dir, "from_scratch_mzi.lun");
            await MziFringeAnalysis.SaveToFileAsync(CreateFileOperations(canvas, templates), lunPath);
            var (loadedCanvas, loadedFileOps, _) = await MziFringeAnalysis.LoadDesignFromPath(lunPath);
            await loadedFileOps.PostLoadRouting;
            AssertFullyRouted(loadedCanvas);

            var reloadedGdsPath = await ExportToGdsAsync(loadedCanvas, python, dir, "from_scratch_mzi_reloaded");
            AssertCheckReport(await checker.CheckAsync(reloadedGdsPath));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }

    private static void AssertFullyRouted(DesignCanvasViewModel canvas)
    {
        canvas.Connections.Count.ShouldBe(5, "input, two arms, terminator feed and output");
        foreach (var connVm in canvas.Connections)
        {
            var c = connVm.Connection;
            c.RoutedPath.ShouldNotBeNull(
                $"{c.StartPin?.ParentComponent.Identifier}.{c.StartPin?.Name} must be routed");
            c.IsPathValid.ShouldBeTrue(
                $"{c.StartPin?.ParentComponent.Identifier}.{c.StartPin?.Name} has invalid route geometry");
        }
        // Router honesty (#1381): exactly one connection may carry the blocked-fallback
        // stamp — the crossing MZI arm. Every other route must be clean.
        var blocked = canvas.Connections.Where(c => c.Connection.IsBlockedFallback).ToList();
        var blockedNames = string.Join(", ",
            blocked.Select(c => $"{c.Connection.StartPin?.ParentComponent.Identifier}.{c.Connection.StartPin?.Name}"));
        blocked.Count.ShouldBe(1, $"only the crossing arm may be blocked, found: {blockedNames}");
        blocked[0].Connection.StartPin?.ParentComponent.Identifier.ShouldBe("mzi_splitter");
    }

    private static void AssertCheckReport(OpenEblCheckReport report)
    {
        // Green half: the submission check is clean — die inside the 605 x 410 µm
        // floorplan, EBeam-conformant layers, both opt_in labels present.
        report.SubmissionErrorCount.ShouldBe(0,
            $"submission output:\n{report.SubmissionOutput}");
        report.SubmissionChecksPassed.ShouldBeTrue();
        report.SubmissionOutput.ShouldContain("opt_in labels (10/0): 2");
        report.DieBoundingBox.ShouldNotBeNull();
        report.DieBoundingBox.WidthMicrometers.ShouldBeLessThanOrEqualTo(
            EBeamFromScratchMziDesign.ChipWidthMicrometers);
        report.DieBoundingBox.HeightMicrometers.ShouldBeLessThanOrEqualTo(
            EBeamFromScratchMziDesign.ChipHeightMicrometers);

        // Pinned defect: exactly one verification error — the arm waveguide cells
        // overlap where the cross-connected arms intersect.
        report.VerificationErrorCount.ShouldBe(PinnedVerificationErrorCount,
            $"pinned defect #1363 (router leaves the crossed arms overlapping) — " +
            $"verification output:\n{report.VerificationOutput}");
        report.VerificationOutput.ShouldContain("category Overlapping component: 1");
        report.VerificationOutput.ShouldContain("category Disconnected pin: 0");
        report.VerificationOutput.ShouldContain("category Shapes outside component: 0");
    }

    private static async Task<string> ExportToGdsAsync(
        DesignCanvasViewModel canvas, string python, string workDir, string fileBaseName)
    {
        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas,
            skippedConnections: skippedConnections,
            exportWarnings: exportWarnings,
            designName: "FromScratchMzi");
        skippedConnections.ShouldBeEmpty("every route must be real, exportable geometry");
        exportWarnings.ShouldBeEmpty();
        script.ShouldNotContain("ic.sbend_p2p");

        var scriptPath = Path.Combine(workDir, fileBaseName + ".py");
        await File.WriteAllTextAsync(scriptPath, script);
        var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, workDir, scriptPath);
        run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
        var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
        File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");
        return gdsPath;
    }

    private static FileOperationsViewModel CreateFileOperations(
        DesignCanvasViewModel canvas, IReadOnlyList<ComponentTemplate> templates)
    {
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
        fileOps.FileDialogService = new Mock<IFileDialogService>().Object;
        return fileOps;
    }
}
