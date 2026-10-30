using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP.Avalonia.ViewModels.PdkResolution;
using CAP_Core.Components.Creation;
using CAP_Core.Export.PdkResolution;
using CAP_Core.Preferences;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Moq;
using Shouldly;

namespace UnitTests.Architecture;

public sealed class BundledPdkConsumerBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lunima-catalog-ui-{Guid.NewGuid():N}");
    private string Source => Path.Combine(_root, "foundry.json");
    private UserPdkStore Store => new(Path.Combine(_root, "user"), new PdkJsonSaver(), new PdkLoader());
    private PdkDraft Draft => new() { Name = "Virtual Foundry", FilePath = Source,
        Process = new() { Name = "Virtual Process" } };
    private Mock<IBundledPdkCatalog> Catalog()
    {
        var catalog = new Mock<IBundledPdkCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.ReadInventory()).Returns(new BundledPdkInventory("virtual-directory", [Source]));
        catalog.Setup(value => value.IsBundledFile(It.IsAny<string>())).Returns((string path) => path == Source);
        return catalog;
    }

    [Fact]
    public void Library_LoadsTheInjectedInventory_WithoutRequiringARealBundledDirectory()
    {
        var catalog = Catalog();
        var reader = new Mock<IPdkReader>(MockBehavior.Strict);
        reader.Setup(value => value.LoadFromFile(Source)).Returns(Draft);
        var canvas = new DesignCanvasViewModel();
        var groups = Mock.Of<IGroupLibrary>();
        var manager = new PdkManagerViewModel();
        var preferences = new Mock<IPdkLibraryPreferences>();
        preferences.Setup(value => value.GetEnabledPdks()).Returns(new HashSet<string>());
        preferences.Setup(value => value.GetKnownPdks()).Returns(new HashSet<string>());
        preferences.Setup(value => value.GetUserPdkPaths()).Returns(new List<string>());
        var vm = new LeftPanelViewModel(catalog.Object, canvas, groups, reader.Object,
            preferences.Object, Mock.Of<IPanelLayoutPreferences>(),
            new HierarchyPanelViewModel(canvas), manager, new ComponentLibraryViewModel(groups), userPdkLibrary: Store)
            { UserPdkStartupRootOverride = Path.Combine(_root, "user") };
        try
        {
            vm.Initialize();
            manager.LoadedPdks.ShouldContain(pdk => pdk.Name == "Virtual Foundry" && pdk.IsBundled && pdk.FilePath == Source);
            reader.Verify(value => value.LoadFromFile(Source), Times.Once);
        }
        finally { canvas.DisposeDocumentBindings(); }
    }

    [Fact]
    public async Task ResolutionCheck_UsesInjectedFiles_WithoutProbingTheDirectory()
    {
        var reader = new Mock<IPdkReader>(MockBehavior.Strict);
        reader.Setup(value => value.LoadFromFileForEditing(Source)).Returns(Draft);
        var resolver = new Mock<PdkFunctionResolutionService>("python", "unused.py", null, null);
        var vm = new PdkResolutionCheckViewModel(Catalog().Object, reader.Object, resolver.Object);
        await vm.RunCheckCommand.ExecuteAsync(null);
        vm.Pdks.ShouldHaveSingleItem().PdkName.ShouldBe("Virtual Foundry");
        vm.HasFailures.ShouldBeFalse();
        reader.VerifyAll();
        resolver.Verify(value => value.ResolveAsync(It.IsAny<IReadOnlyList<PdkResolutionEntry>>(), default), Times.Never);
    }

    [Fact]
    public async Task ProcessEditor_ForksAClassifiedSource_ThenSavesTheForkDirectly()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Source, "protected source");
        var draft = Draft;
        var process = draft.Process;
        var vm = new ProcessManagementViewModel(Catalog().Object, Mock.Of<IFileDialogService>(),
            Array.Empty<IProcessImporter>(), new PdkJsonSaver(), Store);
        vm.LoadForSinglePdkEdit(draft);
        vm.PdkFilePathResolver = _ => Source;
        vm.MetalMayCrossPhotonic = false;
        string? fork = null;
        vm.BundledPdkForkSaved += (_, args) => fork = args.ForkPath;
        await vm.SaveProcessCommand.ExecuteAsync(null);
        fork.ShouldNotBeNull();
        File.Exists(fork).ShouldBeTrue();
        File.ReadAllText(Source).ShouldBe("protected source");
        draft.Process.ShouldBeSameAs(process);
        process!.ElectricalBridgeRequired.ShouldBeNull();
        vm.MetalMayCrossPhotonic = true;
        await vm.SaveProcessCommand.ExecuteAsync(null);
        new PdkLoader().LoadFromFileForEditing(fork).Process!.ElectricalBridgeRequired.ShouldBeNull();
        File.ReadAllText(Source).ShouldBe("protected source");
    }

    [Fact]
    public async Task OffsetEditor_ForksClassifiedSource_EvenWithoutBundledRegistryRow()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Source, "protected source");
        var reader = new Mock<IPdkReader>();
        reader.Setup(value => value.LoadFromFileForEditing(Source)).Returns(Draft);
        var dialogs = new Mock<IFileDialogService>();
        dialogs.Setup(value => value.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(Source);
        var vm = new PdkOffsetEditorViewModel(Catalog().Object, reader.Object, new PdkJsonSaver(),
            new PdkManagerViewModel(), userPdkStore: Store) { FileDialogService = dialogs.Object };
        string? fork = null;
        vm.BundledPdkForkSaved = (_, path) => fork = path;
        await vm.LoadPdkFileCommand.ExecuteAsync(null);
        vm.SavePdkCommand.Execute(null);
        fork.ShouldNotBeNull();
        File.Exists(fork).ShouldBeTrue();
        File.ReadAllText(Source).ShouldBe("protected source");
        string? directSave = null;
        vm.UserPdkSaved = (_, path) => directSave = path;
        vm.SavePdkCommand.Execute(null);
        directSave.ShouldBe(fork);
        File.ReadAllText(Source).ShouldBe("protected source");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
