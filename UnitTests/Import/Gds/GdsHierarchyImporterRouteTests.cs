using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Top-level waveguide geometry and route-derived connections.</summary>
public class GdsHierarchyImporterRouteTests
{
    [Fact]
    public async Task Explode_TopCellOwnWaveguidePolygon_ImportedWithPinnedCoordinates()
    {
        // The route polygon spanning the 5 µm gap between wgA (ends at x=10) and
        // wgB (starts at x=15) is the top cell's OWN geometry on the waveguide
        // layer — it must import, in app space (Y-down, origin at the top bbox
        // top-left (0, 4)). The wgA/wgB core stripes sit on the same (1,0) layer
        // but belong to the INSTANCES — importing them here too would double-draw
        // every component's waveguide, so exactly ONE polygon comes back.
        //
        // The polygon runs at app y ∈ [3.25, 3.75] (GDS y 250…750), 1.25 µm clear
        // of the pin line: the pin labels sit at GDS y=2000 → app y=2 (wgA.out at
        // (10, 2), wgB.in at (15, 2)), and the route-connectivity touch tolerance
        // is 1.0 µm — 1.25 µm of clearance keeps the polygon pin-less, so it
        // stays a FROZEN PATH instead of being consumed as a connection.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("wgB", 15000, 0)
                .Boundary(1, 0, (10000, 250), (15000, 250), (15000, 750), (10000, 750), (10000, 250))
                .Boundary(68, 0, (0, 0), (25000, 0), (25000, 4000), (0, 4000), (0, 0)) // devrec halo
            .EndCell()
            .WaveguideCell("wgA")
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        var polygon = result.TopCellWaveguidePolygons.ShouldHaveSingleItem(
            "only the top cell's OWN (1,0) polygon — instance geometry stays out");
        polygon.Layer.ShouldBe(1);
        polygon.DataType.ShouldBe(0);
        polygon.Points.Select(p => (p.X, p.Y)).ShouldBe(new[]
        {
            (10.0, 3.75), (15.0, 3.75), (15.0, 3.25), (10.0, 3.25), (10.0, 3.75),
        });

        // Restored/frozen accounting is INFO (fully reconstructed geometry);
        // the devrec halo — on neither the route nor the metal layers — comes
        // back as render-only background geometry (also INFO, not a warning).
        result.Infos.ShouldContain(i => i.Contains("imported as frozen paths (not re-routable)"));
        result.Infos.ShouldContain(i => i.Contains("render-only background geometry"));
        result.Warnings.ShouldBeEmpty();
        var residual = result.TopCellResidualPolygons.ShouldHaveSingleItem();
        residual.Layer.ShouldBe(68);
        residual.Points.Select(p => (p.X, p.Y)).ShouldBe(new[]
        {
            (0.0, 4.0), (25.0, 4.0), (25.0, 0.0), (0.0, 0.0), (0.0, 4.0),
        });
    }

    [Fact]
    public async Task Explode_TopCellOwnWaveguidePolygon_TouchingTwoPins_BecomesRouteDerivedConnection()
    {
        // The SAME geometry as the frozen-path test, but with the polygon ON the
        // pin line: it bridges the 5 µm gap between wgA.out (app (10, 2)) and
        // wgB.in (app (15, 2)) — its end edges pass exactly through both pins.
        // The drawn route IS the connectivity, so the polygon is consumed into a
        // real (re-routable) connection instead of coming back as a frozen path.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("wgB", 15000, 0)
                .Boundary(1, 0, (10000, 1750), (15000, 1750), (15000, 2250), (10000, 2250), (10000, 1750))
                .Boundary(68, 0, (0, 0), (25000, 0), (25000, 4000), (0, 4000), (0, 0)) // devrec halo
            .EndCell()
            .WaveguideCell("wgA")
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.TopCellWaveguidePolygons.ShouldBeEmpty(
            "the bridging polygon was consumed into a route-derived connection");
        var connection = result.Connections.ShouldHaveSingleItem();
        connection.IsRouteDerived.ShouldBeTrue();
        connection.A.InstanceIndex.ShouldBe(0);
        connection.A.PinName.ShouldBe("out");
        connection.B.InstanceIndex.ShouldBe(1);
        connection.B.PinName.ShouldBe("in");
        connection.XUm.ShouldBe(12.5, Tolerance);
        connection.YUm.ShouldBe(2.0, Tolerance);

        // The restored connection is INFO (normal, fully-reconstructed
        // behavior); the devrec halo comes back as background geometry (INFO).
        result.Infos.ShouldContain(i => i.Contains("restored as 1 real connection(s) (re-routable)"));
        result.Infos.ShouldContain(i => i.Contains("render-only background geometry"));
        result.Warnings.ShouldBeEmpty();
        result.TopCellResidualPolygons.ShouldHaveSingleItem().Layer.ShouldBe(68);
    }

