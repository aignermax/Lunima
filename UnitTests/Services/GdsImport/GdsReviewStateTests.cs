using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public sealed class GdsReviewStateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gds-review-state-" + Guid.NewGuid());
    private GdsLayerProfileStore Store => new(Path.Combine(_directory, "profiles.json"));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private async Task<GdsInterpretationReviewViewModel> Create(bool nested = false)
    {
        var library = new GdsLibrary();
        var leaf = new GdsCell { Name = "leaf" };
        leaf.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(20, 0), new(20, 4), new(0, 4)] });
        leaf.Elements.Add(new GdsText { Layer = 56, Text = "o1", Position = new(0, 2) });
        leaf.Elements.Add(new GdsText { Layer = 56, Text = "o2", Position = new(20, 2) });
        library.Cells.Add(leaf.Name, leaf);
        var top = new GdsCell { Name = "TOP" };
        if (nested)
        {
            var parent = new GdsCell { Name = "parent" };
            parent.Elements.Add(new GdsReference { CellName = "leaf" });
            library.Cells.Add(parent.Name, parent);
            top.Elements.Add(new GdsReference { CellName = "parent" });
        }
        else top.Elements.Add(new GdsReference { CellName = "leaf" });
        library.Cells.Add(top.Name, top);
        return await GdsInterpretationReviewViewModel.CreateAsync(library, "TOP", "hash",
            [new(1, 0, GdsLayerRole.OpticalCore), new(56, 0, GdsLayerRole.PortLabel)], [], Store);
    }

    [Fact]
    public async Task UndoBeforeDetectionDoesNotSuppressLaterDetectedPins()
    {
        var vm = await Create();
        vm.Layers[0].Role = GdsLayerRole.Auxiliary;
        vm.UndoCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Contains(vm.Cells[0].Ports, p => p.Name == "o1");
        Assert.Contains(vm.Cells[0].Ports, p => p.Name == "o2");
    }

    [Fact]
    public async Task SplitExposesChildrenAndCollapseRestoresParent()
    {
        var vm = await Create(nested: true);
        vm.StageIndex = 2;
        Assert.Equal("parent", Assert.Single(vm.DisplayCells).CellName);
        var parent = vm.Cells.Single(x => x.CellName == "parent");
        parent.ExpandChildren = true;
        Assert.Equal(2, vm.DisplayCells.Count);
        Assert.Equal("leaf", Assert.Single(vm.ActiveCells).CellName);
        vm.StageIndex = 3;
        Assert.Equal("leaf", Assert.Single(vm.DisplayCells).CellName);
        Assert.Equal("leaf", vm.SelectedCell!.CellName);
        parent.ExpandChildren = false;
        Assert.Equal("parent", vm.SelectedCell!.CellName);
    }

    [Fact]
    public async Task SavedInterpretationIsExplicitAndMustBeConfirmedAgain()
    {
        var original = await Create();
        original.Cells[0].DisplayName = "Reviewed coupler";
        var saved = original.BuildInterpretation();
        var reopened = await Create();
        reopened.OfferSavedInterpretation(saved);
        Assert.True(reopened.HasSavedInterpretation);
        Assert.Equal("leaf", reopened.Cells[0].DisplayName);
        reopened.ReuseSavedCommand.Execute(null);
        Assert.Equal("Reviewed coupler", reopened.Cells[0].DisplayName);
        Assert.False(reopened.CanApply);
        Assert.False(reopened.LayersConfirmed);
        reopened.OfferSavedInterpretation(saved with { SourceSha256 = "different" });
        Assert.False(reopened.HasSavedInterpretation);
    }

    [Fact]
    public async Task ProfilesAndGuidePreferenceSurviveReopeningWithoutAutoConfirmation()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        vm.ProfileName = "My lab";
        vm.SaveProfileCommand.Execute(null);
        vm.PauseGuideCommand.Execute(null);
        var reopened = await Create();
        Assert.False(reopened.GuideEnabled);
        reopened.RestartGuideCommand.Execute(null);
        Assert.True(reopened.GuideEnabled);
        reopened.SelectedProfile = Assert.Single(reopened.Profiles);
        reopened.LoadProfileCommand.Execute(null);
        Assert.Equal(1, reopened.StageIndex);
        Assert.False(reopened.LayersConfirmed);
        Assert.Equal(GdsLayerRole.PortLabel, reopened.Layers.Single(x => x.Layer == 56).Role);
    }

    [Fact]
    public async Task ClosedReviewCannotAdvanceOrApply()
    {
        var vm = await Create();
        for (var i = 0; i < 5; i++) await vm.NextCommand.ExecuteAsync(null);
        var applied = false;
        vm.ApplyRequested = () => applied = true;
        vm.Close();
        await vm.NextCommand.ExecuteAsync(null);
        Assert.False(applied);
        Assert.False(vm.GuideEnabled);
    }

    [Fact]
    public async Task IncompatibleModelDomainsAreRejectedBeforeLeavingReview()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        var cell = vm.Cells[0];
        var names = cell.Ports.Select(p => p.Name).ToArray();
        cell.SelectedModel = new GdsModelCandidate("test", "model", names)
        { PinDomains = names.ToDictionary(n => n, n => "electrical") };
        vm.StageIndex = 4;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.StageIndex);
        Assert.False(vm.CanApply);
        foreach (var port in cell.Ports) port.Kind = "electrical";
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(5, vm.StageIndex);
    }

    [Fact]
    public async Task PortNamesAreUniqueIgnoringCaseLikePersistedModels()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        vm.Cells[0].Ports[0].Name = "o1";
        vm.Cells[0].Ports[1].Name = "O1";
        vm.StageIndex = 3;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.StageIndex);
    }

    [Fact]
    public async Task WholeDragIsOneUndoStep()
    {
        var vm = await Create();
        vm.AddPortCommand.Execute(null);
        var start = vm.SelectedPort!.ToPin();
        vm.BeginPortDrag();
        vm.MovePort(0, 2, 1);
        vm.MovePort(0, 8, 2);
        vm.MovePort(0, 12, 3);
        vm.EndPortDrag();
        vm.UndoCommand.Execute(null);
        Assert.Equal(start.XUm, vm.SelectedPort!.XUm);
        Assert.Equal(start.YUm, vm.SelectedPort.YUm);
    }
}
