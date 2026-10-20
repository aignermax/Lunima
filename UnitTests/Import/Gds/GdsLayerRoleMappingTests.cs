using CAP_DataAccess.Import.Gds;
using Shouldly;

namespace UnitTests.Import.Gds;

public class GdsLayerRoleMappingTests
{
    [Fact]
    public void Apply_MapsOnlyCoreAndConductorsToRouting()
    {
        var options = GdsLayerRoleMapping.Apply(new() { PinTouchToleranceUm = 0.25 },
        [
            new(20, 3, GdsLayerRole.OpticalCore),
            new(21, 0, GdsLayerRole.Slab),
            new(22, 0, GdsLayerRole.Etch),
            new(23, 0, GdsLayerRole.Electrical),
            new(24, 0, GdsLayerRole.Heater),
            new(25, 0, GdsLayerRole.Auxiliary),
            new(26, 0, GdsLayerRole.Unknown),
        ]);

        options.RouteLayers.ShouldBe([(20, 3)]);
        options.PinDetection.WaveguideLayers.ShouldBe([(20, 3)]);
        options.MetalRouteLayers.ShouldBe([(23, 0), (24, 0)]);
        options.PinDetection.ElectricalLayers.ShouldBe([(23, 0), (24, 0)]);
        options.PinTouchToleranceUm.ShouldBe(0.25);
    }

    [Theory]
    [InlineData(GdsLayerRole.PortMarker, DetectedPinSource.PortShape)]
    [InlineData(GdsLayerRole.PortLabel, DetectedPinSource.Label)]
    [InlineData(GdsLayerRole.PortMarker | GdsLayerRole.PortLabel, DetectedPinSource.Label)]
    public void Apply_SeparatesMarkerGeometryFromTextLabels(GdsLayerRole role, DetectedPinSource source)
    {
        var flat = new FlattenedGdsCell { CellName = "device" };
        flat.Polygons.Add(Rectangle(40, 0, 0, 1.5, 0.5, 2.5));
        flat.Texts.Add(new GdsText { Layer = 40, TextType = 0, Text = "o1", Position = new(0, 2) });
        var options = GdsLayerRoleMapping.Apply(new(), [new(40, 0, role)]);

        var pins = GdsPinDetector.Detect(flat, new(0, 0, 10, 4), options.PinDetection);

        pins.ShouldHaveSingleItem().Source.ShouldBe(source);
    }

    [Theory]
    [InlineData(GdsLayerRole.Unknown)]
    [InlineData(GdsLayerRole.Slab)]
    [InlineData(GdsLayerRole.Etch)]
    [InlineData(GdsLayerRole.Auxiliary)]
    public async Task ExplicitNonRoutingRole_RetainsGeometryWithoutInventingPins(GdsLayerRole role)
    {
        var library = new GdsLibrary();
        var cell = new GdsCell { Name = "device" };
        cell.Elements.Add(Rectangle(1, 0, 0, 0, 10, 0.5));
        cell.Elements.Add(new GdsText { Layer = 59, TextType = 0, Text = "annotation", Position = new(0, 0.25) });
        library.Cells.Add(cell.Name, cell);
        var options = GdsLayerRoleMapping.Apply(
            new() { Mode = GdsHierarchyImportMode.BlackBox }, [new(1, 0, role)]);

        var result = await GdsHierarchyImporter.ImportAsync(library, cell.Name, options);

        var draft = result.ImportedCellDrafts.ShouldHaveSingleItem();
        draft.Pins.ShouldBeEmpty();
        draft.Outlines.ShouldHaveSingleItem().Layer.ShouldBe(1);
    }

    [Fact]
    public async Task EmptyMapping_RetainsUnassignedTopGeometryAsResidual()
    {
        var library = new GdsLibrary();
        var cell = new GdsCell { Name = "TOP" };
        cell.Elements.Add(Rectangle(1, 0, 0, 0, 10, 0.5));
        library.Cells.Add(cell.Name, cell);

        var result = await GdsHierarchyImporter.ImportAsync(library, cell.Name,
            GdsLayerRoleMapping.Apply(new(), []));

        result.TopCellWaveguidePolygons.ShouldBeEmpty();
        result.TopCellResidualPolygons.ShouldHaveSingleItem().Layer.ShouldBe(1);
        result.Connections.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(GdsLayerRole.Electrical)]
    [InlineData(GdsLayerRole.Heater)]
    public void Apply_RejectsConflictingCoreAndConductorEvenAcrossDuplicateAssignments(GdsLayerRole conductor)
    {
        Should.Throw<ArgumentException>(() => GdsLayerRoleMapping.Apply(new(),
            [new(20, 0, GdsLayerRole.OpticalCore), new(20, 0, conductor)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitMapping_DiscoversArrowMarkersOnlyOnAssignedLayers(bool assignMarkers)
    {
        var flat = new FlattenedGdsCell { CellName = "device" };
        flat.Polygons.Add(Rectangle(1, 0, 0, 1.75, 10, 2.25));
        (double X, double Y)[] shape =
            [(0.25, -0.25), (0, 0), (0.25, 0.25), (0.25, 0.125),
             (0.35, 0.125), (0.35, -0.125), (0.25, -0.125)];
        foreach (int direction in new[] { -1, 1 })
            flat.Polygons.Add(new GdsPolygon
            {
                Layer = 232,
                DataType = 0,
                Points = shape.Select(p => new GdsPoint(direction * p.X, 2 + p.Y)).ToArray(),
            });
        var options = GdsLayerRoleMapping.Apply(new(),
            [new(1, 0, GdsLayerRole.OpticalCore),
             new(232, 0, assignMarkers ? GdsLayerRole.PortMarker : GdsLayerRole.Auxiliary)]);

        var pins = GdsPinDetector.Detect(flat, new(0, 0, 10, 4), options.PinDetection);

        pins.ShouldNotBeEmpty();
        pins.Any(p => p.Source == DetectedPinSource.ArrowMarker).ShouldBe(assignMarkers);
    }

    private static GdsPolygon Rectangle(int layer, int datatype, double x1, double y1, double x2, double y2) =>
        new()
        {
            Layer = layer,
            DataType = datatype,
            Points = [new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2), new(x1, y1)],
        };
}
