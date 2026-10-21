using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsInterpretationReviewTests
{
    [Fact]
    public async Task EditingConfirmedRole_InvalidatesReview_AndUndoRestoresAssignment()
    {
        var vm = await Create();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.LayersConfirmed);
        vm.Layers[0].Role = GdsLayerRole.Auxiliary;
        Assert.False(vm.LayersConfirmed);
        vm.UndoCommand.Execute(null);
        Assert.Equal(GdsLayerRole.OpticalCore, vm.Layers[0].Role);
    }

    [Fact]
    public async Task SnapshotIncludesExplicitUnknownLayer_AndDoesNotModifySource()
    {
        var vm = await Create();
        var snapshot = vm.BuildInterpretation();
        Assert.Equal("top", snapshot.TopCellName);
        Assert.Equal(2, snapshot.Layers.Count);
        Assert.Contains(snapshot.Layers, x => x.Layer == 99 && x.Role == GdsLayerRole.Unknown);
        Assert.False(vm.CanApply);
    }

    [Fact]
    public async Task TourCanBePausedAndRestarted()
    {
        var vm = await Create();
        Assert.True(vm.GuideEnabled);
        vm.PauseGuideCommand.Execute(null);
        Assert.False(vm.GuideEnabled);
        vm.RestartGuideCommand.Execute(null);
        Assert.True(vm.GuideEnabled);
        Assert.False(string.IsNullOrWhiteSpace(vm.GuideTargetName));
    }

    [Fact]
    public async Task PortFrameIncludesAnnotationsLikeTheImporter()
    {
        var vm = await Create();
        Assert.Equal(4, vm.Cells[0].Bounds.Height);
        vm.StageIndex = 3;
        vm.AddPortCommand.Execute(null);
        Assert.Equal(2, vm.SelectedPort!.YUm);
        Assert.Equal(4, vm.Scene.Bounds.Height);
    }

    [Fact]
    public async Task CannotApplyBySkippingUnconfirmedSteps()
    {
        var vm = await Create();
        var applied = false;
        vm.ApplyRequested = () => applied = true;
        vm.StageIndex = 5;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.False(applied);
        Assert.Equal(0, vm.StageIndex);
        for (var i = 0; i < 5; i++) await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.CanApply);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(applied);
    }

    [Fact]
    public async Task MovingPortIsOneUndoableChange()
    {
        var vm = await Create();
        vm.AddPortCommand.Execute(null);
        var before = vm.SelectedPort!.ToPin();
        vm.MovePort(0, 5, .25);
        vm.UndoCommand.Execute(null);
        Assert.Equal(before.XUm, vm.SelectedPort!.XUm);
        Assert.Equal(before.YUm, vm.SelectedPort.YUm);
    }

    private static Task<GdsInterpretationReviewViewModel> Create()
    {
        var lib = new GdsLibrary { Name = "test" };
        var top = new GdsCell { Name = "top" };
        top.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(20, 0), new(20, 2), new(0, 2), new(0, 0)] });
        top.Elements.Add(new GdsText { Layer = 99, Text = "annotation", Position = new(4, 4) });
        lib.Cells.Add("top", top);
        return GdsInterpretationReviewViewModel.CreateAsync(lib, "top", "test-hash",
            [new GdsLayerAssignment(1, 0, GdsLayerRole.OpticalCore)], Array.Empty<GdsModelCandidate>(),
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), "gds-review-test-" + Guid.NewGuid() + ".json")));
    }
}
