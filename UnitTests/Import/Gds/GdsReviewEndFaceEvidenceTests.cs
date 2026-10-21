using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Xunit;

namespace UnitTests.Import.Gds;

public class GdsReviewEndFaceEvidenceTests
{
    [Fact]
    public void TwoPortNamesAtNarrowEndFacesSuggestOnlyLabelsOnArbitraryLayer()
    {
        var library = Library();
        var suggestion = Assert.Single(GdsReviewPortEvidence.Suggest(library, "top"));
        Assert.Equal(new GdsLayerAssignment(987, 4, GdsLayerRole.PortLabel), suggestion.Assignment);
        Assert.Equal(2, suggestion.MatchedLabelCount);
    }

    [Theory]
    [InlineData("drawing title")]
    [InlineData("revision2")]
    public void AnnotationMixedIntoLabelPairPreventsAutomaticSuggestion(string annotation)
    {
        var library = Library();
        library.Cells["top"].Elements.Add(new GdsText { Layer = 987, TextType = 4, Text = annotation, Position = new(5, 5) });
        Assert.Empty(GdsReviewPortEvidence.Suggest(library, "top"));
    }

    [Fact]
    public void OneCorroboratedNameIsInsufficient()
    {
        var library = Library();
        library.Cells["top"].Elements.RemoveAll(e => e is GdsText t && t.Text == "o2");
        Assert.Empty(GdsReviewPortEvidence.Suggest(library, "top"));
    }

    [Fact]
    public void InteriorLabelsAndWideOutlineFacesDoNotProvePorts()
    {
        var library = Library();
        var cell = library.Cells["top"];
        cell.Elements.RemoveAll(e => e is GdsPolygon);
        cell.Elements.Add(new GdsPolygon { Layer = 986, Points = [new(0, -20), new(40, -20), new(40, 20), new(0, 20), new(0, -20)] });
        Assert.Empty(GdsReviewPortEvidence.Suggest(library, "top"));
    }

    [Fact]
    public async Task RealGdsfactoryFixtureKeepsItsLabelOnlyPortsWithoutAssumingMaterialRoles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Tools", "gds-test-data", "gdsfactory_mzi_like.gds")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        await using var stream = File.OpenRead(Path.Combine(directory!.FullName, "Tools", "gds-test-data", "gdsfactory_mzi_like.gds"));
        var library = await new GdsReader().ReadAsync(stream);
        var suggestion = Assert.Single(GdsReviewPortEvidence.Suggest(library, "gdsfactory_mzi_like"));
        Assert.Equal(new GdsLayerAssignment(1, 10, GdsLayerRole.PortLabel), suggestion.Assignment);
        Assert.Equal(10, suggestion.MatchedLabelCount);
        var options = GdsLayerRoleMapping.Apply(new(), [suggestion.Assignment]);
        var builder = new GdsReviewSceneBuilder(library);
        foreach (var cell in library.Cells.Values)
        {
            var expected = cell.Elements.OfType<GdsText>().Select(t => t.Text).Order().ToArray();
            var detected = GdsReviewPinDetector.Detect(cell, builder.GetBounds(cell.Name), options.PinDetection);
            Assert.Equal(expected, detected.Pins.Select(p => p.Name).Order().ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryPortShapedLabelMustBeCorroboratedIncludingReferencedCells(bool inChild)
    {
        var library = Library(); var owner = library.Cells["top"];
        if (inChild)
        {
            owner = new GdsCell { Name = "child" }; library.Cells.Add(owner.Name, owner);
            library.Cells["top"].Elements.Add(new GdsReference { CellName = owner.Name });
        }
        owner.Elements.Add(new GdsText { Layer = 987, TextType = 4, Text = "o99", Position = new(20, 0) });
        Assert.Empty(GdsReviewPortEvidence.Suggest(library, "top"));
    }

    [Fact]
    public void UnclosedPolygonRingStillCorroboratesItsClosingEndFace()
    {
        var library = Library(); var cell = library.Cells["top"];
        var polygon = cell.Elements.OfType<GdsPolygon>().Single();
        cell.Elements.Remove(polygon); cell.Elements.Add(polygon with { Points = polygon.Points.Take(4).ToArray() });
        Assert.Single(GdsReviewPortEvidence.Suggest(library, "top"));
    }

    [Fact]
    public void ParentLabelsAreCorroboratedByTransformedVerifiedChildAnchors()
    {
        var library = Library(); var child = library.Cells["top"]; child.Name = "child";
        library.Cells.Clear(); library.Cells.Add("child", child);
        var top = new GdsCell { Name = "top" }; library.Cells.Add(top.Name, top);
        top.Elements.Add(new GdsReference { CellName = "child", Offset = new(100, 10), AngleDegrees = 90, Magnification = 2, Reflected = true });
        top.Elements.Add(new GdsText { Layer = 987, TextType = 4, Text = "in0", Position = new(100, 10) });
        top.Elements.Add(new GdsText { Layer = 987, TextType = 4, Text = "out0", Position = new(100, 90) });
        Assert.Equal(4, Assert.Single(GdsReviewPortEvidence.Suggest(library, "top")).MatchedLabelCount);
    }

    private static GdsLibrary Library()
    {
        var library = new GdsLibrary(); var cell = new GdsCell { Name = "top" };
        cell.Elements.Add(new GdsPolygon { Layer = 986, Points = [new(0, -.25), new(40, -.25), new(40, .25), new(0, .25), new(0, -.25)] });
        cell.Elements.Add(new GdsText { Layer = 987, TextType = 4, Text = "o1", Position = new(0, 0) });
        cell.Elements.Add(new GdsText { Layer = 987, TextType = 4, Text = "o2", Position = new(40, 0) });
        library.Cells.Add(cell.Name, cell); return library;
    }
}
