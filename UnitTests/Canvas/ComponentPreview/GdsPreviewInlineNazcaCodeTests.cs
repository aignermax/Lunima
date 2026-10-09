using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Export;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>
/// A component whose geometry its PDK writes as Nazca code (the Demo PDK crossing) has no
/// function in the PDK's Nazca module: its preview must render that code, never ask the
/// module for a function it does not have.
/// </summary>
public class GdsPreviewInlineNazcaCodeTests
{
    private const string Code = "import nazca as nd\n\ndef component():\n    return nd.Cell('x')\n";

    [Fact]
    public async Task Geometry_OfInlineCodeComponent_RendersTheCode_AndSkipsTheDiskCache()
    {
        var nazca = new Mock<NazcaComponentPreviewService>("py", "s.py", (TimeSpan?)null, (ProcessLaunchFactory?)null);
        nazca.Setup(s => s.RenderRawCodeAsync(Code, It.IsAny<CancellationToken>())).ReturnsAsync(Ok());
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-inline-" + Guid.NewGuid().ToString("N"));
        var service = new GdsPreviewRenderService(nazca.Object, new GdsPreviewDiskCache(diskDir))
        {
            InlineNazcaCodeLookup = (module, function) => function == "demo_crossing" ? Code : null,
        };
        var key = new GdsPreviewKey("demofab", "demo_crossing", null);

        service.TryGetGeometry(key).ShouldBeNull();
        await service.WaitForPendingAsync();

        service.TryGetGeometry(key).ShouldNotBeNull();
        nazca.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "the PDK module has no such function");
        (Directory.Exists(diskDir) && Directory.EnumerateFiles(diskDir, "*", SearchOption.AllDirectories).Any())
            .ShouldBeFalse("an edited template must not keep showing a persisted old render");
    }

    [Fact]
    public void Find_ReturnsTheNazcaCodeOfTheMatchingTemplateOnly()
    {
        var templates = new[]
        {
            new ComponentTemplate { Name = "MMI", NazcaModuleName = "demofab", NazcaFunctionName = "mmi1x2" },
            new ComponentTemplate { Name = "Crossing", NazcaModuleName = "demofab", NazcaFunctionName = "demo_crossing", RawCode = Code, RawCodeBackend = "nazca" },
            new ComponentTemplate { Name = "GF", NazcaModuleName = "demofab", NazcaFunctionName = "gf_cell", RawCode = "gf code", RawCodeBackend = "gdsfactory" },
        };

        InlineNazcaCode.Find(templates, "demofab", "demo_crossing").ShouldBe(Code);
        InlineNazcaCode.Find(templates, "demofab", "mmi1x2").ShouldBeNull("rendered by the module");
        InlineNazcaCode.Find(templates, "demofab", "gf_cell").ShouldBeNull("gdsfactory code is not Nazca code");
        InlineNazcaCode.Find(templates, "otherfab", "demo_crossing").ShouldBeNull("a different PDK module");
    }

    private static NazcaPreviewResult Ok() => new()
    {
        Success = true, XMin = 0, YMin = 0, XMax = 10, YMax = 10,
        Polygons = new List<NazcaPreviewPolygon>
        {
            new() { Layer = 1, Vertices = new List<(double, double)> { (0, 0), (10, 0), (10, 10) } },
        },
    };
}
