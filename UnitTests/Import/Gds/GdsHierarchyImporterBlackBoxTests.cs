using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Flattened black-box drafts, geometry, labels, and sanity diagnostics.</summary>
public class GdsHierarchyImporterBlackBoxTests
{
    [Fact]
    public async Task BlackBox_TopCell_BecomesSingleDraftWithoutInstances()
    {
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("wgB", 10000, 0)
                .Text(1, 10, "a0", 0, 2000)
                .Text(1, 10, "a1", 20000, 2000)
            .EndCell()
            .WaveguideCell("wgA")
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions { Mode = GdsHierarchyImportMode.BlackBox });

        result.Mode.ShouldBe(GdsHierarchyImportMode.BlackBox);
        result.Instances.ShouldBeEmpty();
        result.Connections.ShouldBeEmpty();

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.CellName.ShouldBe("TOP");
        draft.WidthUm.ShouldBe(20, Tolerance);
        draft.HeightUm.ShouldBe(4, Tolerance);

        // Black-box pins come from the WHOLE flattened hierarchy: the top cell's
        // own labels keep their bare names (they are the circuit's ports), the
        // absorbed children's labels are promoted with their cell context
        // (each of wgA/wgB occurs once, so no occurrence qualifier). Detector
        // order: left edge top-down, then top edge, then right edge.
        draft.Pins.Select(p => p.Name).ShouldBe(new[]
        {
            "wgA_in", "a0", "wgA_out", "wgB_in", "wgB_out", "a1",
        });
        AssertPinsWithinDraftBounds(draft);

        // Outlines absorb the whole hierarchy (stripe + extent per child).
        draft.Outlines.Count.ShouldBe(4);
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task BlackBox_NestedLabels_InferDirectionAndKindFromLocalGeometry()
    {
        // The user's black-box case: a 100 × 80 µm top cell absorbing one device
        // cell whose pins sit DEEP inside the bbox. The metal pad pin must point
        // along the pad's end face (the bbox edge rule would point it at the
        // outer left edge) and read electrical from the metal layer alone ("m1"
        // matches no electrical name); the waveguide pin stays optical; the top
        // cell's own "anode" label has no geometry near and falls back to the
        // bbox direction plus the name-based kind.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("dev", 20000, 30000)
                .Boundary(111, 0, (0, 0), (100000, 0), (100000, 80000), (0, 80000), (0, 0))
                .Text(1, 10, "anode", 50000, 50000)
            .EndCell()
            .BeginCell("dev")
                .Boundary(11, 0, (6000, 2750), (10000, 2750), (10000, 3250), (6000, 3250), (6000, 2750))
                .Boundary(1, 0, (0, 750), (4000, 750), (4000, 1250), (0, 1250), (0, 750))
                .Text(1, 10, "m1", 10000, 3000)
                .Text(1, 10, "o1", 0, 1000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions { Mode = GdsHierarchyImportMode.BlackBox });

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        result.Warnings.ShouldBeEmpty();
        draft.Pins.Count.ShouldBe(3);

        // dev_m1: on the pad's right end face → east (the bbox rule would say
        // 180°, left edge); electrical from the metal polygon touch.
        var metal = draft.Pins.Single(p => p.Name == "dev_m1");
        metal.XUm.ShouldBe(30, Tolerance);
        metal.YUm.ShouldBe(47, Tolerance);
        metal.AngleDegrees.ShouldBe(0, Tolerance);
        metal.IsElectrical.ShouldBe(true);

        // dev_o1: on the waveguide's left end face → west, kind stays unknown
        // (optical downstream).
        var optical = draft.Pins.Single(p => p.Name == "dev_o1");
        optical.XUm.ShouldBe(20, Tolerance);
        optical.YUm.ShouldBe(49, Tolerance);
        optical.AngleDegrees.ShouldBe(180, Tolerance);
        optical.IsElectrical.ShouldBeNull();

        // anode: no geometry near → bbox fallback direction (top edge), kind
        // from the name.
        var named = draft.Pins.Single(p => p.Name == "anode");
        named.XUm.ShouldBe(50, Tolerance);
        named.YUm.ShouldBe(30, Tolerance);
        named.AngleDegrees.ShouldBe(270, Tolerance);
        named.IsElectrical.ShouldBe(true);

        AssertPinsWithinDraftBounds(draft);
    }

