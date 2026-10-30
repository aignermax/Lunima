using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Hierarchy import results, invalid inputs, and direct-instance conservation.</summary>
public class GdsHierarchyImporterTests
{
    [Fact]
    public async Task Explode_TwoAbuttingWaveguides_YieldsDraftsInstancesAndOneConnection()
    {
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("wgB", 10000, 0)
            .EndCell()
            .WaveguideCell("wgA")
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.Mode.ShouldBe(GdsHierarchyImportMode.ExplodeHierarchy);
        result.TopCellName.ShouldBe("TOP");
        result.BoundingBox.MaxX.ShouldBe(20, Tolerance);
        result.BoundingBox.MaxY.ShouldBe(4, Tolerance);
        result.Warnings.ShouldBeEmpty();

        // Two drafts, in order of first appearance.
        result.ImportedCellDrafts.Count.ShouldBe(2);
        var wgA = result.ImportedCellDrafts[0];
        wgA.CellName.ShouldBe("wgA");
        wgA.WidthUm.ShouldBe(10, Tolerance);
        wgA.HeightUm.ShouldBe(4, Tolerance);
        wgA.Pins.Count.ShouldBe(2);
        wgA.Pins[0].Name.ShouldBe("in"); // left edge sorts first
        wgA.Pins[0].XUm.ShouldBe(0, Tolerance);
        wgA.Pins[0].YUm.ShouldBe(2, Tolerance);
        wgA.Pins[0].AngleDegrees.ShouldBe(180, Tolerance);
        wgA.Pins[1].Name.ShouldBe("out");
        wgA.Pins[1].XUm.ShouldBe(10, Tolerance);
        wgA.Pins[1].AngleDegrees.ShouldBe(0, Tolerance);
        AssertPinsWithinDraftBounds(wgA);
        AssertPinsWithinDraftBounds(result.ImportedCellDrafts[1]);

        // RawCode round-trip snippet with the file-name token: the loaded cell is
        // re-anchored to its bbox bottom-left (the app-space top-left origin), and
        // topcellsonly=False because the imported cell is a SUBcell of TOP.
        wgA.RawCodeBackend.ShouldBe("nazca");
        wgA.RawCode.ShouldContain("def component():");
        wgA.RawCode.ShouldContain("nd.load_gds(filename=\"{GdsFileName}\", cellname=\"wgA\", topcellsonly=False)");
        wgA.RawCode.ShouldContain("_loaded.put(-_bb[0], -_bb[1])");

        // Two instances at app-space positions.
        result.Instances.Count.ShouldBe(2);
        result.Instances[0].InstanceName.ShouldBe("wgA#0");
        result.Instances[0].CellDraftName.ShouldBe("wgA");
        result.Instances[0].KnownComponentIdentifier.ShouldBeNull();
        result.Instances[0].PositionXUm.ShouldBe(0, Tolerance);
        result.Instances[0].PositionYUm.ShouldBe(0, Tolerance);
        result.Instances[0].RotationDegrees.ShouldBe(0, Tolerance);
        result.Instances[0].Reflected.ShouldBeFalse();
        result.Instances[1].PositionXUm.ShouldBe(10, Tolerance);
        result.Instances[1].PositionYUm.ShouldBe(0, Tolerance);

        // Exactly one connection: wgA.out ↔ wgB.in at (10, 2).
        var connection = result.Connections.ShouldHaveSingleItem();
        connection.A.InstanceIndex.ShouldBe(0);
        connection.A.PinName.ShouldBe("out");
        connection.B.InstanceIndex.ShouldBe(1);
        connection.B.PinName.ShouldBe("in");
        connection.XUm.ShouldBe(10, Tolerance);
        connection.YUm.ShouldBe(2, Tolerance);
    }

    [Fact]
    public async Task ImportAsync_UnknownTopCell_ThrowsInvalidData()
    {
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP").EndCell()
            .EndLibrary()
            .ToArray());

        await Should.ThrowAsync<InvalidDataException>(
            () => GdsHierarchyImporter.ImportAsync(library, "MISSING", new GdsHierarchyImportOptions()));
    }

    [Fact]
    public async Task ImportAsync_EmptyLibrary_ThrowsInvalidData()
    {
        await Should.ThrowAsync<InvalidDataException>(
            () => GdsHierarchyImporter.ImportAsync(new GdsLibrary(), "TOP", new GdsHierarchyImportOptions()));
    }

    [Fact]
    public async Task Explode_TopCellWithoutInstances_WarnsNothingToExplode()
    {
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .Boundary(1, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.ImportedCellDrafts.ShouldBeEmpty();
        result.Instances.ShouldBeEmpty();
        result.Connections.ShouldBeEmpty();
        result.Warnings.ShouldContain(w => w.Contains("nothing to explode"));
        // The lone (1,0) polygon is top-cell own geometry on a waveguide layer:
        // it comes back as a frozen path — an INFO, not a warning (nothing is
        // silently dropped: the frozen path stays visible on the group).
        result.Infos.ShouldContain(i => i.Contains("imported as frozen paths (not re-routable)"));
        var polygon = result.TopCellWaveguidePolygons.ShouldHaveSingleItem();
        polygon.Layer.ShouldBe(1);
        polygon.Points.Select(p => (p.X, p.Y)).ShouldBe(new[]
        {
            (0.0, 4.0), (10.0, 4.0), (10.0, 0.0), (0.0, 0.0), (0.0, 4.0),
        });
    }

    [Fact]
    public async Task Explode_EveryDirectInstanceIsImportedOrExplicitlySkipped()
    {
        var gds = GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("WG", 0, 0)
                .SRef("WG", 0, 20000)
                .ARef("WG", columns: 2, rows: 2, originX: 40000, originY: 0,
                    columnSpacingDbUnits: 20000, rowSpacingDbUnits: 20000)
                .SRef("EMPTY", 100000, 0)
                .SRef("EMPTY", 120000, 0)
            .EndCell()
            .WaveguideCell("WG")
            .BeginCell("EMPTY").EndCell()
            .EndLibrary()
            .ToArray();
        var library = await new GdsReader().ReadAsync(new MemoryStream(gds));

        var circuit = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        int directInstances = library.Cells["TOP"].Elements
            .OfType<GdsReference>()
            .Sum(r => r.Rows * r.Columns);
        directInstances.ShouldBe(8, "fixture: 2 SREFs + 2×2 AREF of WG, 2 SREFs of EMPTY");

        circuit.Instances.Count.ShouldBe(6, "the six WG instances all import");
        var skipNote = circuit.Infos.Single(i => i.Contains("has no geometry", StringComparison.Ordinal));
        skipNote.ShouldContain("'EMPTY'");
        skipNote.ShouldContain("2 instance(s)");

        // The invariant itself: imported + explicitly skipped == everything the GDS placed.
        (circuit.Instances.Count + 2).ShouldBe(directInstances);
    }
}
