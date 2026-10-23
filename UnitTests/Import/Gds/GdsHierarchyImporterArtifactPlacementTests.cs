using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Device placement and composed transforms through exported artifact wrappers.</summary>
public class GdsHierarchyImporterArtifactPlacementTests
{
    [Fact]
    public async Task Explode_ArtifactPartialWithDeviceReferences_DevicesJoinPlacementSet()
    {
        // The mixed-backend reality: the partial is NOT flattened — its device
        // cells are real references with port labels. The import recurses ONE
        // level: the devices join the placement set as if they were top-level
        // instances (transforms composed through the partial's own transform),
        // pin detection applies, and the old skip note disappears. The AREF
        // inside the partial expands per member.
        // dev: 5×4 µm, built like WaveguideCell — a (1,0) core stripe inside a
        // (111,0) extent rectangle (which sizes the bbox without firing the
        // edge heuristic), labels opt1/opt2 on the left/right edge midpoints.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("ConnectAPIC_NazcaPartial", 10000, 0)
            .EndCell()
            .WaveguideCell("wgA")
            .BeginCell("ConnectAPIC_NazcaPartial")
                .SRef("dev", 0, 0)
                .ARef("dev", columns: 2, rows: 1, originX: 20000, originY: 0,
                    columnSpacingDbUnits: 10000, rowSpacingDbUnits: 5000)
            .EndCell()
            .BeginCell("dev")
                .Boundary(1, 0, (0, 1750), (5000, 1750), (5000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (5000, 0), (5000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "opt1", 0, 2000)
                .Text(1, 10, "opt2", 5000, 2000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        // One draft per distinct cell: wgA and dev — never the partial itself.
        result.ImportedCellDrafts.Select(d => d.CellName).ShouldBe(new[] { "wgA", "dev" });
        var dev = result.ImportedCellDrafts[1];
        dev.WidthUm.ShouldBe(5, Tolerance);
        dev.HeightUm.ShouldBe(4, Tolerance);
        dev.Pins.Select(p => p.Name).ShouldBe(new[] { "opt1", "opt2" });

        // The partial's three device references (one SREF + two AREF members)
        // are placed at their composed offsets: partial (10, 0) + child offset.
        // Top bbox: MinX 0, MaxY 4 (all cells share the 4 µm extent) → the
        // placed bbox top-left of a dev at origin (x, 0) is app (x, 0).
        result.Instances.Select(i => i.InstanceName).ShouldBe(
            new[] { "wgA#0", "dev#0", "dev#1", "dev#2" });
        var dev0 = result.Instances[1];
        dev0.CellDraftName.ShouldBe("dev");
        dev0.PositionXUm.ShouldBe(10, Tolerance);
        dev0.PositionYUm.ShouldBe(0, Tolerance);
        dev0.RotationDegrees.ShouldBe(0, Tolerance);
        result.Instances[2].PositionXUm.ShouldBe(30, Tolerance);
        result.Instances[3].PositionXUm.ShouldBe(40, Tolerance);

        result.Warnings.ShouldBeEmpty();
        result.Infos.ShouldBeEmpty("an expanded partial produces no artifact-skip note");

        // wgA.out (10, 2) coincides with dev#0.opt1 — the device pin projected
        // through the composed transform takes part in abutment matching.
        var connection = result.Connections.ShouldHaveSingleItem();
        connection.A.PinName.ShouldBe("out");
        connection.B.PinName.ShouldBe("opt1");
        connection.XUm.ShouldBe(10, Tolerance);
        connection.YUm.ShouldBe(2, Tolerance);
    }

    [Fact]
    public async Task Explode_WrappedArtifactPartial_TransformsComposeThroughWrapperChain()
    {
        // The documented mixed-backend shape: top → 'nazca' pass-through
        // wrapper → partial → devices. Offsets on every level compose (the
        // wrapper instance at (6, 0), the wrapper→partial reference at (3, 0),
        // the partial→dev reference at (1, 0) → dev origin (10, 0) GDS).
        // The device resolves to a KNOWN component by name — the resolver
        // applies to expanded devices exactly like to top-level instances.
        var known = new KnownComponent(
            "dev", "testpdk", 5, 4,
            new[] { Pin("opt1", 0, 2, 180), Pin("opt2", 5, 2, 0) });

        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("nazca", 6000, 0)
            .EndCell()
            .WaveguideCell("wgA")
            .BeginCell("nazca")
                .SRef("ConnectAPIC_NazcaPartial", 3000, 0)
            .EndCell()
            .BeginCell("ConnectAPIC_NazcaPartial")
                .SRef("dev", 1000, 0)
            .EndCell()
            .BeginCell("dev")
                .Boundary(1, 0, (0, 1750), (5000, 1750), (5000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (5000, 0), (5000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "opt1", 0, 2000)
                .Text(1, 10, "opt2", 5000, 2000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ResolveKnownComponent = name => name == "dev" ? known : null,
            });

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("wgA");
        result.Instances.Select(i => i.InstanceName).ShouldBe(new[] { "wgA#0", "dev#0" });
        var dev = result.Instances[1];
        dev.KnownComponentIdentifier.ShouldBe("dev");
        dev.CellDraftName.ShouldBeNull();
        dev.PositionXUm.ShouldBe(10, Tolerance);
        dev.PositionYUm.ShouldBe(0, Tolerance);

        result.Warnings.ShouldBeEmpty();
        // The only note is the known-component resolution — no artifact skip.
        var note = result.Infos.ShouldHaveSingleItem();
        note.ShouldContain("resolved to existing component 'dev'");

        var connection = result.Connections.ShouldHaveSingleItem();
        connection.A.PinName.ShouldBe("out");
        connection.B.PinName.ShouldBe("opt1");
    }

    [Fact]
    public async Task Explode_ArtifactBehindTwoWrapperLevels_FallsBackToSkipNote()
    {
        // Deeper than the documented shape (top → wrapper → partial): TWO
        // wrapper levels between the top cell and the partial. The unwrap still
        // recognizes the artifact (the look-through walks pass-through chains),
        // but the recursion supports one wrapper level only — the cell keeps
        // the skip + info note and its devices do not import.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("outer", 20000, 0)
            .EndCell()
            .WaveguideCell("wgA")
            .BeginCell("outer")
                .SRef("nazca", 0, 0)
            .EndCell()
            .BeginCell("nazca")
                .SRef("ConnectAPIC_NazcaPartial", 0, 0)
            .EndCell()
            .BeginCell("ConnectAPIC_NazcaPartial")
                .SRef("dev", 1000, 0)
            .EndCell()
            .BeginCell("dev")
                .Boundary(1, 0, (0, 1750), (5000, 1750), (5000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (5000, 0), (5000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "opt1", 0, 2000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("wgA");
        result.Instances.ShouldHaveSingleItem().CellName.ShouldBe("wgA");
        result.Warnings.ShouldBeEmpty();
        var note = result.Infos.ShouldHaveSingleItem();
        note.ShouldContain("export artifact");
        note.ShouldContain("'outer'");
        note.ShouldContain("ConnectAPIC_NazcaPartial");
        note.ShouldContain("not reconstructed");
    }
}
