using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// The process editor's waveguide companion layers: loaded from the process, editable as
/// rows (offset / total width as text, empty = not declared), and written back on save.
/// </summary>
public class ProcessManagementCompanionLayerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "companion-editor-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* temp cleanup best effort */ }
    }

    [Fact]
    public void Load_ShowsTheProcessCompanionLayers()
    {
        var vm = CreateVm();

        vm.Load(ProcessWithTrench());

        var row = vm.CompanionLayers.ShouldHaveSingleItem();
        row.Name.ShouldBe("TRENCH");
        row.Layer.ShouldBe(2);
        row.Role.ShouldBe(WaveguideCompanionRole.Trench);
        row.OffsetText.ShouldBe("8");
        row.WidthText.ShouldBeEmpty();
    }

    [Fact]
    public void RowEdits_WriteThroughToTheProcess()
    {
        var vm = CreateVm();
        vm.Load(ProcessWithTrench());
        var row = vm.CompanionLayers.Single();

        row.Role = WaveguideCompanionRole.KeepOut;
        row.OffsetText = "";
        row.WidthText = "12,5";

        var layer = vm.ToProcess().WaveguideCompanionLayers.ShouldNotBeNull().ShouldHaveSingleItem();
        layer.Role.ShouldBe(WaveguideCompanionRole.KeepOut);
        layer.OffsetUm.ShouldBeNull();
        layer.WidthUm.ShouldBe(12.5);
    }

    [Fact]
    public void InvalidNumber_KeepsTheLastValidValue()
    {
        var vm = CreateVm();
        vm.Load(ProcessWithTrench());
        var row = vm.CompanionLayers.Single();

        row.OffsetText = "abc";
        row.OffsetText = "-3";

        row.Model.OffsetUm.ShouldBe(8.0);
    }

    [Fact]
    public void AddAndRemove_ManageRows_EmptyListSavesAsAbsent()
    {
        var vm = CreateVm();
        vm.Load(new ProcessDefinition { Name = "P", Layers = { new ProcessLayer { Name = "WG", Layer = 1 } } });

        vm.AddCompanionLayerCommand.Execute(null);
        var added = vm.CompanionLayers.ShouldHaveSingleItem();
        added.Layer.ShouldBe(2, "the next free layer number");
        added.Model.OffsetUm.ShouldBe(2.0);

        vm.RemoveCompanionLayerCommand.Execute(added);
        vm.CompanionLayers.ShouldBeEmpty();
        vm.ToProcess().WaveguideCompanionLayers.ShouldBeNull();
    }

    [Fact]
    public async Task SaveProcess_WritesCompanionLayersToThePdkFile()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "custom.json");
        var vm = CreateVm();
        var draft = new PdkDraft { Name = "Custom", Process = new ProcessDefinition { Name = "Custom" } };
        vm.LoadForSinglePdkEdit(draft);
        vm.PdkFilePathResolver = _ => path;
        vm.AddCompanionLayerCommand.Execute(null);
        vm.CompanionLayers.Single().Role = WaveguideCompanionRole.KeepOut;

        await vm.SaveProcessCommand.ExecuteAsync(null);

        var saved = new PdkLoader().LoadFromFile(path);
        saved.Process!.WaveguideCompanionLayers.ShouldNotBeNull()
            .ShouldHaveSingleItem().Role.ShouldBe(WaveguideCompanionRole.KeepOut);
    }

    private ProcessManagementViewModel CreateVm() =>
        new(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory), Mock.Of<IFileDialogService>(), Array.Empty<IProcessImporter>(), new PdkJsonSaver(),
            userPdkStore: new UserPdkStore(Path.Combine(_tempDir, "user-pdks"), new PdkJsonSaver(), new PdkLoader()));

    private static ProcessDefinition ProcessWithTrench() => new()
    {
        Name = "P",
        WaveguideCompanionLayers = new List<ProcessCompanionLayer>
        {
            new() { Name = "TRENCH", Layer = 2, Datatype = 0, Role = WaveguideCompanionRole.Trench, OffsetUm = 8.0 },
        },
    };
}
