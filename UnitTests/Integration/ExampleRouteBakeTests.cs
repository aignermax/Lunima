using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.Routing;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Route-bake utility for the shipped examples (issue #1166): recomputes an example's
/// cached routes from scratch through the real load → route → save path and writes the
/// result back into <c>examples/</c> — but only when the bake is at least as good as the
/// shipped state (a file that already carried a full cache keeps it unless the blocked-wire
/// count shrinks). This is how the cached routes in <c>examples/*.lun</c> are regenerated
/// after router improvements.
/// <para>
/// Gated by <c>CAP_BAKE_EXAMPLES=1</c> (unset, the theory is a no-op) and <c>Category=Slow</c>:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_BAKE_EXAMPLES=1 python3 tools/smart_test.py ExampleRouteBake</c>
/// </para>
/// <para>
/// The example JSON is copied to a temp file with every router-output property stripped
/// (<see cref="RouteGeometryProperties"/>), so all connections load path-less and route
/// exactly like a hand-written design would on open. Routing runs through
/// <see cref="DesignCanvasViewModel.RecalculateRoutesAsync"/> — the same full pass the
/// loader's post-load routing uses, invoked directly so the bake is not bound by
/// <see cref="FileOperationsViewModel.MaxConnectionsRoutedOnLoad"/> (which only guards
/// interactive opens). The save goes through the real save command, so the written file
/// is byte-for-byte what the product serializer produces. A verification reload mirrors
/// <see cref="ExampleLoadRoutingTests"/> before the example file is replaced.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class ExampleRouteBakeTests
{
    /// <summary>Environment variable that arms the bake ("1"); unset, the theory is a no-op.</summary>
    private const string BakeEnableVariable = "CAP_BAKE_EXAMPLES";

    /// <summary>';'-separated substring filters selecting the examples to bake (<c>CAP_BAKE_SKIP</c> wins).</summary>
    private const string BakeOnlyVariable = "CAP_BAKE_ONLY";
    private const string BakeSkipVariable = "CAP_BAKE_SKIP";

    /// <summary>Bake-time overrides: ordering-retry cap (main time cost) / per-wire A* node budget (for grids larger than the default budget).</summary>
    private const string BakeMaxAttemptsVariable = "CAP_BAKE_MAX_ATTEMPTS";
    private const string BakePhase2NodesVariable = "CAP_BAKE_PHASE2_NODES";
    private const string ManifestFileName = "examples.json";

    /// <summary>Connection JSON properties written by the router; stripped so every wire routes fresh.</summary>
    private static readonly string[] RouteGeometryProperties =
        { "CachedSegments", "IsBlockedFallback", "IsInvalidGeometry", "IsPlaceholderGeometry" };

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles { get; } = LoadManifestFiles();

    private static bool MatchesAny(string exampleFileName, string semicolonSeparatedSubstrings) =>
        semicolonSeparatedSubstrings.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => exampleFileName.Contains(part, StringComparison.OrdinalIgnoreCase));

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Bake_ExampleFromScratch_NeverRegressesBlockedWires(string exampleFileName)
    {
        if (Environment.GetEnvironmentVariable(BakeEnableVariable) != "1")
            return;

        var only = Environment.GetEnvironmentVariable(BakeOnlyVariable);
        if (!string.IsNullOrEmpty(only) && !MatchesAny(exampleFileName, only))
            return;

        var skip = Environment.GetEnvironmentVariable(BakeSkipVariable);
        if (!string.IsNullOrEmpty(skip) && MatchesAny(exampleFileName, skip))
            return;

        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var prep = PrepareGeometryLessCopy(examplePath);
        if (prep.ConnectionCount == 0)
        {
            ReportProgress($"[bake] {exampleFileName}: no connections — nothing to bake");
            if (File.Exists(prep.TempPath)) File.Delete(prep.TempPath);
            return;
        }

        var watch = Stopwatch.StartNew();
        var (canvas, fileOps) = CreateCanvasAndFileOperations(prep);
        if (int.TryParse(Environment.GetEnvironmentVariable(BakeMaxAttemptsVariable), out var maxAttempts)
            && maxAttempts > 0)
            canvas.ConnectionManager.MaxRoutingAttempts = maxAttempts;
        if (int.TryParse(Environment.GetEnvironmentVariable(BakePhase2NodesVariable), out var phase2Nodes)
            && phase2Nodes > 0)
            canvas.Router.Phase2MaxNodes = phase2Nodes;
        (await fileOps.LoadDesignFromPathAsync(prep.TempPath)).ShouldBeTrue(
            $"'{exampleFileName}' must load with its route geometry stripped");
        await fileOps.PostLoadRouting;
        await canvas.RecalculateRoutesAsync();
        watch.Stop();

        var blocked = canvas.Connections.Count(c => c.Connection.IsBlockedFallback);
        var endpoint = canvas.Connections.Count(c => c.Connection.FailureReason == RoutingFailureReason.EndpointBlocked);
        var contention = canvas.Connections.Count(c => c.Connection.FailureReason == RoutingFailureReason.Contention);
        ReportProgress(FormatCensusLine(exampleFileName, blocked, endpoint, contention, blocked - endpoint - contention));
        if (canvas.ConnectionManager.LastRoutingPassTimings is { } passTimings)
            ReportProgress(FormatPassTimingsLine(exampleFileName, passTimings));

        await fileOps.SaveDesignCommand.ExecuteAsync(null);

        var (verifyCanvas, verifyOps) = CreateCanvasAndFileOperations(prep);
        (await verifyOps.LoadDesignFromPathAsync(prep.TempPath)).ShouldBeTrue(
            $"'{exampleFileName}' must reload after the bake");
        await verifyOps.PostLoadRouting;

        var unrouted = verifyCanvas.Connections.Count(c => c.Connection.RoutedPath == null);
        var blockedAfter = verifyCanvas.Connections.Count(c => c.Connection.IsBlockedFallback);
        ReportProgress($"[bake] {exampleFileName}: {prep.ConnectionCount} connections, "
            + $"blocked {prep.BlockedBefore} → {blockedAfter}, unrouted {unrouted}, "
            + $"routing took {watch.Elapsed.TotalSeconds:F1}s, artifact {prep.TempPath}");

        unrouted.ShouldBe(0, $"'{exampleFileName}': every connection must carry a route after the bake");

        if (!prep.HadFullRouteCache)
        {
            // Shipped without a full cache (fallback lines on open): any complete route set wins.
            File.Copy(prep.TempPath, examplePath, overwrite: true);
            return;
        }

        blockedAfter.ShouldBeLessThanOrEqualTo(prep.BlockedBefore,
            $"'{exampleFileName}': the bake must not add blocked wires — the shipped file was kept");
        if (blockedAfter < prep.BlockedBefore)
            File.Copy(prep.TempPath, examplePath, overwrite: true);
        // The temp artifact stays for diagnosis when a bake regresses or surprises.
    }

    /// <summary>
    /// Creates the canvas/file-operations pair for one bake or verification pass. The
    /// file's chip size is applied to the canvas BEFORE loading and again through
    /// <see cref="FileOperationsViewModel.ApplyChipSizeAfterLoad"/> (wired like the app
    /// wires the chip-size panel in <c>MainViewModel</c>): the loader starts post-load
    /// routing before it restores the chip size, and an unrestored chip would route the
    /// design on the small default grid and save the default chip size back.
    /// </summary>
    private static (DesignCanvasViewModel Canvas, FileOperationsViewModel FileOps) CreateCanvasAndFileOperations(
        BakePreparation prep)
    {
        var canvas = new DesignCanvasViewModel();
        ApplyChipSize(canvas, prep.ChipWidthMicrometers, prep.ChipHeightMicrometers);
        var fileOps = CreateFileOperations(canvas);
        fileOps.ApplyChipSizeAfterLoad = (widthUm, heightUm) => ApplyChipSize(canvas, widthUm, heightUm);
        return (canvas, fileOps);
    }

    /// <summary>Mirrors <c>ChipSizeViewModel.ApplyToCanvas</c>: chip bounds plus a chip-sized routing grid.</summary>
    private static void ApplyChipSize(DesignCanvasViewModel canvas, double widthUm, double heightUm)
    {
        if (widthUm <= 0 || heightUm <= 0)
            return;
        canvas.ChipMinX = 0;
        canvas.ChipMinY = 0;
        canvas.ChipMaxX = widthUm;
        canvas.ChipMaxY = heightUm;
        canvas.InitializeAStarRouting(0, 0, widthUm, heightUm);
    }

    private static FileOperationsViewModel CreateFileOperations(DesignCanvasViewModel canvas)
    {
        var fileOps = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new ErrorConsoleService());
        // Never invoked (CurrentFilePath is set by the load), but SaveDesign requires it non-null.
        fileOps.FileDialogService = new Mock<IFileDialogService>().Object;
        return fileOps;
    }

    /// <summary>
    /// Formats the per-example blocked-wire census line (issue #1249): how many of the
    /// blocked wires are endpoint-blocked (sealed pin — no ordering retry can help) vs.
    /// contention (other wires in the way) vs. unclassified. The mix decides the next
    /// router slice.
    /// </summary>
    internal static string FormatCensusLine(string exampleFileName, int blocked, int endpoint, int contention, int unclassified) =>
        $"[bake] {exampleFileName}: blocked={blocked} (endpoint={endpoint}, contention={contention}, unclassified={unclassified})";

    /// <summary>
    /// Formats the per-pass wall-clock breakdown of the example's full re-route: where the
    /// total routing time went (initial pass, ordering cascade with attempt count, crossing
    /// insertion, pin-lead collapse, bend upsizing, crossing scan, contention repair with
    /// attempts/accepts). Formatted invariant — this is a machine-parseable census line.
    /// </summary>
    internal static string FormatPassTimingsLine(string exampleFileName, RoutingPassTimings timings)
    {
        static string Seconds(TimeSpan duration) =>
            duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);

        return $"[bake] {exampleFileName}: passes total={Seconds(timings.Total)}s ("
            + $"initial={Seconds(timings.InitialPass)}s, "
            + $"ordering-cascade={Seconds(timings.OrderingCascade)}s/{timings.OrderingAttempts} attempts, "
            + $"crossing-dissolve={Seconds(timings.CrossingDissolution)}s, "
            + $"crossing-insert={Seconds(timings.CrossingInsertion)}s, "
            + $"pin-lead-collapse={Seconds(timings.PinLeadCollapse)}s, "
            + $"bend-upsize={Seconds(timings.BendUpsizing)}s, "
            + $"crossing-scan={Seconds(timings.CrossingScan)}s, "
            + $"contention-repair={Seconds(timings.ContentionRepair)}s"
            + $"/{timings.ContentionRepairAttempts} attempts"
            + $"/{timings.ContentionRepairAccepts} accepts)"
            + (timings.OrderingEarlyStopped ? " [early-stop]" : "")
            + (timings.WasCancelled ? " [cancelled]" : "");
    }

    private static void ReportProgress(string line)
    {
        Console.WriteLine(line);
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "route-bake-progress.log"), line + Environment.NewLine);
    }

    /// <summary>Writes a geometry-stripped copy of a .lun file to a temp file (the state a hand-written design loads in).</summary>
    internal static string StripRouteGeometryToTempFile(string sourcePath) =>
        PrepareGeometryLessCopy(sourcePath).TempPath;

    private static BakePreparation PrepareGeometryLessCopy(string examplePath)
    {
        var json = JsonNode.Parse(File.ReadAllText(examplePath))!;
        var prep = new BakePreparation(Path.Combine(Path.GetTempPath(), $"route-bake-{Guid.NewGuid():N}.lun"));
        prep.ChipWidthMicrometers = json?["ChipWidthMicrometers"]?.GetValue<double>() ?? 0;
        prep.ChipHeightMicrometers = json?["ChipHeightMicrometers"]?.GetValue<double>() ?? 0;
        StripRouteGeometryRecursive(json!, prep);
        File.WriteAllText(prep.TempPath, json!.ToJsonString());
        return prep;
    }

    private static void StripRouteGeometryRecursive(JsonNode node, BakePreparation prep)
    {
        if (node is not JsonObject obj)
            return;

        if (obj["Connections"] is JsonArray connections)
        {
            foreach (var connection in connections)
            {
                if (connection is not JsonObject conn)
                    continue;
                prep.ConnectionCount++;
                if (conn["IsBlockedFallback"]?.GetValue<bool>() == true)
                    prep.BlockedBefore++;
                if (conn["CachedSegments"] == null)
                    prep.HadFullRouteCache = false;
                foreach (var property in RouteGeometryProperties)
                    conn.Remove(property);
            }
        }

        if (obj["Groups"] is JsonArray groups)
            foreach (var group in groups)
                if (group != null)
                    StripRouteGeometryRecursive(group, prep);
    }

    private static TheoryData<string> LoadManifestFiles()
    {
        var data = new TheoryData<string>();
        var manifestPath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ManifestFileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        foreach (var entry in doc.RootElement.GetProperty("examples").EnumerateArray())
            data.Add(entry.GetProperty("file").GetString()!);
        return data;
    }

    /// <summary>Mutable bake counters gathered while stripping one example's route geometry.</summary>
    private sealed class BakePreparation(string tempPath)
    {
        public string TempPath { get; } = tempPath;
        public int ConnectionCount { get; set; }
        public int BlockedBefore { get; set; }
        public bool HadFullRouteCache { get; set; } = true;
        public double ChipWidthMicrometers { get; set; }
        public double ChipHeightMicrometers { get; set; }
    }
}