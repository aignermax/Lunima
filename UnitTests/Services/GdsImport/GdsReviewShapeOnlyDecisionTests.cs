using System.Text.Json;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Issue #1572, review side: pressing "Shape only · no simulation" on a
/// 2-optical-pin cell records an explicit per-cell decision — the pass-through
/// hint and Summary row disappear, the Summary lists the cell as shape-only,
/// and the decision round-trips through the persisted interpretation. Choosing
/// a model afterwards retracts the decision; Undo restores the pass-through.
/// </summary>
public sealed class GdsReviewShapeOnlyDecisionTests
{
    [Fact]
    public async Task ShapeOnlyButtonOptsOutOfTheAssumedPassThrough()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        var cell = vm.Cells[0];
        cell.IsPassThroughAssumed.ShouldBeTrue("fixture: two optical pins, no mapped model");

        vm.StageIndex = 4;
        vm.SelectedCell = cell;
        vm.GeometryOnlyCommand.Execute(null);

        cell.GeometryOnly.ShouldBeTrue();
        cell.IsShapeOnly.ShouldBeTrue();
        cell.IsPassThroughAssumed.ShouldBeFalse("the explicit decision hides the pass-through hint");
        cell.PassThroughSummaryText.ShouldBeEmpty();
        cell.GeometryOnlySummaryText.ShouldContain(cell.DisplayName);
        vm.HasPassThroughCells.ShouldBeFalse();
        vm.HasGeometryOnlyCells.ShouldBeTrue();
        vm.HasModelDecisionRows.ShouldBeTrue("the Summary lists the cell as shape-only");
        vm.BuildInterpretation().Cells.Single(c => c.CellName == cell.CellName)
            .GeometryOnly.ShouldBeTrue("the decision persists into the interpretation");
    }

    [Fact]
    public async Task ChoosingAModelRetractsTheShapeOnlyDecision()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        var cell = vm.Cells[0];
        vm.StageIndex = 4;
        vm.SelectedCell = cell;
        vm.GeometryOnlyCommand.Execute(null);
        cell.IsShapeOnly.ShouldBeTrue();

        cell.SelectedModel = new GdsModelCandidate("pdk", "model", cell.Ports.Select(p => p.Name).ToArray());

        cell.GeometryOnly.ShouldBeFalse("the mapped model wins");
        cell.IsShapeOnly.ShouldBeFalse();
        cell.GeometryOnlySummaryText.ShouldBeEmpty();
        vm.BuildInterpretation().Cells.Single().GeometryOnly.ShouldBeFalse();
    }

    [Fact]
    public async Task UndoRestoresTheAssumedPassThrough()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        var cell = vm.Cells[0];
        vm.StageIndex = 4;
        vm.SelectedCell = cell;
        vm.GeometryOnlyCommand.Execute(null);
        cell.IsShapeOnly.ShouldBeTrue();

        vm.UndoCommand.Execute(null);

        cell.GeometryOnly.ShouldBeFalse();
        cell.IsPassThroughAssumed.ShouldBeTrue("undo returns to the unmapped default");
    }

    [Fact]
    public async Task SavedInterpretationRestoresTheShapeOnlyDecision()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        vm.StageIndex = 4;
        vm.SelectedCell = vm.Cells[0];
        vm.GeometryOnlyCommand.Execute(null);
        var saved = JsonSerializer.Deserialize<GdsImportInterpretation>(
            JsonSerializer.Serialize(vm.BuildInterpretation()))!;

        var reopened = await Create();
        await reopened.NextCommand.ExecuteAsync(null);
        await reopened.NextCommand.ExecuteAsync(null);
        reopened.OfferSavedInterpretation(saved);
        reopened.ReuseSavedCommand.Execute(null);

        var cell = reopened.Cells[0];
        cell.GeometryOnly.ShouldBeTrue("the decision survives save/load of the review");
        cell.IsShapeOnly.ShouldBeTrue();
        cell.IsPassThroughAssumed.ShouldBeFalse();
        reopened.HasGeometryOnlyCells.ShouldBeTrue();
    }

    private static Task<GdsInterpretationReviewViewModel> Create()
    {
        var library = new GdsLibrary();
        var leaf = new GdsCell { Name = "leaf" };
        leaf.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(20, 0), new(20, 4), new(0, 4)] });
        leaf.Elements.Add(new GdsText { Layer = 56, Text = "o1", Position = new(0, 2) });
        leaf.Elements.Add(new GdsText { Layer = 56, Text = "o2", Position = new(20, 2) });
        library.Cells.Add(leaf.Name, leaf);
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsReference { CellName = "leaf" });
        library.Cells.Add(top.Name, top);
        return GdsInterpretationReviewViewModel.CreateAsync(library, "TOP", "shape-only-decision",
            [new(1, 0, GdsLayerRole.OpticalCore), new(56, 0, GdsLayerRole.PortLabel)], [],
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), "gds-review-so-" + Guid.NewGuid() + ".json")));
    }
}
