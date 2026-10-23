using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Empty cells and exported artifact wrappers are skipped without losing normal cells.</summary>
public class GdsHierarchyImporterArtifactSkipTests
{
    [Fact]
    public async Task Explode_ZeroGeometryCell_DroppedWithOneInfoNote_InstancesExcluded()
    {
        // "zeroL" mimics a gdsfactory zero-length straight (a route_bundle
        // artifact): its only content is a port label at a single point, so the
        // flattened bbox is empty (0 × 0). Two instances of it sit exactly on
        // wgA's pins — the wgA.out ↔ wgB.in abutment must survive untouched.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("zeroL", 0, 2000)
                .SRef("zeroL", 10000, 2000)
                .SRef("wgB", 10000, 0)
            .EndCell()
            .WaveguideCell("wgA")
            .BeginCell("zeroL")
                .Text(1, 10, "io", 0, 0)
            .EndCell()
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        // The normal cells import as before; the zero-geometry cell leaves no
        // draft and no instances behind.
        result.ImportedCellDrafts.Select(d => d.CellName).ShouldBe(new[] { "wgA", "wgB" });
        result.Instances.Count.ShouldBe(2);
        result.Instances.ShouldNotContain(i => i.CellName == "zeroL");

        // The old cascade (empty-bbox warning, "not registered: zero size",
        // per-instance placement skips) collapses into ONE info note per cell.
        result.Warnings.ShouldBeEmpty();
        var note = result.Infos.ShouldHaveSingleItem();
        note.ShouldContain("'zeroL'");
        note.ShouldContain("2 instance(s) skipped");

        // The surviving connection is the wgA.out ↔ wgB.in abutment — nothing
        // references the dropped instances (they never entered the matcher).
        var connection = result.Connections.ShouldHaveSingleItem();
        connection.A.PinName.ShouldBe("out");
        connection.B.PinName.ShouldBe("in");
        connection.XUm.ShouldBe(10, Tolerance);
        connection.YUm.ShouldBe(2, Tolerance);
    }

    [Fact]
    public async Task Explode_LunimaExportArtifactCell_SkippedWithOneInfoNote()
    {
        // 'ConnectAPIC_NazcaPartial' is the top cell name our mixed-backend
        // export (MixedBackendGdsOrchestrator.NazcaPartialTopCellName) writes
        // for the flattened nazca partial. It HAS geometry (on a non-port
        // layer, hence pinless) — the skip is by name convention, independent
        // of the zero-geometry drop. Two references still yield ONE note.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("ConnectAPIC_NazcaPartial", 20000, 0)
                .SRef("ConnectAPIC_NazcaPartial", 40000, 0)
            .EndCell()
            .WaveguideCell("wgA")
            .BeginCell("ConnectAPIC_NazcaPartial")
                .Boundary(111, 0, (0, 0), (5000, 0), (5000, 3000), (0, 3000), (0, 0))
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("wgA");
        result.Instances.ShouldHaveSingleItem().CellName.ShouldBe("wgA");
        result.Warnings.ShouldBeEmpty(
            "the artifact is skipped by convention, not failed as an unpersistable draft");
        var note = result.Infos.ShouldHaveSingleItem();
        note.ShouldContain("ConnectAPIC_NazcaPartial");
        note.ShouldContain("export artifact");
        note.ShouldContain("not reconstructed");
    }

    [Fact]
    public async Task Explode_LunimaExportArtifactBehindPassThroughWrapper_SkippedWithOneInfoNote()
    {
        // The user's mixed-backend round-trip: gdsfactory's import_gds names the
        // merged component after the source file's TOP cell, so the flattened
        // nazca partial re-enters the merged file behind nazca's default 'nazca'
        // pass-through wrapper (one untransformed reference, nothing else). A
        // name-only artifact check misses this — the wrapper became a pin-less
        // draft and failed with "not registered: no pins" plus a skipped
        // placement, the exact cascade the artifact skip exists to prevent.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("nazca", 20000, 0)
            .EndCell()
            .WaveguideCell("wgA")
            .BeginCell("nazca")
                .SRef("ConnectAPIC_NazcaPartial", 0, 0)
            .EndCell()
            .BeginCell("ConnectAPIC_NazcaPartial")
                .Boundary(111, 0, (0, 0), (5000, 0), (5000, 3000), (0, 3000), (0, 0))
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("wgA");
        result.Instances.ShouldHaveSingleItem().CellName.ShouldBe("wgA");
        result.Warnings.ShouldBeEmpty(
            "the wrapped artifact is skipped by convention, not failed as an unpersistable draft");
        var note = result.Infos.ShouldHaveSingleItem();
        note.ShouldContain("nazca");
        note.ShouldContain("ConnectAPIC_NazcaPartial");
        note.ShouldContain("export artifact");
        note.ShouldContain("not reconstructed");
    }

    [Fact]
    public async Task Explode_PassThroughWrapperAroundNormalCell_IsNotSkipped()
    {
        // The wrapper look-through must ONLY recognize wrappers around artifact
        // cells: a pass-through wrapper around a real design cell imports
        // exactly as before (draft named after the wrapper, absorbing the
        // subtree — pins included).
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("nazca", 0, 0)
            .EndCell()
            .BeginCell("nazca")
                .SRef("wgA", 0, 0)
            .EndCell()
            .WaveguideCell("wgA")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("nazca");
        result.Instances.ShouldHaveSingleItem().CellName.ShouldBe("nazca");
        result.ImportedCellDrafts[0].Pins.Count.ShouldBe(2);
        result.Warnings.ShouldBeEmpty();
        result.Infos.ShouldBeEmpty("a wrapper around a normal cell is not an export artifact");
    }

    [Fact]
    public async Task Explode_TransformedWrapperAroundArtifact_IsNotSkipped()
    {
        // A wrapper whose reference TRANSFORMS the artifact (here: 90° rotation)
        // is not a pass-through — unwrapping would misplace the geometry, so the
        // cell is treated as ordinary design content (draft, no artifact note).
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("nazca", 0, 0)
            .EndCell()
            .BeginCell("nazca")
                .SRef("ConnectAPIC_NazcaPartial", 0, 0, angleDegrees: 90)
            .EndCell()
            .BeginCell("ConnectAPIC_NazcaPartial")
                .Boundary(111, 0, (0, 0), (5000, 0), (5000, 3000), (0, 3000), (0, 0))
            .EndCell()
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("nazca");
        result.Instances.ShouldHaveSingleItem().CellName.ShouldBe("nazca");
        result.Infos.ShouldBeEmpty("a transforming wrapper is not a pass-through artifact");
    }
}
