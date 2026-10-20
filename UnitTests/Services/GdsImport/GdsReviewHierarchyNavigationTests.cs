using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public sealed class GdsReviewHierarchyNavigationTests
{
    [Fact]
    public async Task NamedChildCanBeOpenedWithoutAccidentallySelectingFirstPadAndUndoRestoresBoundary()
    {
        var vm = await Create();
        vm.StageIndex = 2;
        Assert.Equal(new[] { "pad", "MMI", "MZI" }, vm.ChildCells.Select(x => x.CellName));
        var mmi = vm.ChildCells.Single(x => x.CellName == "MMI");
        Assert.Equal(6, mmi.PlacementCount);
        vm.SelectedChild = mmi;
        vm.OpenSelectedChildCommand.Execute(null);
        Assert.Equal("MMI", vm.SelectedCell!.CellName);
        Assert.True(vm.Cells.Single(x => x.CellName == "assembly").ExpandChildren);
        Assert.Equal("top / assembly / MMI", vm.HierarchyPath);
        Assert.Empty(vm.ChildCells);
        vm.ParentAssemblyCommand.Execute(null);
        Assert.Equal("assembly", vm.SelectedCell!.CellName);
        Assert.Equal(3, vm.ActiveCells.Count);
        vm.UndoCommand.Execute(null);
        Assert.Equal("assembly", Assert.Single(vm.ActiveCells).CellName);
        vm.Close();
    }

    [Fact]
    public async Task ChildListTracksSelectionAndArrayPlacementsWithoutChangingImportBoundary()
    {
        var vm = await Create();
        vm.StageIndex = 2;
        Assert.Equal(1, vm.ChildCells.Single(x => x.CellName == "MZI").ChildTypeCount);
        Assert.Equal("assembly", Assert.Single(vm.ActiveCells).CellName);
        vm.SelectedChild = vm.ChildCells.Single(x => x.CellName == "MZI");
        vm.OpenSelectedChildCommand.Execute(null);
        Assert.Equal("MMI", Assert.Single(vm.ChildCells).CellName);
        vm.SelectedChild = vm.ChildCells[0];
        vm.OpenSelectedChildCommand.Execute(null);
        Assert.Equal("top / assembly / MZI / MMI", vm.HierarchyPath);
        vm.ParentAssemblyCommand.Execute(null);
        Assert.Equal("MZI", vm.SelectedCell!.CellName);
        vm.Close();
    }

    private static Task<GdsInterpretationReviewViewModel> Create()
    {
        var lib = new GdsLibrary();
        foreach (var name in new[] { "top", "assembly", "pad", "MMI", "MZI" })
            lib.Cells.Add(name, new GdsCell { Name = name });
        lib.Cells["top"].Elements.Add(new GdsReference { CellName = "assembly" });
        lib.Cells["assembly"].Elements.AddRange([
            new GdsReference { CellName = "pad", Columns = 10 },
            new GdsReference { CellName = "MMI", Columns = 2, Rows = 3 },
            new GdsReference { CellName = "MZI" }]);
        lib.Cells["MZI"].Elements.Add(new GdsReference { CellName = "MMI" });
        lib.Cells["MMI"].Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0,0), new(10,0), new(10,2), new(0,2)] });
        return GdsInterpretationReviewViewModel.CreateAsync(lib, "top", "test", [], [],
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    }
}
