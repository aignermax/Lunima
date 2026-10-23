using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Exact, hash-stripped, and empty-cell known-component resolution.</summary>
public class GdsHierarchyImporterKnownComponentResolutionTests
{
    [Fact]
    public async Task Explode_HashSuffixedCell_ResolvesToBaseKnownComponent()
    {
        // A gdsfactory-style hashed cell name resolves to the base PDK name;
        // its pins come from the resolver (authoritative PDK pin names).
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
                .SRef("mmi1x2_A1B2C3", 0, 0)
                .SRef("wgB", 30000, 3000)
            .EndCell()
            .BeginCell("mmi1x2_A1B2C3")
                .Boundary(1, 0, (0, 0), (30000, 0), (30000, 10000), (0, 10000), (0, 0))
            .EndCell()
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ResolveKnownComponent = name => name == "mmi1x2" ? known : null,
            });

        // The known cell produces no draft; only the unknown wgB does.
        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("wgB");

        result.Instances.Count.ShouldBe(2);
        result.Instances[0].KnownComponentIdentifier.ShouldBe("mmi1x2");
        result.Instances[0].PdkSource.ShouldBe("testpdk");
        result.Instances[0].CellDraftName.ShouldBeNull();
        result.Instances[1].CellDraftName.ShouldBe("wgB");

        // mmi.o2 at GDS (30, 5) abuts wgB.in (offset (30, 3) + cell-local (0, 2)).
        var connection = result.Connections.ShouldHaveSingleItem();
        connection.A.InstanceIndex.ShouldBe(0);
        connection.A.PinName.ShouldBe("o2");
        connection.B.InstanceIndex.ShouldBe(1);
        connection.B.PinName.ShouldBe("in");
        connection.XUm.ShouldBe(30, Tolerance);
        connection.YUm.ShouldBe(5, Tolerance);
        // Resolution visibility: the user sees which library component the cell
        // was bound to — as an INFO note, not a warning (a successful binding
        // is the normal case).
        result.Warnings.ShouldBeEmpty();
        result.Infos.ShouldHaveSingleItem().ShouldContain("resolved to existing component 'mmi1x2'");
    }

    [Fact]
    public async Task Explode_ExactCellNameMatch_ResolvesWithoutStripping()
    {
        var known = new KnownComponent(
            "wgA", "testpdk", 10, 4,
            new[]
            {
                Pin("in", 0, 2, 180),
                Pin("out", 10, 2, 0),
            });

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

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ResolveKnownComponent = name => name == "wgA" ? known : null,
            });

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("wgB");
        result.Instances[0].KnownComponentIdentifier.ShouldBe("wgA");
        // Connection reconstruction uses the resolver-supplied pins.
        result.Connections.ShouldHaveSingleItem().A.PinName.ShouldBe("out");
        // Resolution visibility: the binding lands in the info-notes channel.
        result.Warnings.ShouldBeEmpty();
        result.Infos.ShouldHaveSingleItem().ShouldContain("resolved to existing component 'wgA'");
    }

    [Fact]
    public async Task Explode_AmbiguousStrippedNames_NeverGuessed_BecomesDraftWithWarning()
    {
        // Both "thing_AB12" and "thing" resolve to DIFFERENT components: the
        // importer must not guess — the cell becomes a draft.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("thing_AB12_CD34", 0, 0)
            .EndCell()
            .WaveguideCell("thing_AB12_CD34")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ResolveKnownComponent = name => name switch
                {
                    "thing_AB12" => new KnownComponent("thingAB", "pdk", 10, 4, Array.Empty<DetectedPin>()),
                    "thing" => new KnownComponent("thingBase", "pdk", 10, 4, Array.Empty<DetectedPin>()),
                    _ => null,
                },
            });

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("thing_AB12_CD34");
        result.Instances[0].KnownComponentIdentifier.ShouldBeNull();
        result.Instances[0].CellDraftName.ShouldBe("thing_AB12_CD34");
        result.Warnings.ShouldContain(w => w.Contains("ambiguous") && w.Contains("thing_AB12_CD34"));
    }

    [Fact]
    public async Task Explode_ZeroGeometryCellResolvingToKnownComponent_IsKept()
    {
        // A deliberate name binding to a PDK component wins over the
        // zero-geometry drop (the size-mismatch warning covers the geometry
        // gap) — only UNKNOWN zero-geometry cells are dropped.
        var known = new KnownComponent(
            "anchor", "testpdk", 5, 5,
            new[] { Pin("io", 0, 0, 180) });

        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("anchor", 0, 0)
            .EndCell()
            .BeginCell("anchor")
                .Text(1, 10, "io", 0, 0)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ResolveKnownComponent = name => name == "anchor" ? known : null,
            });

        result.Instances.ShouldHaveSingleItem().KnownComponentIdentifier.ShouldBe("anchor");
        result.Warnings.ShouldContain(w => w.Contains("UNSCALED"),
            "the known component keeps its size-mismatch warning");
        result.Infos.ShouldNotContain(i => i.Contains("skipped"),
            "a resolved zero-geometry cell is not treated as a skip");
    }
}
