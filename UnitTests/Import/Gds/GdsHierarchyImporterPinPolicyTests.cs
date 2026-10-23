using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Pin naming, exclusion, metadata filtering, and generated-code escaping.</summary>
public class GdsHierarchyImporterPinPolicyTests
{
    [Fact]
    public async Task Explode_DuplicateAndHeuristicCollidingPinNames_DedupedWithWarnings()
    {
        // Two labels with the same text (legal GDS) plus a label literally
        // named "heur_1" colliding with the heuristic pin: names are made
        // unique BEFORE connection reconstruction, so pin-by-name resolution
        // can never mis-wire. Pin order: left edge by app Y (label y=0,
        // heuristic y=500, label y=1000), then the right-edge label.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("dup", 0, 0)
            .EndCell()
            .BeginCell("dup")
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Text(1, 10, "o1", 0, 1500)
                .Text(1, 10, "o1", 0, 2500)        // same text twice — legal GDS
                .Text(1, 10, "heur_1", 10000, 2000) // collides with the heuristic pin name
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.Pins.Select(p => p.Name).ShouldBe(new[] { "o1", "heur_1", "o1_2", "heur_1_2" });
        result.Warnings.ShouldContain(w => w.Contains("duplicate pin name 'o1'") && w.Contains("o1_2"));
        result.Warnings.ShouldContain(w => w.Contains("duplicate pin name 'heur_1'") && w.Contains("heur_1_2"));
    }

    [Fact]
    public async Task Explode_ExcludedGuessedPin_RemovedFromDraftAndSurvivorKeepsName()
    {
        // A cell with no port labels: the edge heuristic produces two pins
        // (heur_1 on the left edge, heur_2 on the right edge). When the user
        // removes heur_1 in the dialog, the exclusion must reach the importer
        // and the surviving pin must keep the name the dialog showed.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgNoLabel", 0, 0)
            .EndCell()
            .UnlabeledWaveguideCell("wgNoLabel")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(
            library, "TOP", new GdsHierarchyImportOptions
            {
                ExcludedGuessedPins = new[] { new GdsGuessedPin("wgNoLabel", "heur_1") },
            });

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.Pins.Select(p => p.Name).ShouldBe(new[] { "heur_2" });
        draft.Pins[0].XUm.ShouldBe(10, Tolerance);
        draft.Pins[0].AngleDegrees.ShouldBe(0, Tolerance);

        // No abutment exists with only one pin per instance.
        result.Connections.ShouldBeEmpty();

        // The instance's pin set follows the filtered draft.
        var instance = result.Instances.ShouldHaveSingleItem();
        instance.CellDraftName.ShouldBe("wgNoLabel");
    }

    [Fact]
    public async Task Explode_MultiLineMetadataLabelOnPortLayer_DoesNotBecomeDraftPin()
    {
        // Foundry files carry metadata blobs as TEXT records — nazca writes
        // "cellname: …\nfoundry_pdk: …" into every cell. Even on the configured
        // port layer such a blob is not a port label: a pin name cannot span
        // lines (the top-level-port path already filters these).
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("dev", 0, 0)
            .EndCell()
            .BeginCell("dev")
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "in", 0, 2000)
                .Text(1, 10, "out", 10000, 2000)
                .Text(1, 10, "cellname: dev\nfoundry_pdk: test_pdk", 5000, 2000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.Pins.Select(p => p.Name).ShouldBe(new[] { "in", "out" },
            "the multi-line metadata blob must not become a pin");
        draft.Pins.ShouldNotContain(p => p.Name.Contains('\n'));
    }

    [Fact]
    public async Task Explode_MultiLineMetadataLabelOnOddLayer_FallbackIgnoresIt()
    {
        // The any-layer label fallback sweeps EVERY text when no configured port
        // layer yields a label — without the multi-line filter it promoted the
        // metadata blob to a pin with embedded newlines in its name (seen on a
        // real production file).
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("dev", 0, 0)
            .EndCell()
            .BeginCell("dev")
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
                .Text(66, 0, "cellname: dev\nfoundry_pdk: test_pdk", 5000, 1000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.Pins.ShouldNotContain(p => p.Name.Contains('\n'),
            "the fallback must skip multi-line metadata blobs");
        draft.Pins.ShouldNotContain(p => p.Name.Contains("cellname"));
        draft.Pins.Count.ShouldBe(2, "the waveguide stripe's two edge-heuristic pins remain");
        result.Infos.ShouldNotContain(i => i.Contains("non-standard layer"),
            "no label fallback ran — the blob is the only text and is not a label");
    }

    [Fact]
    public async Task Explode_PinlessDraft_ImporterStaysSilent_ServiceOwnsThatWarning()
    {
        // "blob" has no waveguide-layer geometry and no port labels → no pins.
        // The importer deliberately does NOT warn here: the service reports the
        // more actionable "geometry-only component" message when it registers
        // the draft — two warnings for the same fact would be noise.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("blob", 0, 0)
            .EndCell()
            .BeginCell("blob")
                .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.ImportedCellDrafts.ShouldHaveSingleItem().Pins.ShouldBeEmpty();
        result.Warnings.ShouldNotContain(w => w.Contains("no pins"));
    }

    [Fact]
    public async Task Explode_CellNameWithControlCharacters_RawCodeStaysValidPython()
    {
        // A GDS STRING may contain control characters (e.g. a newline) — the
        // emitted Python snippet must not break on them.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("weird\ncell", 0, 0)
            .EndCell()
            .BeginCell("weird\ncell")
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Text(1, 10, "in", 0, 2000)
                .Text(1, 10, "out", 10000, 2000)
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.RawCode.ShouldNotContain("weird\ncell");
        draft.RawCode.ShouldContain("weird_cell");
    }
}
