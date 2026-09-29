using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.Library;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Tests for the click-to-assign half of <see cref="GdsImportDialogViewModel"/>
/// (issue #1173): the picker builds over the analyzed top cell, seeds from the
/// current field state, and its decisions move pairs between the layer fields.
/// </summary>
[Collection("LocalizationSingleton")]
public class GdsImportDialogLayerPickerTests : IDisposable
{
    /// <summary>The picker's legend text is localized; pin English so assertions
    /// stay culture-independent regardless of the CI/dev OS language.</summary>
    public GdsImportDialogLayerPickerTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-gdspicker-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        _host.Dispose();
    }

    /// <summary>Same shape as the census fixture: a waveguide instance, a route
    /// path on the unknown (37,0), and port-style texts on (56,0).</summary>
    private static byte[] FoundryStyleLibrary() => GdsTestWriter.Create()
        .StandardPrologue()
        .BeginCell("TOP")
            .SRef("wg", 0, 0)
            .Path(37, 0, 500, 0, (0, 0), (50000, 0))
        .EndCell()
        .BeginCell("wg")
            .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
            .Text(56, 0, "opt_in", 0, 2000)
            .Text(56, 0, "opt_out", 10000, 2000)
        .EndCell()
        .EndLibrary()
        .ToArray();

    private async Task<GdsImportDialogViewModel> AnalyzedDialog()
    {
        Directory.CreateDirectory(_root);
        var gdsPath = Path.Combine(_root, "picker.gds");
        File.WriteAllBytes(gdsPath, FoundryStyleLibrary());
        var executor = new GdsPlacementExecutor(
            new DesignCanvasViewModel(), new CommandManager(), () => new List<ComponentTemplate>());
        var vm = new GdsImportDialogViewModel(gdsPath, _host.CreateService(), executor);
        await vm.StartAnalysisAsync();
        vm.HasError.ShouldBeFalse(vm.ErrorText);
        return vm;
    }

    [Fact]
    public async Task CreateLayerPicker_BuildsSceneOverSelectedTopCell()
    {
        var vm = await AnalyzedDialog();

        var picker = vm.CreateLayerPicker();

        picker.ShouldNotBeNull();
        picker.Scene.CellName.ShouldBe("TOP");
        picker.Rows.Select(r => r.Pair).ShouldContain(new GdsLayerPair(37, 0));
        picker.Rows.Select(r => r.Pair).ShouldContain(new GdsLayerPair(56, 0));
    }

    [Fact]
    public async Task CreateLayerPicker_WithoutSelectedTopCell_ReturnsNull()
    {
        var vm = await AnalyzedDialog();
        vm.SelectedTopCell = null;

        vm.CreateLayerPicker().ShouldBeNull();
    }

    [Fact]
    public async Task CreateLayerPicker_SeedsAssignmentsFromCurrentFields()
    {
        var vm = await AnalyzedDialog();
        vm.WaveguideLayersText = "1,0";
        vm.MetalLayersText = "37,0";

        var picker = vm.CreateLayerPicker()!;

        picker.Rows.Single(r => r.Pair == new GdsLayerPair(1, 0)).AssignmentText.ShouldBe("waveguide");
        picker.Rows.Single(r => r.Pair == new GdsLayerPair(37, 0)).AssignmentText.ShouldBe("metal");
    }

    [Fact]
    public async Task PickedAssignment_AppendsPairToTargetField()
    {
        var vm = await AnalyzedDialog();
        vm.MetalLayersText = "";
        var picker = vm.CreateLayerPicker()!;

        // Click the route path drawn on (37,0): 0.5 µm wide around y = 0.
        picker.PickAt(25, 0, 0.05);
        picker.SelectedRow!.Pair.ShouldBe(new GdsLayerPair(37, 0));
        picker.AssignMetalCommand.Execute(null);

        GdsImportDialogViewModel.ParseLayerPairs(vm.MetalLayersText)!
            .ShouldContain((37, 0));
    }

    [Fact]
    public async Task PickedAssignment_MovesPairBetweenFields()
    {
        var vm = await AnalyzedDialog();
        vm.WaveguideLayersText = "";
        vm.MetalLayersText = "1,0";
        var picker = vm.CreateLayerPicker()!;

        // Click inside the referenced waveguide boundary (0..10 µm × 1.75..2.25 µm).
        picker.PickAt(5, 2, 0.05);
        picker.SelectedRow!.Pair.ShouldBe(new GdsLayerPair(1, 0));
        picker.AssignWaveguideCommand.Execute(null);

        GdsImportDialogViewModel.ParseLayerPairs(vm.MetalLayersText)!.ShouldNotContain((1, 0));
        GdsImportDialogViewModel.ParseLayerPairs(vm.WaveguideLayersText)!.ShouldContain((1, 0));
    }

    [Fact]
    public async Task PickedIgnore_RemovesPairFromEveryField()
    {
        var vm = await AnalyzedDialog();
        vm.PortLayersText = "56,0";
        var picker = vm.CreateLayerPicker()!;

        // Click the "opt_out" label anchor at (10 µm, 2 µm): the text wins there.
        picker.PickAt(10, 2, 1.0);
        picker.SelectedRow!.Pair.ShouldBe(new GdsLayerPair(56, 0));
        picker.AssignIgnoreCommand.Execute(null);

        GdsImportDialogViewModel.ParseLayerPairs(vm.PortLayersText)!.ShouldNotContain((56, 0));
    }
}
