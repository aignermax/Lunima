using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Configured route layers and bounded render-only residual geometry.</summary>
public class GdsHierarchyImporterResidualGeometryTests
{
    [Fact]
    public async Task Explode_TopCellOwnGeometryOnlyOnNonWaveguideLayers_BecomesBackgroundGeometry()
    {
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .Boundary(68, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0)) // devrec only
            .EndCell()
            .WaveguideCell("wgA")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.TopCellWaveguidePolygons.ShouldBeEmpty("a devrec halo is not routing geometry");
        // The halo is not lost: it rides the created group as render-only
        // background geometry — an INFO, not the stale "not reconstructed" warning.
        result.Infos.ShouldContain(i =>
            i.Contains("1 polygon(s) on other layers are imported as render-only background geometry"));
        result.Warnings.ShouldBeEmpty();
        result.TopCellResidualPolygons.ShouldHaveSingleItem().Layer.ShouldBe(68);
    }

    [Fact]
    public async Task Explode_NazcaDefaultInterconnectPolygon_ImportedByDefault()
    {
        // Our OWN exporter flattens routed connections with nazca's default
        // interconnect (nd.strt/nd.bend), which writes polygons on layer
        // (1111,0) — a re-import must recognize it without any option tuning.
        // The polygon is drawn OFF the pins (1.5 µm clear of the 1 µm touch
        // tolerance): a polygon touching exactly two pins of ONE instance is a
        // feedback loop and becomes a route-derived connection instead of a
        // frozen path — not what this layer-recognition test is about.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .Boundary(1111, 0, (0, 3500), (10000, 3500), (10000, 4000), (0, 4000), (0, 3500))
            .EndCell()
            .WaveguideCell("wgA")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.TopCellWaveguidePolygons.ShouldHaveSingleItem().Layer.ShouldBe(1111);
        result.Infos.ShouldContain(i => i.Contains("imported as frozen paths (not re-routable)"));
        result.Warnings.ShouldBeEmpty("the (1111,0) polygon is fully reconstructed as a frozen path — no leftover geometry");
    }

    [Fact]
    public async Task Explode_CustomRouteLayers_ImportsOnlyTheConfiguredLayer()
    {
        // The route-layer list is configurable (GdsHierarchyImportOptions.RouteLayers);
        // with (3,0) configured the (1,0) polygon is NOT routing anymore.
        // The (3,0) polygon is drawn 1.5 µm clear of the pins (touch tolerance
        // is 1 µm): touching exactly two pins of ONE instance would make it a
        // route-derived feedback-loop connection, not the frozen path this
        // layer-filtering test asserts.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .Boundary(3, 0, (0, 0), (10000, 0), (10000, 500), (0, 500), (0, 0))
                .Boundary(1, 0, (0, 3000), (10000, 3000), (10000, 3500), (0, 3500), (0, 3000))
            .EndCell()
            .WaveguideCell("wgA")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions
        {
            RouteLayers = [(3, 0)],
        });

        var polygon = result.TopCellWaveguidePolygons.ShouldHaveSingleItem();
        polygon.Layer.ShouldBe(3);
        polygon.Points.Select(p => (p.X, p.Y)).ShouldBe(new[]
        {
            (0.0, 4.0), (10.0, 4.0), (10.0, 3.5), (0.0, 3.5), (0.0, 4.0),
        });
        // The (3,0) polygon comes back as a frozen path (INFO); the (1,0)
        // polygon is on no configured route layer — render-only background (INFO).
        result.Infos.ShouldContain(i => i.Contains("imported as frozen paths (not re-routable)"));
        result.Infos.ShouldContain(i => i.Contains("render-only background geometry"));
        result.Warnings.ShouldBeEmpty();
        result.TopCellResidualPolygons.ShouldHaveSingleItem().Layer.ShouldBe(1);
    }

    [Fact]
    public async Task Explode_ResidualPolygonsOverOutlineCap_DroppedWithTrueCountWarning()
    {
        // 2500 tiny (68,0) squares = 12500 outline points against an 8000-point
        // cap (set explicitly so the test does not depend on the default's
        // value). A square never simplifies below its 5 ring points (and must
        // not silently vanish when the escalated tolerance overshoots it), so
        // the cap is enforced by counted drops only: 1600 × 5 = 8000 points
        // exactly fill the cap, 900 are dropped, and the warning reports that
        // true count.
        var writer = GdsTestWriter.Create().StandardPrologue().BeginCell("TOP");
        for (int i = 0; i < 2500; i++)
        {
            int x = (i % 50) * 1000;
            int y = (i / 50) * 1000;
            writer.Boundary(68, 0, (x, y), (x + 500, y), (x + 500, y + 500), (x, y + 500), (x, y));
        }
        var library = await ReadLibraryAsync(writer.EndCell().EndLibrary().ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP",
            new GdsHierarchyImportOptions { MaxOutlinePointsPerCell = 8000 });

        result.TopCellResidualPolygons.Count.ShouldBe(1600,
            "1600 squares × 5 points exactly fill the 8000-point cap");
        result.Warnings.ShouldContain(w => w.Contains("dropped 900 background polygon(s)"),
            "the warning reports the true dropped count (2500 − 1600 kept)");
        result.Infos.ShouldContain(i => i.Contains("render-only background geometry"));
    }
}
