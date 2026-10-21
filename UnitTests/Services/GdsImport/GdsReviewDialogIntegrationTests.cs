using System.Diagnostics;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>Native review-to-canvas journeys with real import, source bytes and design persistence.</summary>
public sealed class GdsReviewDialogIntegrationTests : IDisposable
{
    private readonly GdsDesignScopeTestHost _host = new();
    private readonly List<GdsImportDialogViewModel> _dialogs = [];
    public void Dispose()
    {
        foreach (var dialog in _dialogs) dialog.OnWindowClosed();
        _host.Dispose();
    }

    [Fact]
    public async Task ConfirmedReviewSurvivesAutoCloseAndImportsTheReviewedSnapshot()
    {
        var bytes = Library();
        var (vm, canvas) = Create(bytes);
        await vm.StartAnalysisAsync();
        vm.HasError.ShouldBeFalse(vm.ErrorText);
        var review = vm.Review.ShouldNotBeNull();
        ConfigureRoles(review);
        await review.NextCommand.ExecuteAsync(null);
        await review.NextCommand.ExecuteAsync(null);
        var cell = review.Cells.Single(c => c.CellName == "deviceA");
        var input = cell.Ports.Single(p => p.Name == "in");
        input.Name = "reviewed_input";
        input.XUm = 0.25;
        input.WidthUm = 0.7;
        input.Kind = "vertical_te";
        await ConfirmReview(review);
        vm.CanImport.ShouldBeTrue(review.Message);
        canvas.Components.ShouldBeEmpty();
        _host.Scope.Sets.ShouldBeEmpty();

        // The view invokes OnWindowClosed synchronously when the import auto-closes it.
        var closeCount = 0;
        vm.OnClose = () => { closeCount++; vm.OnWindowClosed(); };
        await File.WriteAllBytesAsync(vm.GdsFilePath, [0, 1, 2]);
        await vm.ImportCommand.ExecuteAsync(null);

        vm.HasError.ShouldBeFalse(vm.ErrorText);
        vm.ImportCompleted.ShouldBeTrue(vm.ErrorText);
        closeCount.ShouldBe(1);
        review.GuideEnabled.ShouldBeFalse();
        canvas.Components.Count.ShouldBe(2);
        var set = _host.Scope.Sets.ShouldHaveSingleItem();
        set.GdsBytes.ShouldBe(bytes);
        var interpretation = set.Interpretation.ShouldNotBeNull();
        interpretation.TopCellName.ShouldBe("TOP_A");
        interpretation.Layers.Single(l => l.Layer == 1 && l.Datatype == 10).Role.ShouldBe(GdsLayerRole.PortLabel);
        var savedPin = interpretation.Cells.Single(c => c.CellName == "deviceA").Pins!
            .Single(p => p.Name == "reviewed_input");
        savedPin.XUm.ShouldBe(0.25);
        savedPin.PortType.ShouldBe("vertical_te");
        var placedPin = canvas.Components.SelectMany(c => c.Component.PhysicalPins)
            .Single(p => p.Name == "reviewed_input");
        placedPin.WaveguideWidthMicrometers.ShouldBe(0.7);
        placedPin.PortType.ShouldBe("vertical_te");

        var payload = JsonSerializer.Serialize(_host.Scope.CaptureForSave([set.PdkName]));
        _host.Scope.RestoreDesignScope(JsonSerializer.Deserialize<List<ImportedGdsComponentSetData>>(payload));
        _host.Scope.Sets.Single().Interpretation!.Cells.Single(c => c.CellName == "deviceA")
            .Pins!.Single(p => p.Name == "reviewed_input").XUm.ShouldBe(0.25);
    }

    [Fact]
    public async Task CancellingReviewLeavesCanvasAndDesignScopeUnchanged()
    {
        var (vm, canvas) = Create(Library());
        await vm.StartAnalysisAsync();
        var review = vm.Review.ShouldNotBeNull();
        ConfigureRoles(review);
        await ConfirmReview(review);
        var closeCount = 0;
        vm.OnClose = () => { closeCount++; vm.OnWindowClosed(); };
        vm.CancelCommand.Execute(null);
        await review.NextCommand.ExecuteAsync(null);
        closeCount.ShouldBe(1);
        vm.ImportCompleted.ShouldBeFalse();
        canvas.Components.ShouldBeEmpty();
        canvas.Connections.ShouldBeEmpty();
        _host.Scope.Sets.ShouldBeEmpty();
    }

    [Fact]
    public async Task SwitchingTopCellInvalidatesConfirmedReviewAndItsApplyCallback()
    {
        var (vm, canvas) = Create(Library());
        await vm.StartAnalysisAsync();
        var oldReview = vm.Review.ShouldNotBeNull();
        ConfigureRoles(oldReview);
        await ConfirmReview(oldReview);
        vm.CanImport.ShouldBeTrue();
        vm.SelectedTopCell = vm.TopCells.Single(t => t.CellName == "TOP_B");
        vm.CanImport.ShouldBeFalse();
        await WaitUntil(() => !vm.IsBusy);
        vm.HasError.ShouldBeFalse(vm.ErrorText);
        var replacement = vm.Review.ShouldNotBeNull();
        replacement.ShouldNotBeSameAs(oldReview);
        replacement.BuildInterpretation().TopCellName.ShouldBe("TOP_B");
        replacement.CanApply.ShouldBeFalse();
        oldReview.ApplyRequested.ShouldBeNull();
        await oldReview.NextCommand.ExecuteAsync(null);
        canvas.Components.ShouldBeEmpty();
        _host.Scope.Sets.ShouldBeEmpty();
    }

