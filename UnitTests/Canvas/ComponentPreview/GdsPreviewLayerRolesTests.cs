using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>
/// Process-derived core/metal layer roles for the GDS preview (issue #1494): a CornerStone
/// SiN core on layer 203 must render with the waveguide brush instead of the faint "other"
/// grey, and the SiEPIC heater on layer 11 must read as copper. Without a resolvable
/// process (or with the demo PDK) the preview behaves exactly as before #1494.
/// </summary>
public class GdsPreviewLayerRolesTests
{
    private const int CornerstoneCoreLayer = 203;
    private const int CornerstoneHeaterLayer = 39;
    private const int CornerstonePadLayer = 41;
    private const int SiepicCoreLayer = 1;
    private const int SiepicHeaterLayer = 11;
    private const int OutlineLayer = 999;

    private static string PdkDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs");

    private static GdsPreviewLayerRoles RolesFor(string pdkFile) =>
        GdsPreviewLayerRoles.FromProcess(new PdkLoader().LoadFromFile(Path.Combine(PdkDir, pdkFile)).Process);

    [Fact]
    public void CornerStoneSin_NitrideCore203_GetsCoreBrush_AndHeater39_GetsMetalBrush()
    {
        var roles = RolesFor("cornerstone-sin-pdk.json");

        // The core/metal brushes themselves are internal to the style; asserting brush
        // identity against the known default-role core (1) and metal (10) layers pins them.
        GdsPreviewLayerStyle.BrushFor(CornerstoneCoreLayer, roles)
            .ShouldBeSameAs(GdsPreviewLayerStyle.BrushFor(1, GdsPreviewLayerRoles.Default));
        GdsPreviewLayerStyle.BrushFor(CornerstoneHeaterLayer, roles)
            .ShouldBeSameAs(GdsPreviewLayerStyle.BrushFor(10, GdsPreviewLayerRoles.Default));
        GdsPreviewLayerStyle.BrushFor(CornerstonePadLayer, roles)
            .ShouldBeSameAs(GdsPreviewLayerStyle.BrushFor(10, GdsPreviewLayerRoles.Default));
        // Under the demo roles layer 203 was the faint "other" grey — that was the bug.
        GdsPreviewLayerStyle.BrushFor(CornerstoneCoreLayer, GdsPreviewLayerRoles.Default)
            .ShouldNotBeSameAs(GdsPreviewLayerStyle.BrushFor(CornerstoneCoreLayer, roles));
    }

    [Fact]
    public void CornerStoneSin_Core203_IsDrawnLast_AndOutlineSlabIsDropped()
    {
        var roles = RolesFor("cornerstone-sin-pdk.json");
        var result = Cell(
            Rect(OutlineLayer, 0, 0, 100, 20),
            Rect(CornerstoneHeaterLayer, 0, 2, 100, 8),
            Rect(CornerstoneCoreLayer, 0, 9, 100, 11));

        var layers = GdsPreviewLayerStyle.InDrawOrder(result, roles).Select(p => p.Layer).ToList();

        layers.ShouldBe(new[] { CornerstoneHeaterLayer, CornerstoneCoreLayer });
    }

    [Fact]
    public void SiepicEBeam_WgCore1_GetsCoreBrush_AndM1Heater11_GetsMetalBrush()
    {
        var roles = RolesFor("siepic-ebeam-pdk.json");

        GdsPreviewLayerStyle.BrushFor(SiepicCoreLayer, roles)
            .ShouldBeSameAs(GdsPreviewLayerStyle.BrushFor(1, GdsPreviewLayerRoles.Default));
        GdsPreviewLayerStyle.BrushFor(SiepicHeaterLayer, roles)
            .ShouldBeSameAs(GdsPreviewLayerStyle.BrushFor(10, GdsPreviewLayerRoles.Default));
        // Under the demo roles the SiEPIC heater on 11 was not copper — that was the bug.
        GdsPreviewLayerStyle.BrushFor(SiepicHeaterLayer, GdsPreviewLayerRoles.Default)
            .ShouldNotBeSameAs(GdsPreviewLayerStyle.BrushFor(SiepicHeaterLayer, roles));
    }

    [Fact]
    public void NoProcess_FallsBackToDemoRoles()
    {
        var roles = GdsPreviewLayerRoles.FromProcess(null);

        roles.Signature.ShouldBe(GdsPreviewLayerRoles.Default.Signature);
    }

    [Fact]
    public void DemoPdk_KeepsTodaysBehaviour()
    {
        // The demo PDK declares only a metal cross-section — no optical core — so it falls
        // back to the hardwired demo roles (cores 1/3, metal 10) instead of losing them.
        RolesFor("demo-pdk.json").Signature.ShouldBe(GdsPreviewLayerRoles.Default.Signature);
    }

    [Fact]
    public void DifferentProcesses_ProduceDifferentRoleSignatures()
    {
        var cornerstone = RolesFor("cornerstone-sin-pdk.json");
        var siepic = RolesFor("siepic-ebeam-pdk.json");

        cornerstone.Signature.ShouldNotBe(siepic.Signature);
        cornerstone.Signature.ShouldNotBe(GdsPreviewLayerRoles.Default.Signature);
    }

    private static NazcaPreviewResult Cell(params NazcaPreviewPolygon[] polygons) => new()
    {
        Success = true,
        XMin = 0,
        YMin = 0,
        XMax = 100,
        YMax = 20,
        Polygons = polygons,
    };

    private static NazcaPreviewPolygon Rect(int layer, double x0, double y0, double x1, double y1) => new()
    {
        Layer = layer,
        Vertices = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) },
    };
}