    [Fact]
    public async Task BlackBox_Outline_SimplifiedUnderPointCap_LayerKept_YDown()
    {
        // 72-gon "circle" (radius 5 µm) on layer 3 plus a bbox rectangle on
        // layer 1; point cap 25 forces adaptive tolerance growth.
        var circlePoints = Enumerable.Range(0, 72)
            .Select(i =>
            {
                double angle = 2 * Math.PI * i / 72;
                return ((int)Math.Round(5000 + 5000 * Math.Cos(angle)),
                        (int)Math.Round(5000 + 5000 * Math.Sin(angle)));
            })
            .Append((10000, 5000)) // close the ring
            .ToArray();

        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .Boundary(3, 0, circlePoints)
                .Boundary(1, 0, (0, 0), (10000, 0), (10000, 10000), (0, 10000), (0, 0))
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                Mode = GdsHierarchyImportMode.BlackBox,
                MaxOutlinePointsPerCell = 25,
            });

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.Outlines.Count.ShouldBe(2);
        draft.Outlines.Sum(p => p.Points.Count).ShouldBeLessThanOrEqualTo(25);

        // Layer/datatype survive simplification.
        draft.Outlines.ShouldContain(p => p.Layer == 3 && p.DataType == 0);
        draft.Outlines.ShouldContain(p => p.Layer == 1 && p.DataType == 0);

        // App-space Y-down: the rectangle's top edge (GDS MaxY) maps to y = 0.
        var rectangle = draft.Outlines.First(p => p.Layer == 1);
        rectangle.Points.ShouldContain(pt => pt.Y == 0 && pt.X == 0);
        rectangle.Points.ShouldContain(pt => pt.Y == 0 && pt.X == 10);
        rectangle.Points.Min(pt => pt.Y).ShouldBe(0, Tolerance);
        rectangle.Points.Max(pt => pt.Y).ShouldBe(10, Tolerance);
    }

    [Fact]
    public async Task BlackBox_MultiLineMetadataLabel_DoesNotBecomePin()
    {
        // Black-box mode promotes every nested label to a context-prefixed pin —
        // the same multi-line filter must apply before prefixing.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("dev", 0, 0)
            .EndCell()
            .BeginCell("dev")
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Text(1, 10, "o1", 0, 2000)
                .Text(1, 10, "cellname: dev\nfoundry_pdk: test_pdk", 10000, 2000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions { Mode = GdsHierarchyImportMode.BlackBox });

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.Pins.Select(p => p.Name).ShouldContain("dev_o1");
        draft.Pins.ShouldNotContain(p => p.Name.Contains('\n'),
            "the multi-line metadata blob must not become a black-box pin");
    }

    [Fact]
    public async Task BlackBox_LabelFloodExceedingPinSanityThreshold_Warns()
    {
        // Field observation (issue #829): a black-box import of a large top cell
        // persisted ~9800 pins because every text label became a pin. Past the
        // sanity threshold the import must warn (it still registers the pins).
        var writer = GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .Boundary(1, 0, (0, 0), (600000, 0), (600000, 1000), (0, 1000), (0, 0));
        for (var i = 0; i < 600; i++)
            writer.Text(1, 10, $"p{i}", i * 1000, 500);
        var library = await ReadLibraryAsync(writer.EndCell().EndLibrary().ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions { Mode = GdsHierarchyImportMode.BlackBox });

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.Pins.Count.ShouldBeGreaterThan(512);
        result.Warnings.ShouldContain(w =>
            w.Contains("Black-box import of 'TOP'") && w.Contains("explode mode"));
    }
}
