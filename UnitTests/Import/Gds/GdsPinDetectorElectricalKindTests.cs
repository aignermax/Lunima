using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Metal geometry and label names determine electrical pin kinds.</summary>
public class GdsPinDetectorElectricalKindTests
{
    [Fact]
    public void Label_OnMetalPolygonOutline_InfersElectrical()
    {
        var cell = Cell(
            Poly(11, 0, (4, 1.75), (6, 1.75), (6, 2.25), (4, 2.25), (4, 1.75)),
            Label(1, 10, "p1", x: 6, y: 2)); // ON the pad's right edge

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldHaveSingleItem().IsElectrical.ShouldBe(true);
    }

    [Fact]
    public void Label_InsideMetalPad_InfersElectrical()
    {
        // Pad label at the pad CENTER, farther from the outline than the touch
        // tolerance: the interior still proves the metal contact (the user's
        // black-box case).
        var cell = Cell(
            Poly(12, 0, (2, 0.5), (8, 0.5), (8, 3.5), (2, 3.5), (2, 0.5)),
            Label(1, 10, "p1", x: 5, y: 2));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldHaveSingleItem().IsElectrical.ShouldBe(true);
    }

    [Fact]
    public void Label_OnWaveguidePolygon_KeepsUnknownKind()
    {
        // Waveguide evidence means optical — expressed as kind-UNKNOWN (null):
        // a later metal-route match may still overrule it with stronger evidence.
        var cell = Cell(
            Poly(1, 0, (0, 1.75), (3, 1.75), (3, 2.25), (0, 2.25), (0, 1.75)),
            Label(1, 10, "o1", x: 0, y: 2));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldHaveSingleItem().IsElectrical.ShouldBeNull();
    }

    [Fact]
    public void Label_TouchingMetalAndWaveguide_MetalWins()
    {
        // Electrode over waveguide (eopm-style): both layer classes touch the
        // anchor; metal is the stronger evidence.
        var cell = Cell(
            Poly(1, 0, (1, 1.75), (4, 1.75), (4, 2.25), (1, 2.25), (1, 1.75)),
            Poly(11, 0, (1, 1.9), (4, 1.9), (4, 2.1), (1, 2.1), (1, 1.9)),
            Label(1, 10, "p1", x: 2, y: 2));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldHaveSingleItem().IsElectrical.ShouldBe(true);
    }

    [Theory]
    [InlineData("anode")]
    [InlineData("CATHODE")]
    [InlineData("elec1")]
    [InlineData("Bond_Pad_34")]
    [InlineData("gnd")]
    [InlineData("VCC")]
    [InlineData("vdd_0")]
    public void Label_WithElectricalNameAndNoGeometry_InfersElectrical(string label)
    {
        var pins = GdsPinDetector.Detect(Cell(Label(1, 10, label, x: 5, y: 2)), Box10x4);

        pins.ShouldHaveSingleItem().IsElectrical.ShouldBe(true);
    }

    [Theory]
    [InlineData("o1")]
    [InlineData("in")]
    [InlineData("port0")]
    public void Label_WithOpticalNameAndNoGeometry_KeepsUnknownKind(string label)
    {
        var pins = GdsPinDetector.Detect(Cell(Label(1, 10, label, x: 5, y: 2)), Box10x4);

        pins.ShouldHaveSingleItem().IsElectrical.ShouldBeNull();
    }

    [Fact]
    public void Label_WithElectricalNameOnWaveguide_GeometryBeatsName()
    {
        // The name heuristic is only the fallback: waveguide evidence wins.
        var cell = Cell(
            Poly(1, 0, (0, 1.75), (3, 1.75), (3, 2.25), (0, 2.25), (0, 1.75)),
            Label(1, 10, "anode", x: 0, y: 2));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        pins.ShouldHaveSingleItem().IsElectrical.ShouldBeNull();
    }

    [Fact]
    public void HeuristicPin_OnWaveguideEdge_KeepsUnknownKind()
    {
        var cell = Cell(Poly(1, 0, (8, 1), (10, 1), (10, 2), (8, 2), (8, 1)));

        var pins = GdsPinDetector.Detect(cell, Box10x4);

        // Both channel ends of the label-free stub — both keep the unknown kind.
        pins.Count.ShouldBe(2);
        pins.ShouldAllBe(p => p.IsElectrical == null);
    }
}
