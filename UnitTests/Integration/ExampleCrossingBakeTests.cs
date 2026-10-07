using System.Text.Json.Nodes;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Crossing bake for the shipped examples: loads an example with its cached routes, connects
/// its blocked wires through chains of placed crossings (every other route stays as shipped),
/// verifies the saved result reloads with fewer blocked wires, and only then writes it back
/// into <c>examples/</c>. This is how blocked wires in the examples are resolved after
/// crossing-routing improvements.
/// <para>
/// Gated by <c>CAP_BAKE_CROSSINGS=1</c> (unset, the theory is a no-op) and <c>Category=Slow</c>;
/// <c>CAP_BAKE_ONLY</c> restricts it to examples whose file name contains one of its
/// ';'-separated parts.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class ExampleCrossingBakeTests
{
    private const string BakeEnableVariable = "CAP_BAKE_CROSSINGS";
    private const string BakeOnlyVariable = "CAP_BAKE_ONLY";

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleRouteBakeTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Bake_BlockedWiresThroughCrossings_NeverRegresses(string exampleFileName)
    {
        if (Environment.GetEnvironmentVariable(BakeEnableVariable) != "1") return;
        var only = Environment.GetEnvironmentVariable(BakeOnlyVariable);
        if (!string.IsNullOrEmpty(only) && !only.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => exampleFileName.Contains(part, StringComparison.OrdinalIgnoreCase)))
            return;

        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(exampleFileName);
        await fileOps.PostLoadRouting;
        int blockedBefore = canvas.ConnectionManager.Connections.Count(c => c.IsBlockedFallback);
        if (blockedBefore == 0) return;

        var templates = TestPdkLoader.LoadAllTemplates();
        var preferred = CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), templates);
        var binder = new CrossingInsertionCanvasBinder(
            canvas, () => CrossingComponentInstance.CreateFromTemplates(templates, preferred), action => action());
        int connected = binder.Service.ConnectBlockedWiresThroughCrossings(canvas.ConnectionManager, canvas.Router);
        Console.WriteLine($"[crossing-bake] {exampleFileName}: blocked {blockedBefore}, connected {connected}");
        if (connected == 0) return;

        var bakedPath = Path.Combine(Path.GetTempPath(), $"crossing-bake-{Guid.NewGuid():N}.lun");
        await MziFringeAnalysis.SaveToFileAsync(fileOps, bakedPath);
        var (verify, verifyOps, _) = await MziFringeAnalysis.LoadDesignFromPath(bakedPath);
        await verifyOps.PostLoadRouting;

        verify.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0, "every wire reloads with a route");
        int blockedAfter = verify.ConnectionManager.Connections.Count(c => c.IsBlockedFallback);
        Console.WriteLine($"[crossing-bake] {exampleFileName}: reloaded blocked {blockedAfter}, artifact {bakedPath}");
        blockedAfter.ShouldBeLessThan(blockedBefore, "the bake must reduce the blocked wires");
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        File.WriteAllText(examplePath, KeepShippedProcess(examplePath, bakedPath));
    }

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
