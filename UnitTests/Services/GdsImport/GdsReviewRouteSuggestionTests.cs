using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsReviewRouteSuggestionTests
{
    [Fact]
    public async Task ConfirmingRouteKindIsUndoableAndInvalidatesLayerConfirmation()
    {
        var library = new GdsLibrary { Name = "test" };
        var top = new GdsCell { Name = "top" };
        top.Elements.Add(new GdsPath { Layer = 5, WidthMicrometers = 1, Points = [new(0, 0), new(20, 0)] });
        library.Cells.Add("top", top);
        var vm = await Create(library);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.Layers);
        row.ApplyElectricalSuggestionCommand.Execute(null);
        Assert.Equal(GdsLayerRole.Electrical, row.Role);
        Assert.False(vm.LayersConfirmed);
        vm.UndoCommand.Execute(null);
        Assert.Equal(GdsLayerRole.Unknown, row.Role);
    }

    [Fact]
    public async Task ReachableRouteGeometryOffersChoiceWithoutAssigningMaterial()
    {
        var library = new GdsLibrary { Name = "test" };
        var top = new GdsCell { Name = "top" };
        top.Elements.Add(new GdsReference { CellName = "route" });
        var route = new GdsCell { Name = "route" };
        route.Elements.Add(new GdsPolygon { Layer = 987, Points = [new(0, 0), new(20, 0), new(20, 1), new(0, 1), new(0, 0)] });
        route.Elements.Add(new GdsText { Layer = 99, Text = "e1", Position = new(0, .5) });
        library.Cells.Add("top", top);
        library.Cells.Add("route", route);
        var vm = await Create(library);
        var row = vm.Layers.Single(x => x.Layer == 987);
        Assert.Equal(GdsLayerRole.Unknown, row.Role);
        Assert.True(row.HasRouteSuggestion);
        Assert.Equal(GdsLayerRole.Unknown, vm.Layers.Single(x => x.Layer == 99).Role);
    }

    [Fact]
    public async Task CandidateNavigationSkipsAnnotationsAndPreservesAssignments()
    {
        var library = new GdsLibrary { Name = "test" };
        var top = new GdsCell { Name = "top" };
        foreach (var layer in new[] { 2, 7 })
            top.Elements.Add(new GdsPath { Layer = layer, WidthMicrometers = 1, Points = [new(0, 0), new(20, 0)] });
        top.Elements.Add(new GdsText { Layer = 3, Text = "annotation" });
        library.Cells.Add("top", top);
        var vm = await Create(library);
        Assert.Equal(2, vm.SuggestedRouteLayerCount);
        vm.NextSuggestedRouteCommand.Execute(null);
        Assert.Equal(7, vm.SelectedLayer!.Layer);
        vm.NextSuggestedRouteCommand.Execute(null);
        Assert.Equal(2, vm.SelectedLayer!.Layer);
        vm.SelectedLayer.ApplyOpticalSuggestionCommand.Execute(null);
        Assert.Equal(GdsLayerRole.OpticalCore, vm.BuildInterpretation().Layers.Single(x => x.Layer == 2).Role);
        Assert.Equal(GdsLayerRole.Unknown, vm.Layers.Single(x => x.Layer == 3).Role);
    }

    [Fact]
    public void SourceEvidenceDoesNotCountUnreachableCellsOrRepeatedInstances()
    {
        var library = new GdsLibrary { Name = "test" };
        var top = new GdsCell { Name = "top" };
        top.Elements.Add(new GdsReference { CellName = "child", Columns = 100, Rows = 100 });
        top.Elements.Add(new GdsReference { CellName = "child" });
        var child = new GdsCell { Name = "child" };
        child.Elements.Add(new GdsPath { Layer = 987, WidthMicrometers = 1, Points = [new(0, 0), new(20, 0)] });
        var unused = new GdsCell { Name = "unused" };
        unused.Elements.Add(new GdsPath { Layer = 12, WidthMicrometers = 1, Points = [new(0, 0), new(20, 0)] });
        library.Cells.Add("top", top); library.Cells.Add("child", child); library.Cells.Add("unused", unused);
        var evidence = Assert.Single(GdsReviewRouteEvidence.Suggest(library, "top"));
        Assert.Equal(987, evidence.Layer);
        Assert.Equal(1, evidence.ShapeCount);
        Assert.Equal(1, evidence.CellCount);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => GdsReviewRouteEvidence.Suggest(library, "top", cancellation.Token));
    }

    private static Task<GdsInterpretationReviewViewModel> Create(GdsLibrary library) =>
        GdsInterpretationReviewViewModel.CreateAsync(library, "top", "hash", [], [],
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
}
