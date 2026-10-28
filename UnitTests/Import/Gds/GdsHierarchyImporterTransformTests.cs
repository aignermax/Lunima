using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Instance rotations, reflections, arrays, and magnification diagnostics.</summary>
public class GdsHierarchyImporterTransformTests
{
    [Fact]
    public async Task Explode_RotatedInstance90_PinsProjectedNumericallyCorrect()
    {
        // B is rotated 90° CCW (GDS, Y-up) at offset (10, 6) µm. Worked example
        // (all µm): cell "wg" 10×4, pins in=(0,2,180°), out=(10,2,0°).
        // T: x′ = −y + 10, y′ = x + 6. Top bbox = (0,0)-(10,16).
        //   B.in : (0,2) → GDS (8,6)  → app (8, 10), west → down  (90°)
        //   B.out: (10,2) → GDS (8,16) → app (8, 0),  east → up   (270°)
        //   B placed bbox top-left: (6, 0); app rotation = −90° ≡ 270°.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wg", 0, 0)
                .SRef("wg", 10000, 6000, angleDegrees: 90)
            .EndCell()
            .WaveguideCell("wg")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.ImportedCellDrafts.ShouldHaveSingleItem().CellName.ShouldBe("wg");
        result.Instances.Count.ShouldBe(2);
        result.Instances[0].PositionXUm.ShouldBe(0, Tolerance);
        result.Instances[0].PositionYUm.ShouldBe(12, Tolerance); // 16 − 4: A sits at the bottom
        result.Instances[1].PositionXUm.ShouldBe(6, Tolerance);
        result.Instances[1].PositionYUm.ShouldBe(0, Tolerance);
        result.Instances[1].RotationDegrees.ShouldBe(270, Tolerance); // GDS +90° ≡ app −90°
        result.Connections.ShouldBeEmpty();

        // Numeric projection check of the rotated pins through the internal projector.
        var flattener = new GdsCellFlattener(library);
        var gdsInstances = flattener.GetInstanceTree("TOP");
        var cellBBox = flattener.GetBoundingBox("wg");
        var topBBox = flattener.GetBoundingBox("TOP");
        var pins = new[] { Pin("in", 0, 2, 180), Pin("out", 10, 2, 0) };

        var projected = GdsInstancePinProjector.ProjectPins(gdsInstances[1], cellBBox, pins, topBBox);
        projected[0].Name.ShouldBe("in");
        projected[0].XUm.ShouldBe(8, Tolerance);
        projected[0].YUm.ShouldBe(10, Tolerance);
        projected[0].AngleDegrees.ShouldBe(90, Tolerance);
        projected[1].Name.ShouldBe("out");
        projected[1].XUm.ShouldBe(8, Tolerance);
        projected[1].YUm.ShouldBe(0, Tolerance);
        projected[1].AngleDegrees.ShouldBe(270, Tolerance);
    }

    [Fact]
    public async Task Explode_ReflectedInstance_ReconstructionUsesReflectedTransform()
    {
        // Asymmetric pin: "in" at cell GDS (0, 3). Mirrored about the cell's X
        // axis it lands at GDS (0, −3); the top bbox becomes (0,−4)-(10,0), so
        // app Y = 0 − (−3) = 3 — an unreflected reconstruction would say 1.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wg", 0, 0, reflected: true)
            .EndCell()
            .WaveguideCell("wg", inY: 3000)
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        var instance = result.Instances.ShouldHaveSingleItem();
        instance.Reflected.ShouldBeTrue();
        result.Warnings.ShouldNotContain(w => w.Contains("mirrored"),
            "mirrored instances are placed mirrored — nothing to warn about");

        var flattener = new GdsCellFlattener(library);
        var projected = GdsInstancePinProjector.ProjectPins(
            flattener.GetInstanceTree("TOP")[0],
            flattener.GetBoundingBox("wg"),
            new[] { Pin("in", 0, 1, 180), Pin("out", 10, 2, 0) },
            flattener.GetBoundingBox("TOP"));

        // Note: the cell-local app pin (0,1) — app frame of the UNREFLECTED
        // cell — is what gets projected through the true reflected transform.
        projected[0].XUm.ShouldBe(0, Tolerance);
        projected[0].YUm.ShouldBe(3, Tolerance);
        projected[0].AngleDegrees.ShouldBe(180, Tolerance); // X-mirror keeps horizontal directions
        projected[1].XUm.ShouldBe(10, Tolerance);
        projected[1].YUm.ShouldBe(2, Tolerance);
    }

    [Fact]
    public async Task Explode_NonCardinalAngle_KeptExactlyWithWarning()
    {
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wg", 0, 0, angleDegrees: 45)
            .EndCell()
            .WaveguideCell("wg")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        // GDS +45° ≡ app −45° ≡ 315° — kept exactly, not snapped to a cardinal:
        // snapping would move the placed pins off the true projected joints.
        result.Instances.ShouldHaveSingleItem().RotationDegrees.ShouldBe(315, Tolerance);
        result.Warnings.ShouldContain(w =>
            w.Contains("45") && w.Contains("kept exactly") && w.Contains("Manhattan"));
        result.Infos.ShouldBeEmpty("the non-cardinal rotation note is a caveat, not an informational note");
    }

    [Fact]
    public async Task Explode_ArrayWithNonCardinalRotation_WarnsOncePerReferenceWithMemberCount()
    {
        // A 3×3 AREF rotated 45° expands to 9 instances — the snap warning must
        // collapse into ONE per reference (with member count), not flood nine.
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .ARef("wg", columns: 3, rows: 3, originX: 0, originY: 0,
                    columnSpacingDbUnits: 20000, rowSpacingDbUnits: 10000, angleDegrees: 45.0)
            .EndCell()
            .WaveguideCell("wg")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.Instances.Count.ShouldBe(9);
        var rotationWarnings = result.Warnings.Where(w => w.Contains("non-cardinal")).ToList();
        var warning = rotationWarnings.ShouldHaveSingleItem(
            "an AREF must not flood one identical warning per member");
        warning.ShouldContain("9 instances");
        warning.ShouldContain("wg#0");
        warning.ShouldContain("45");
    }

    [Fact]
    public async Task Explode_NegativeMagnification_WarnsAbout180DegreeSnapError()
    {
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wg", 0, 0, magnification: -2.0)
            .EndCell()
            .WaveguideCell("wg")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.Warnings.ShouldContain(w => w.Contains("NEGATIVE") && w.Contains("180"));
    }
}
