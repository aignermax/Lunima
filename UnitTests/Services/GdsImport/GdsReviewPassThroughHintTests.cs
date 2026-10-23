using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Issue #1570: the review must surface the assumed lossless pass-through model
/// for an unmapped 2-optical-pin cell — the same rule the import applies
/// (<see cref="GdsImportService.IsPassThroughEligible(IEnumerable{string?})"/>),
/// never a silently assumed model.
/// </summary>
public sealed class GdsReviewPassThroughHintTests
{
    [Theory]
    [InlineData(new[] { "optical", "optical" }, true)]
    [InlineData(new[] { "optical", null }, true)]
    [InlineData(new[] { "Electrical", "optical" }, false)]
    [InlineData(new[] { "optical" }, false)]
    [InlineData(new[] { "optical", "optical", "optical" }, false)]
    [InlineData(new string?[] { }, false)]
    public void ImportRuleMatchesPinCountAndDomain(string?[] pinKinds, bool expected) =>
        GdsImportService.IsPassThroughEligible(pinKinds).ShouldBe(expected);

    [Fact]
    public async Task TwoOpticalPinsWithoutModelShowTheAssumedPassThrough()
    {
        var vm = await Create();
        vm.Cells[0].IsPassThroughAssumed.ShouldBeFalse("pin detection has not run yet");
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        var cell = vm.Cells[0];
        cell.Ports.Count.ShouldBe(2);
        cell.Ports.ShouldAllBe(p => p.Kind == "optical");
        cell.IsPassThroughAssumed.ShouldBeTrue();
        cell.PassThroughSummaryText.ShouldBe(string.Format(
            CAP.Avalonia.Services.Localization.LocalizationService.Instance.Translate("GdsReview.PassThroughSummaryRow"),
            cell.DisplayName));
        vm.HasPassThroughCells.ShouldBeTrue();
    }

    [Fact]
    public async Task ThreeOpticalPinsStayGeometryOnlyAndShowNoHint()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        vm.AddPortCommand.Execute(null);
        var cell = vm.Cells[0];
        cell.Ports.Count.ShouldBe(3);
        cell.Ports.ShouldAllBe(p => p.Kind == "optical");
        cell.IsPassThroughAssumed.ShouldBeFalse();
        cell.PassThroughSummaryText.ShouldBeEmpty();
        vm.HasPassThroughCells.ShouldBeFalse();
    }

    [Fact]
    public async Task ElectricalPinMakesTheCellIneligibleForTheHint()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        var cell = vm.Cells[0];
        cell.Ports[1].Kind = "electrical";
        cell.IsPassThroughAssumed.ShouldBeFalse("an electrical pin means the cell needs a real model");
        vm.HasPassThroughCells.ShouldBeFalse();
    }

    [Fact]
    public async Task MappedModelHidesTheHint()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        var cell = vm.Cells[0];
        cell.SelectedModel = new GdsModelCandidate("pdk", "model", cell.Ports.Select(p => p.Name).ToArray());
        cell.IsPassThroughAssumed.ShouldBeFalse("an explicit model replaces the assumed pass-through");
        cell.PassThroughSummaryText.ShouldBeEmpty();
        vm.HasPassThroughCells.ShouldBeFalse();
        cell.SelectedModel = null;
        cell.IsPassThroughAssumed.ShouldBeTrue("unmapping restores the assumed pass-through hint");
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
        return GdsInterpretationReviewViewModel.CreateAsync(library, "TOP", "pass-through-hint",
            [new(1, 0, GdsLayerRole.OpticalCore), new(56, 0, GdsLayerRole.PortLabel)], [],
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), "gds-review-pt-" + Guid.NewGuid() + ".json")));
    }
}
