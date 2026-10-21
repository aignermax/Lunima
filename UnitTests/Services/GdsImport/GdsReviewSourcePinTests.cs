using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsReviewSourcePinTests
{
    [Fact]
    public void EditingPositionDoesNotTurnUnknownSignalDomainIntoAuthoritativeOptical()
    {
        var row = new CAP.Avalonia.ViewModels.GdsImport.Interpretation.GdsPortReviewRow(
            new DetectedPin { Name = "a0", IsElectrical = null });
        Assert.True(row.NeedsKindReview);
        Assert.Null(row.ToPin().IsElectrical);
        row.XUm = 4;
        Assert.Null(row.ToPin().IsElectrical);
        Assert.Null(row.ToPin().PortType);
        row.Kind = "electrical";
        Assert.False(row.NeedsKindReview);
        Assert.True(row.ToPin().IsElectrical);
        row.Kind = "optical";
        Assert.False(row.ToPin().IsElectrical);
    }

    [Fact]
    public async Task PreviewVertexLimitDoesNotSuppressOwnNamedPorts()
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "top" };
        // Decoration consumes the preview budget before the meaningful port geometry.
        top.Elements.Add(new GdsPolygon
        {
            Layer = 99,
            Points = Enumerable.Range(0, 250002)
            .Select(i => new GdsPoint(50 + 10 * Math.Cos(i * .01), 50 + 10 * Math.Sin(i * .01))).ToArray()
        });
        top.Elements.Add(new GdsText { Layer = 235, Text = "o1", Position = new(0, 0) });
        top.Elements.Add(new GdsText { Layer = 235, Text = "o2", Position = new(100, 0) });
        top.Elements.Add(new GdsPolygon { Layer = 401, Points = [new(0, -1), new(100, -1), new(100, 1), new(0, 1), new(0, -1)] });
        library.Cells.Add(top.Name, top);
        var vm = await GdsInterpretationReviewViewModel.CreateAsync(library, "top", "test",
            [new(235, 0, GdsLayerRole.PortLabel), new(401, 0, GdsLayerRole.OpticalCore)], [],
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        Assert.True(vm.Scene.IsLimited);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "o1", "o2" }, vm.Cells.Single().Ports.Select(p => p.Name).Order().ToArray());
        Assert.False(vm.Cells.Single().DetectionLimited);
        vm.Close();
    }
}
