using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsOccurrenceGroupingTests
{
    [Fact]
    public async Task SelectingTwoOccurrencesDoesNotSelectOtherCopiesAndGroupIsUndoable()
    {
        var vm = await Create();
        vm.StageIndex = 2;
        var occurrences = vm.Occurrences.ToArray();
        Assert.Equal(4, occurrences.Length);
        vm.PickPlacement([new(occurrences[0].CellName, occurrences[0].Transform)], false);
        vm.PickPlacement([new(occurrences[1].CellName, occurrences[1].Transform)], true);
        Assert.Equal(2, vm.SelectedOccurrenceBounds.Count);
        vm.GroupName = "diode";
        vm.CreateOccurrenceGroupCommand.Execute(null);
        var group = Assert.Single(vm.BuildInterpretation().Groups);
        Assert.Equal(new[] { "front#0", "rear#0" }, group.InstanceNames);
        Assert.Empty(group.CellNames);
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.BuildInterpretation().Groups);
        vm.Close();
    }

    [Fact]
    public async Task RegroupingKeepsUnselectedMembersTogether()
    {
        var vm = await Create(); vm.StageIndex = 2; vm.GroupName = "all";
        foreach (var item in vm.Occurrences)
            vm.PickPlacement([new(item.CellName, item.Transform)], true);
        vm.CreateOccurrenceGroupCommand.Execute(null);
        var first = vm.Occurrences[0]; var second = vm.Occurrences[1];
        vm.PickPlacement([new(first.CellName, first.Transform)], false);
        vm.PickPlacement([new(second.CellName, second.Transform)], true);
        vm.GroupName = "diode"; vm.CreateOccurrenceGroupCommand.Execute(null);
        var groups = vm.BuildInterpretation().Groups;
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "front#1", "rear#1" }, groups.Single(x => x.Name == "all").InstanceNames);
        Assert.Equal(new[] { "front#0", "rear#0" }, groups.Single(x => x.Name == "diode").InstanceNames);
        vm.Close();
    }

    [Fact]
    public async Task RegroupingLegacyCellTypeGroupPreservesRemainingOccurrences()
    {
        var vm = await Create(); vm.StageIndex = 2; vm.GroupName = "legacy";
        foreach (var cell in vm.ActiveCells) cell.IncludeInGroup = true;
        vm.GroupCellsCommand.Execute(null);
        var first = vm.Occurrences[0]; var second = vm.Occurrences[1];
        vm.PickPlacement([new(first.CellName, first.Transform)], false);
        vm.PickPlacement([new(second.CellName, second.Transform)], true);
        vm.GroupName = "diode"; vm.CreateOccurrenceGroupCommand.Execute(null);
        var groups = vm.BuildInterpretation().Groups;
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "front#1", "rear#1" }, groups.Single(x => x.Name == "legacy").InstanceNames);
        vm.Close();
    }

    internal static async Task<GdsInterpretationReviewViewModel> Create()
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "top" };
        foreach (var (name, x) in new[] { ("front", 0d), ("rear", 10d), ("front", 40d), ("rear", 50d) })
            top.Elements.Add(new GdsReference { CellName = name, Offset = new(x, 0) });
        library.Cells.Add("top", top);
        foreach (var name in new[] { "front", "rear" })
        {
            var cell = new GdsCell { Name = name };
            cell.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(8, 0), new(8, 8), new(0, 8)] });
            library.Cells.Add(name, cell);
        }
        return await GdsInterpretationReviewViewModel.CreateAsync(library, "top", "occurrences", [], [],
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), $"gds-occurrence-{Guid.NewGuid():N}.json")));
    }
}