    [Fact]
    public async Task RapidTopCellSwitchKeepsAReviewForTheLatestSelection()
    {
        var (vm, canvas) = Create(Library());
        await vm.StartAnalysisAsync();
        var first = vm.TopCells.Single(t => t.CellName == "TOP_A");
        vm.SelectedTopCell = vm.TopCells.Single(t => t.CellName == "TOP_B");
        vm.SelectedTopCell = first;
        await WaitUntil(() => !vm.IsBusy);
        vm.HasError.ShouldBeFalse(vm.ErrorText);
        vm.Review.ShouldNotBeNull().BuildInterpretation().TopCellName.ShouldBe("TOP_A");
        canvas.Components.ShouldBeEmpty();
    }

    [Fact]
    public async Task FlatReviewedLayoutImportsItsPortsAndPersistsItsInterpretation()
    {
        var bytes = GdsTestWriter.Create().StandardPrologue().BeginCell("FLAT")
            .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
            .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
            .Text(1, 10, "input", 0, 2000).Text(1, 10, "output", 10000, 2000)
            .EndCell().EndLibrary().ToArray();
        var (vm, canvas) = Create(bytes);
        await vm.StartAnalysisAsync();
        var review = vm.Review.ShouldNotBeNull();
        review.Cells.Single().CellName.ShouldBe("FLAT");
        ConfigureRoles(review);
        await ConfirmReview(review);
        vm.OnClose = vm.OnWindowClosed;
        await vm.ImportCommand.ExecuteAsync(null);
        vm.HasError.ShouldBeFalse(vm.ErrorText);
        vm.ImportCompleted.ShouldBeTrue(vm.ErrorText);
        var placed = canvas.Components.ShouldHaveSingleItem();
        placed.Component.PhysicalPins.Select(p => p.Name).ShouldContain("input");
        placed.Component.PhysicalPins.Select(p => p.Name).ShouldContain("output");
        var set = _host.Scope.Sets.ShouldHaveSingleItem();
        set.GdsBytes.ShouldBe(bytes);
        set.Interpretation.ShouldNotBeNull().Cells.Single().CellName.ShouldBe("FLAT");
        set.Drafts.Single().Pins.Count.ShouldBe(2);
        _host.Scope.CaptureForSave([set.PdkName]).ShouldNotBeNull().Count.ShouldBe(1);
    }

    private (GdsImportDialogViewModel Vm, DesignCanvasViewModel Canvas) Create(byte[] bytes)
    {
        Directory.CreateDirectory(_host.GdsCacheDirectory);
        var path = Path.Combine(_host.GdsCacheDirectory, "review-source.gds");
        File.WriteAllBytes(path, bytes);
        var canvas = new DesignCanvasViewModel();
        var executor = new GdsPlacementExecutor(canvas, new CommandManager(), () => _host.Templates.ToList());
        var vm = new GdsImportDialogViewModel(path, _host.CreateService(), executor)
        {
            EnableReview = true, GroupImportRequested = false,
            ReviewProfileStore=new GdsLayerProfileStore(Path.Combine(_host.GdsCacheDirectory,"review-profiles.json"))
        };
        _dialogs.Add(vm);
        return (vm, canvas);
    }

    private static void ConfigureRoles(GdsInterpretationReviewViewModel review)
    {
        foreach (var layer in review.Layers)
            layer.Role = layer.Layer == 1 && layer.Datatype == 0 ? GdsLayerRole.OpticalCore
                : layer.Layer == 1 && layer.Datatype == 10 ? GdsLayerRole.PortLabel : GdsLayerRole.Auxiliary;
    }

    private static async Task ConfirmReview(GdsInterpretationReviewViewModel review)
    {
        review.StageIndex = 0;
        for (var step = 0; step < 5; step++)
        {
            review.StageIndex.ShouldBe(step, review.Message);
            await review.NextCommand.ExecuteAsync(null);
        }
        review.StageIndex.ShouldBe(5, review.Message);
        review.CanApply.ShouldBeTrue(review.Message);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate() && elapsed.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        predicate().ShouldBeTrue("the review refresh must complete");
    }

    private static byte[] Library()
    {
        var writer = GdsTestWriter.Create().StandardPrologue()
            .BeginCell("TOP_A").SRef("deviceA", 0, 0).SRef("deviceB", 20000, 0).EndCell()
            .BeginCell("TOP_B").SRef("deviceB", 0, 0).SRef("deviceA", 30000, 0).EndCell();
        foreach (var name in new[] { "deviceA", "deviceB" })
            writer.BeginCell(name)
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "in", 0, 2000).Text(1, 10, "out", 10000, 2000).EndCell();
        return writer.EndLibrary().ToArray();
    }
}