    [Fact]
    public async Task Explode_TopCellOwnWaveguidePath_TouchingTwoPins_BecomesRouteDerivedConnection()
    {
        // The same bridge as the polygon test above, but drawn the way real
        // PDK exports draw most routing: a PATH (centerline + width) instead
        // of a BOUNDARY. The 0.5 µm wide centerline runs on the pin line from
        // wgA.out (app (10, 2)) to wgB.in (app (15, 2)); its outline quad is
        // geometrically identical to the polygon fixture, so the route matcher
        // must consume it into the same route-derived connection.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("wgB", 15000, 0)
                .Path(1, 0, widthDbUnits: 500, pathType: 0, (10000, 2000), (15000, 2000))
                .Boundary(68, 0, (0, 0), (25000, 0), (25000, 4000), (0, 4000), (0, 0)) // devrec halo
            .EndCell()
            .WaveguideCell("wgA")
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.TopCellWaveguidePolygons.ShouldBeEmpty(
            "the path's outline quad was consumed into a route-derived connection");
        var connection = result.Connections.ShouldHaveSingleItem();
        connection.IsRouteDerived.ShouldBeTrue();
        connection.A.InstanceIndex.ShouldBe(0);
        connection.A.PinName.ShouldBe("out");
        connection.B.InstanceIndex.ShouldBe(1);
        connection.B.PinName.ShouldBe("in");
        connection.XUm.ShouldBe(12.5, Tolerance);
        connection.YUm.ShouldBe(2.0, Tolerance);

        result.Infos.ShouldContain(i => i.Contains("restored as 1 real connection(s) (re-routable)"));
        result.Infos.ShouldContain(i => i.Contains("render-only background geometry"));
        result.Warnings.ShouldBeEmpty();
        result.TopCellResidualPolygons.ShouldHaveSingleItem().Layer.ShouldBe(68);
    }

    [Fact]
    public async Task Explode_TopCellRoutePolygon_WithOwnExportPortLabelOnPin_StillBecomesRouteDerivedConnection()
    {
        // The whole-layout export stamps a top-cell port label exactly on every
        // coupler pin. Re-importing that file makes the route network read as a
        // 3-pin junction (both real pins + the label) unless the coincident
        // label is recognized as the SAME joint — the coupler-terminated route
        // must still become a real connection, and the label must stay an
        // unconsumed external port.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("wgB", 15000, 0)
                .Boundary(1, 0, (10000, 1750), (15000, 1750), (15000, 2250), (10000, 2250), (10000, 1750))
                .Text(1, 10, "wgc_out", 10000, 2000) // own-export port label exactly on wgA.out
            .EndCell()
            .WaveguideCell("wgA")
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.TopCellWaveguidePolygons.ShouldBeEmpty(
            "the label is the same joint as the pin it sits on, not a third junction party");
        var connection = result.Connections.ShouldHaveSingleItem();
        connection.IsRouteDerived.ShouldBeTrue();
        connection.A.InstanceIndex.ShouldBe(0);
        connection.A.PinName.ShouldBe("out");
        connection.B.InstanceIndex.ShouldBe(1);
        connection.B.PinName.ShouldBe("in");
        result.Infos.ShouldNotContain(i => i.Contains("junction"),
            "a port label coincident with a touched pin is not junction topology");
        result.Infos.ShouldContain(i => i.Contains("restored as 1 real connection(s) (re-routable)"));
        result.Warnings.ShouldBeEmpty();
    }
}
