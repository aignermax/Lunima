using System.Text.Json.Nodes;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Crossing bake for the shipped examples: loads an example with its cached routes, connects
/// its blocked wires — top-level ones and those frozen inside groups
/// (<see cref="GroupCrossingChainBake"/>) — through chains of placed crossings (every other
/// route stays as shipped),
/// verifies the saved result reloads with fewer blocked wires, and only then writes it back
/// into <c>examples/</c>. This is how blocked wires in the examples are resolved after
/// crossing-routing improvements.
/// <para>
/// Gated by <c>CAP_BAKE_CROSSINGS=1</c> (unset, the theory is a no-op) and <c>Category=Slow</c>;
/// <c>CAP_BAKE_ONLY</c> restricts it to examples whose file name contains one of its
/// ';'-separated parts, <c>CAP_BAKE_SKIP</c> leaves those out (e.g. a composed example that
/// is re-authored from its baked parts instead). <c>CAP_BAKE_SOURCE_DIR</c> bakes from
/// scratch: the example in that directory (e.g. a version without crossings) is stripped of
/// its routes, fully re-routed and then connected through crossings; the result replaces the
/// shipped file only if it has fewer blocked paths than the shipped one.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class ExampleCrossingBakeTests
{
    private const string BakeEnableVariable = "CAP_BAKE_CROSSINGS";
    private const string BakeOnlyVariable = "CAP_BAKE_ONLY";
    private const string BakeSkipVariable = "CAP_BAKE_SKIP";
    private const string BakeSourceDirectoryVariable = "CAP_BAKE_SOURCE_DIR";

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleRouteBakeTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Bake_BlockedWiresThroughCrossings_NeverRegresses(string exampleFileName)
    {
        if (Environment.GetEnvironmentVariable(BakeEnableVariable) != "1") return;
        var only = Environment.GetEnvironmentVariable(BakeOnlyVariable);
        if (!string.IsNullOrEmpty(only) && !MatchesAny(exampleFileName, only)) return;
        var skip = Environment.GetEnvironmentVariable(BakeSkipVariable);
        if (!string.IsNullOrEmpty(skip) && MatchesAny(exampleFileName, skip)) return;

        var (shipped, shippedOps, _) = await MziFringeAnalysis.LoadExample(exampleFileName);
        await shippedOps.PostLoadRouting;
        int blockedBefore = TotalBlockedPaths(shipped);
        if (blockedBefore == 0) return;
        var sourceDirectory = Environment.GetEnvironmentVariable(BakeSourceDirectoryVariable);
        var sourcePath = string.IsNullOrEmpty(sourceDirectory) ? null : Path.Combine(sourceDirectory, exampleFileName);
        var (canvas, fileOps) = sourcePath != null && File.Exists(sourcePath)
            ? await RouteFromScratchAsync(sourcePath)
            : (shipped, shippedOps);

        var templates = TestPdkLoader.LoadAllTemplates();
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        var binder = new CrossingInsertionCanvasBinder(
            canvas, () => CrossingComponentInstance.CreateFromTemplates(templates, preferred), action => action());
        binder.Service.ChainPassTimeBudget = TimeSpan.MaxValue;
        // Offline there is time for the searches the interactive pass cannot afford.
        binder.Service.FallbackChainSearches = new[] { (40_000_000, 1.0), (8_000_000, 2.5) };
        int connected = binder.Service.ConnectBlockedWiresThroughCrossings(canvas.ConnectionManager, canvas.Router);
        Component? NewCrossing() => CrossingComponentInstance.CreateFromTemplates(templates, preferred)?.Component;
        foreach (var group in TopLevelGroups(canvas))
            connected += GroupCrossingChainBake.Bake(group, NewCrossing, canvas.Router.MinBendRadiusMicrometers,
                canvas.ConnectionManager.WaveguideWidthMicrometers);
        Console.WriteLine($"[crossing-bake] {exampleFileName}: blocked paths {blockedBefore}, connected {connected}");
        if (connected == 0 && ReferenceEquals(canvas, shipped)) return;

        var bakedPath = Path.Combine(Path.GetTempPath(), $"crossing-bake-{Guid.NewGuid():N}.lun");
        await MziFringeAnalysis.SaveToFileAsync(fileOps, bakedPath);
        var (verify, verifyOps, _) = await MziFringeAnalysis.LoadDesignFromPath(bakedPath);
        await verifyOps.PostLoadRouting;

        verify.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0, "every wire reloads with a route");
        int blockedAfter = TotalBlockedPaths(verify);
        Console.WriteLine($"[crossing-bake] {exampleFileName}: reloaded blocked paths {blockedAfter}, artifact {bakedPath}");
        blockedAfter.ShouldBeLessThan(blockedBefore, "the bake must reduce the blocked paths");
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        File.WriteAllText(examplePath, KeepShippedProcess(examplePath, bakedPath));
    }

    /// <summary>Loads the source design without route geometry and routes it fully on its chip.</summary>
    private static async Task<(DesignCanvasViewModel Canvas, CAP.Avalonia.ViewModels.Panels.FileOperationsViewModel FileOps)>
        RouteFromScratchAsync(string sourcePath)
    {
        var prep = ExampleRouteBakeTests.PrepareGeometryLessCopy(sourcePath);
        var (canvas, fileOps) = ExampleRouteBakeTests.CreateCanvasAndFileOperations(prep);
        (await fileOps.LoadDesignFromPathAsync(prep.TempPath)).ShouldBeTrue($"'{sourcePath}' must load without its routes");
        await fileOps.PostLoadRouting;
        await canvas.RecalculateRoutesAsync();
        Console.WriteLine($"[crossing-bake] {Path.GetFileName(sourcePath)}: from scratch, {TotalBlockedPaths(canvas)} blocked before crossings");
        return (canvas, fileOps);
    }

    private static bool MatchesAny(string exampleFileName, string semicolonSeparatedParts) =>
        semicolonSeparatedParts.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => exampleFileName.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static List<ComponentGroup> TopLevelGroups(DesignCanvasViewModel canvas) =>
        canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList();

    /// <summary>Blocked paths of the whole design: top-level wires plus those frozen inside groups.</summary>
    private static int TotalBlockedPaths(DesignCanvasViewModel canvas) =>
        new DesignValidator()
            .Validate(canvas.Connections.Select(c => c.Connection).ToList(), TopLevelGroups(canvas))
            .Count(issue => issue.Type == DesignIssueType.BlockedPath);

    /// <summary>
    /// The baked JSON with the shipped file's active-process entry: loading in a test session
    /// activates the playground process, and saving would otherwise pin it into the example.
    /// </summary>
    private static string KeepShippedProcess(string shippedPath, string bakedPath)
    {
        const string ActiveProcessProperty = "ActiveProcess";
        var shipped = JsonNode.Parse(File.ReadAllText(shippedPath))!.AsObject();
        var baked = JsonNode.Parse(File.ReadAllText(bakedPath))!.AsObject();
        if (JsonNode.DeepEquals(shipped[ActiveProcessProperty], baked[ActiveProcessProperty]))
            return File.ReadAllText(bakedPath);
        baked.Remove(ActiveProcessProperty);
        if (shipped[ActiveProcessProperty] is { } process)
            baked[ActiveProcessProperty] = process.DeepClone();
        return baked.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
