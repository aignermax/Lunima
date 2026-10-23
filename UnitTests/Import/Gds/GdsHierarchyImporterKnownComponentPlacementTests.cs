using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Pin-anchored placement and diagnostics for known-component geometry mismatches.</summary>
public class GdsHierarchyImporterKnownComponentPlacementTests
{
    [Fact]
    public async Task Explode_KnownComponentSizeMismatch_WarnsPinsUnscaledAndConnectionsUncertain()
    {
        // PDK says 30×10 µm but the GDS cell measures 60×10 µm: pins are mapped
        // unscaled onto the GDS bbox, so reconstructed connections may be wrong —
        // the warning must say exactly that.
        var known = new KnownComponent(
            "mmi1x2", "testpdk", 30, 10,
            new[]
            {
                Pin("o1", 0, 5, 180),
                Pin("o2", 30, 5, 0),
            });

        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("mmi1x2", 0, 0)
            .EndCell()
            .BeginCell("mmi1x2")
                .Boundary(1, 0, (0, 0), (60000, 0), (60000, 10000), (0, 10000), (0, 0))
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ResolveKnownComponent = name => name == "mmi1x2" ? known : null,
            });

        var warning = result.Warnings.First(w => w.Contains("UNSCALED"));
        warning.ShouldContain("geometrically incorrect");
    }

    [Fact]
    public async Task Explode_KnownComponentWithMarkerInflatedBBox_PlacesPinAnchoredWithoutWarning()
    {
        // The SiEPIC bond-pad shape (issue #811): the foundry cell's m_pin
        // marker paths inflate the cell bbox to 115.2×115.2 µm while the real
        // pad is the template's 100×100 and the restored 'elec' label sits at
        // the TRUE pin position (50, 50) in pad coordinates — (57.6, 57.6) in
        // the inflated bbox frame. The pins are authoritative: the placement
        // must follow the label, not the bbox (bbox-top-left mapping would
        // place the pad 7.6 µm off), and the pure bbox inflation must NOT fire
        // the size-mismatch warning.
        var known = new KnownComponent(
            "pad", "testpdk", 100, 100,
            new[] { Pin("elec", 50, 50, 0) });

        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("pad", 0, 0)
                .SRef("probe", 50000, 48000)
            .EndCell()
            .BeginCell("pad")
                // The real pad metal (11,0) at the true 100×100 box…
                .Boundary(11, 0, (0, 0), (100000, 0), (100000, 100000), (0, 100000), (0, 0))
                // …and the marker frame (non-waveguide layer) inflating the bbox to 115.2 µm.
                .Boundary(111, 0, (-7600, -7600), (107600, -7600), (107600, 107600), (-7600, 107600), (-7600, -7600))
                .Text(1, 10, "elec", 50000, 50000)
            .EndCell()
            .WaveguideCell("probe")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ResolveKnownComponent = name => name == "pad" ? known : null,
            });

        // Top bbox = the inflated pad bbox (−7.6,−7.6)–(107.6,107.6): the pad's
        // TRUE box lands at app (7.6, 7.6) — the bbox mapping would say (0, 0).
        var pad = result.Instances.Single(i => i.CellName == "pad");
        pad.PositionXUm.ShouldBe(7.6, Tolerance);
        pad.PositionYUm.ShouldBe(7.6, Tolerance);
        result.Warnings.ShouldBeEmpty(
            "marker-inflated bbox with matching pins is benign — no size warning");

        // The projected template pin sits at the label's TRUE position, so the
        // abutting probe pin reconstructs a connection (previously the pad's
        // pin was projected 7.6 µm off and missed).
        var connection = result.Connections.ShouldHaveSingleItem();
        connection.A.PinName.ShouldBe("elec");
        connection.B.PinName.ShouldBe("in");
        connection.XUm.ShouldBe(57.6, Tolerance);
        connection.YUm.ShouldBe(57.6, Tolerance);
    }

    [Fact]
    public async Task Explode_KnownComponentWithMismatchedPinLabels_WarnsButPlacesPinAnchored()
    {
        // A genuine pin-layout mismatch (the cell's pins are NOT a rigid
        // translation of the template's — here 40 µm apart vs the template's
        // 30): the pins still anchor the placement (best fit — the mean
        // translation), but the deviation earns ONE warning per cell.
        var known = new KnownComponent(
            "mmi", "testpdk", 30, 10,
            new[]
            {
                Pin("o1", 0, 5, 180),
                Pin("o2", 30, 5, 0),
            });

        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("mmi", 0, 0)
                .SRef("mmi", 100000, 0)
            .EndCell()
            .BeginCell("mmi")
                .Boundary(111, 0, (0, 0), (40000, 0), (40000, 10000), (0, 10000), (0, 0))
                .Text(1, 10, "o1", 0, 5000)
                .Text(1, 10, "o2", 40000, 5000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ResolveKnownComponent = name => name == "mmi" ? known : null,
            });

        // Per-pin deltas (0,0) and (10,0) → mean (5,0), deviation 5 µm each.
        var warning = result.Warnings.ShouldHaveSingleItem(
            "two instances of the same cell collapse into ONE pin-mismatch warning");
        warning.ShouldContain("'mmi'");
        warning.ShouldContain("pin layout");
        warning.ShouldContain("5");
        warning.ShouldContain("geometrically incorrect");

        // Best-fit placement: the template's 30×10 box shifted by the mean
        // delta (5, 0) — instance 2 adds its GDS offset (100, 0).
        result.Instances[0].PositionXUm.ShouldBe(5, Tolerance);
        result.Instances[0].PositionYUm.ShouldBe(0, Tolerance);
        result.Instances[1].PositionXUm.ShouldBe(105, Tolerance);
        result.Instances[1].PositionYUm.ShouldBe(0, Tolerance);
    }
}
