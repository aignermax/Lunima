using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;
using System.IO;

namespace UnitTests.Import.Gds;

/// <summary>Real GDS reader and flattener fixtures exercise label and port-shape detection.</summary>
public class GdsPinDetectorReaderIntegrationTests
{
    [Fact]
    public async Task EndToEnd_PortShapeOnly_PinDetectedThroughRealReader()
    {
        // gdsfactory-style cell sized by a DevRec outline on (2,0) and a port
        // marker on the default port layer (1,10); no port labels at all.
        var gds = GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("CELL")
                .Boundary(2, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
                .Boundary(1, 10, (0, 1500), (500, 1500), (500, 2500), (0, 2500), (0, 1500))
            .EndCell()
            .EndLibrary()
            .ToArray();

        using var stream = new MemoryStream(gds);
        var library = await new GdsReader().ReadAsync(stream);
        var flattener = new GdsCellFlattener(library);
        var flattened = flattener.Flatten("CELL");
        var bbox = flattener.GetBoundingBox("CELL");

        var pins = GdsPinDetector.Detect(flattened, bbox);

        var pin = pins.ShouldHaveSingleItem();
        pin.Source.ShouldBe(DetectedPinSource.PortShape);
        pin.XUm.ShouldBe(0, Tolerance);
        pin.YUm.ShouldBe(2, Tolerance);
        pin.WidthUm.ShouldBe(1, Tolerance);
        pin.AngleDegrees.ShouldBe(180, Tolerance);
    }

    [Fact]
    public async Task EndToEnd_GdsFactoryStyleWaveguide_LabelAndHeuristicPin()
    {
        // 10 × 0.5 µm waveguide ending at the left/right bbox edges, a DevRec-style
        // marker on a non-waveguide layer sizing the cell to 10 × 4 µm, and a port
        // label on (1, 10) covering the left end face. 1000 db units = 1 µm.
        var gds = GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("WG")
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Boundary(2, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "o1", 0, 2000)
            .EndCell()
            .EndLibrary()
            .ToArray();

        using var stream = new MemoryStream(gds);
        var library = await new GdsReader().ReadAsync(stream);
        var flattener = new GdsCellFlattener(library);
        var flattened = flattener.Flatten("WG");
        var bbox = flattener.GetBoundingBox("WG");

        var pins = GdsPinDetector.Detect(flattened, bbox);

        pins.Count.ShouldBe(2);
        // Left edge: label pin only — the waveguide face below it is covered.
        pins[0].Name.ShouldBe("o1");
        pins[0].Source.ShouldBe(DetectedPinSource.Label);
        pins[0].XUm.ShouldBe(0, Tolerance);
        pins[0].YUm.ShouldBe(2, Tolerance); // 4 − 2
        pins[0].AngleDegrees.ShouldBe(180, Tolerance);
        // Right edge: heuristic pin from the waveguide end face.
        pins[1].Name.ShouldBe("heur_1");
        pins[1].Source.ShouldBe(DetectedPinSource.EdgeHeuristic);
        pins[1].XUm.ShouldBe(10, Tolerance);
        pins[1].YUm.ShouldBe(2, Tolerance);
        pins[1].AngleDegrees.ShouldBe(0, Tolerance);
        pins[1].WidthUm.ShouldBe(0.5, Tolerance);
    }
}
