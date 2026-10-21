using CAP_DataAccess.Import.Gds;
using Xunit;

namespace UnitTests.Import.Gds;

public class GdsReviewPortEvidenceTests
{
    [Fact]
    public void LabelsNeedMatchingArrowEvidenceAndNeverInferRoutingMaterials()
    {
        var library = Library();
        var cell = library.Cells["top"];
        cell.Elements.Add(Arrow(700, 0));
        cell.Elements.Add(new GdsText { Layer = 701, Text = "a0", Position = new(.3, 0) });
        cell.Elements.Add(new GdsText { Layer = 702, Text = "lc", Position = new(.3, 0) });
        cell.Elements.Add(new GdsText { Layer = 703, Text = "length:200", Position = new(.3, 0) });
        cell.Elements.Add(new GdsText { Layer = 704, Text = "owner: foundry\nversion: 1", Position = new(.3, 0) });
        cell.Elements.Add(new GdsText { Layer = 705, Text = "o9", Position = new(100, 0) });
        var result = GdsReviewPortEvidence.Suggest(library, "top");
        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Assignment == new GdsLayerAssignment(700, 0, GdsLayerRole.PortMarker));
        Assert.Contains(result, s => s.Assignment == new GdsLayerAssignment(701, 0, GdsLayerRole.PortLabel));
    }

    [Fact]
    public void DoesNotSuggestArrowsMixedWithDeviceGeometryOrUnrelatedCells()
    {
        var library = Library();
        var cell = library.Cells["top"];
        cell.Elements.Add(Arrow(700, 0));
        cell.Elements.Add(new GdsPolygon { Layer = 700, Points = [new(0, 0), new(100, 0), new(100, 10), new(0, 10), new(0, 0)] });
        cell.Elements.Add(new GdsText { Layer = 701, Text = "a0", Position = new(.3, 0) });
        var unrelated = new GdsCell { Name = "unrelated" };
        unrelated.Elements.Add(Arrow(900, 0));
        unrelated.Elements.Add(new GdsText { Layer = 901, Text = "o1", Position = new(.3, 0) });
        library.Cells.Add(unrelated.Name, unrelated);
        Assert.Empty(GdsReviewPortEvidence.Suggest(library, "top"));
    }

    [Fact]
    public void MixedTextLayerDoesNotPromoteUncorroboratedAnnotations()
    {
        var library = Library(); var cell = library.Cells["top"];
        cell.Elements.Add(Arrow(700, 0));
        cell.Elements.Add(new GdsText { Layer = 701, Text = "a0", Position = new(.3, 0) });
        cell.Elements.Add(new GdsText { Layer = 701, Text = "drawing title", Position = new(100, 100) });
        Assert.DoesNotContain(GdsReviewPortEvidence.Suggest(library, "top"),
            x => x.Assignment.Role == GdsLayerRole.PortLabel);
    }

    [Fact]
    public void SourceDetectionUsesSingleArrowTipAndDirectionWithoutInventingPortsFromOtherArrows()
    {
        var library = Library(); var cell = library.Cells["top"];
        cell.Elements.Add(Arrow(700, 0)); cell.Elements.Add(Arrow(700, 50));
        cell.Elements.Add(new GdsText { Layer = 701, Text = "a0", Position = new(.3, 0) });
        var options = GdsLayerRoleMapping.Apply(new(), [new(700, 0, GdsLayerRole.PortMarker), new(701, 0, GdsLayerRole.PortLabel)]);
        var result = GdsReviewPinDetector.Detect(cell, new(-10, -10, 100, 10), options.PinDetection);
        var pin = Assert.Single(result.Pins);
        Assert.Equal("a0", pin.Name); Assert.Equal(10, pin.XUm); Assert.Equal(10, pin.YUm);
        Assert.Equal(180, pin.AngleDegrees); Assert.Null(pin.IsElectrical);
    }

    [Fact]
    public void SharedMarkerAndTextPairRetainsBothSuggestedRoles()
    {
        var library = Library(); var cell = library.Cells["top"];
        cell.Elements.Add(Arrow(700, 0));
        cell.Elements.Add(new GdsText { Layer = 700, Text = "a0", Position = new(.3, 0) });
        var suggestion = Assert.Single(GdsReviewPortEvidence.Suggest(library, "top"));
        Assert.Equal(GdsLayerRole.PortMarker | GdsLayerRole.PortLabel, suggestion.Assignment.Role);
    }

    [Fact]
    public void ElectricalKindIsProbedAtArrowTipBeforeOffsetLabel()
    {
        var library = Library(); var cell = library.Cells["top"];
        cell.Elements.Add(Arrow(700, 0));
        cell.Elements.Add(new GdsText { Layer = 701, Text = "a0", Position = new(1.5, 0) });
        cell.Elements.Add(new GdsPolygon { Layer = 800, Points = [new(-.1, -.1), new(.1, -.1), new(.1, .1), new(-.1, .1), new(-.1, -.1)] });
        var options = GdsLayerRoleMapping.Apply(new(), [new(700, 0, GdsLayerRole.PortMarker), new(701, 0, GdsLayerRole.PortLabel), new(800, 0, GdsLayerRole.Electrical)]);
        var pin = Assert.Single(GdsReviewPinDetector.Detect(cell, new(-10, -10, 100, 10), options.PinDetection).Pins);
        Assert.True(pin.IsElectrical); Assert.Equal(10, pin.XUm); Assert.Equal(180, pin.AngleDegrees);
    }

    [Fact]
    public void RotatedArrowsRemainRecognizableAfterNanometerGridRounding()
    {
        var library = Library(); var cell = library.Cells["top"];
        var source = Arrow(700, 0); const double angle = .015;
        cell.Elements.Add(source with
        {
            Points = source.Points.Select(p => new GdsPoint(
            Math.Round((p.X * Math.Cos(angle) - p.Y * Math.Sin(angle)) * 1000) / 1000,
            Math.Round((p.X * Math.Sin(angle) + p.Y * Math.Cos(angle)) * 1000) / 1000)).ToArray()
        });
        cell.Elements.Add(new GdsText { Layer = 701, Text = "a0", Position = new(.3, 0) });
        Assert.Equal(2, GdsReviewPortEvidence.Suggest(library, "top").Count);
    }

    [Fact]
    public void OversizedPathIsRejectedBeforeEnumeratingAndKeepsNamedPorts()
    {
        var library = Library(); var cell = library.Cells["top"];
        cell.Elements.Add(new GdsPath { Layer = 800, WidthMicrometers = 1, Points = new OversizedPoints() });
        cell.Elements.Add(new GdsText { Layer = 701, Text = "o1", Position = new(0, 0) });
        var options = GdsLayerRoleMapping.Apply(new(), [new(701, 0, GdsLayerRole.PortLabel), new(800, 0, GdsLayerRole.OpticalCore)]);
        var result = GdsReviewPinDetector.Detect(cell, new(-10, -10, 100, 10), options.PinDetection);
        Assert.True(result.Limited); Assert.Equal("o1", Assert.Single(result.Pins).Name);
    }

    private sealed class OversizedPoints : IReadOnlyList<GdsPoint>
    {
        public int Count => 1_000_000;
        public GdsPoint this[int index] => throw new InvalidOperationException("Must preflight before reading points.");
        public IEnumerator<GdsPoint> GetEnumerator() => throw new InvalidOperationException("Must preflight before enumerating.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static GdsLibrary Library()
    { var library = new GdsLibrary(); library.Cells.Add("top", new GdsCell { Name = "top" }); return library; }
    private static GdsPolygon Arrow(int layer, double x) => new()
    {
        Layer = layer,
        Points = [new(x, 0), new(x + .5, .5), new(x + .5, .25), new(x + .7, .25), new(x + .7, -.25), new(x + .5, -.25), new(x + .5, -.5), new(x, 0)]
    };
}
