using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Integration;

/// <summary>
/// The waveguide companion layers shipped with the bundled PDKs must not flood the design
/// checks of the shipped examples: after load + post-load routing, the per-connection DRC
/// (the path <c>MainViewModel.RunDesignChecks</c> takes) reports exactly as many spacing
/// findings with the companion layers as without them. The demo process' trench band is a
/// Trench, not a KeepOut, so it adds none. The test also logs what the examples would report
/// if the demo band were a keep-out, to keep the cost of that choice visible.
/// </summary>
[Trait("Category", "Slow")]
public class ExampleCompanionKeepOutDrcTests
{
    private const string DemoPdkFile = "demo-pdk.json";
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    public ExampleCompanionKeepOutDrcTests(ITestOutputHelper output) => _output = output;

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles => ExampleWaveguideCrossingTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task ShippedCompanionLayers_AddNoSpacingFindings(string exampleFileName)
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(exampleFileName);
        await fileOps.PostLoadRouting;
        var templates = TestPdkLoader.LoadAllTemplates();

        int without = CountSpacingFindings(canvas, templates, LoadDrafts(), keepCompanions: false);
        int with = CountSpacingFindings(canvas, templates, LoadDrafts(), keepCompanions: true);
        int keepOut2 = CountSpacingFindings(canvas, templates, DemoAsKeepOut(2.0), keepCompanions: true);
        int keepOut8 = CountSpacingFindings(canvas, templates, DemoAsKeepOut(8.0), keepCompanions: true);
        _output.WriteLine(
            $"{exampleFileName}: spacing findings without={without} shipped={with} " +
            $"whatIfDemoKeepOut2um={keepOut2} whatIfDemoKeepOut8um={keepOut8}");

        with.ShouldBe(without, $"'{exampleFileName}': shipped companion layers must not add spacing findings");
    }

    private static int CountSpacingFindings(
        DesignCanvasViewModel canvas, List<ComponentTemplate> templates,
        IReadOnlyList<PdkDraft> drafts, bool keepCompanions)
    {
        var connections = canvas.Connections.Select(c => c.Connection).ToList();
        var groups = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList();
        string? PdkOf(PhysicalPin? pin) =>
            pin?.ParentComponent is { } component ? ComponentPdkSourceResolver.Resolve(component, templates) : null;

        ConnectionDrcRules? Rules(WaveguideConnection connection)
        {
            var rules = ConnectionDrcRuleResolver.ResolveForEndpointPdkNames(
                PdkOf(connection.StartPin), PdkOf(connection.EndPin), drafts);
            return rules == null || keepCompanions
                ? rules
                : new ConnectionDrcRules(rules.WidthRules, rules.MinSpacingMicrometers);
        }

        return new PerConnectionDrcChecker()
            .Check(connections, groups, 0, null, Rules)
            .Count(i => i.Type == DesignIssueType.WaveguideSpacingViolation);
    }

    private static List<PdkDraft> LoadDrafts()
    {
        var pdkDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs");
        var loader = new PdkLoader();
        return Directory.GetFiles(pdkDir, "*.json").OrderBy(f => f).Select(loader.LoadFromFile).ToList();
    }

    private static List<PdkDraft> DemoAsKeepOut(double offsetMicrometers)
    {
        var drafts = LoadDrafts();
        var demo = drafts.Single(d => d.FilePath?.EndsWith(DemoPdkFile, StringComparison.OrdinalIgnoreCase) == true
                                      || d.Process?.Name == "Demo SOI 220nm");
        foreach (var layer in demo.Process!.WaveguideCompanionLayers!)
        {
            layer.Role = WaveguideCompanionRole.KeepOut;
            layer.OffsetUm = offsetMicrometers;
        }
        return drafts;
    }
}
