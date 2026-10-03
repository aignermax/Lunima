using System.Collections.ObjectModel;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Export;
using Moq;
using Shouldly;

namespace UnitTests.Persistence;

/// <summary>
/// .lun round-trip contract of the coherent interference mode (issue #1333):
/// the flag survives save → reload through the real file path, legacy files
/// without the field load with the mode off, and the JSON property name stays
/// stable. Off is never written — it is the default, so old files round-trip
/// byte-identically in this field.
/// </summary>
public class CoherentPropagationPhasePersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ObservableCollection<ComponentTemplate> _library =
        new(TestPdkLoader.LoadAllTemplates());

    [Fact]
    public void Roundtrip_WithModeOn_PreservesFlag()
    {
        var original = new DesignFileData
        {
            FormatVersion = "2.0",
            CoherentPropagationPhase = true,
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundtripped = JsonSerializer.Deserialize<DesignFileData>(json);

        roundtripped.ShouldNotBeNull();
        roundtripped!.CoherentPropagationPhase.ShouldBe(true);
    }

    [Fact]
    public void LegacyFileWithoutField_DeserializesWithModeOff()
    {
        const string legacyJson = """
            {
              "FormatVersion": "2.0",
              "Components": [],
              "Connections": []
            }
            """;

        var data = JsonSerializer.Deserialize<DesignFileData>(legacyJson);

        data.ShouldNotBeNull();
        data!.CoherentPropagationPhase.ShouldBeNull("missing field = mode off");
    }

    [Fact]
    public void Serialization_UsesStableJsonPropertyName()
    {
        var data = new DesignFileData { CoherentPropagationPhase = true };

        var json = JsonSerializer.Serialize(data, JsonOptions);

        // Pin the on-disk property name — renaming it would break existing user files.
        json.ShouldContain("\"CoherentPropagationPhase\":true");
    }

    [Fact]
    public void Serialization_OmitsFieldWhenModeOff()
    {
        var data = new DesignFileData();

        var json = JsonSerializer.Serialize(data, JsonOptions);

        json.ShouldNotContain("CoherentPropagationPhase");
    }

    [Fact]
    public async Task SaveLoadRoundtrip_ThroughRealFilePath_KeepsFlag()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"coherent_{Guid.NewGuid():N}.lun");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();
            saveCanvas.ConnectionManager.EnableCoherentPropagationPhase = true;
            await SaveToFile(saveVm, tempFile);

            File.ReadAllText(tempFile).ShouldContain("\"CoherentPropagationPhase\": true");

            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeTrue(
                "a saved-on flag must come back on after load");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SaveLoadRoundtrip_ModeOff_WritesNoField_AndLoadsOff()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"coherent_{Guid.NewGuid():N}.lun");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();
            await SaveToFile(saveVm, tempFile);

            File.ReadAllText(tempFile).ShouldNotContain("CoherentPropagationPhase");

            var (loadVm, loadCanvas) = CreateSetup();
            loadCanvas.ConnectionManager.EnableCoherentPropagationPhase = true; // dirty pre-state
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeFalse(
                "a file without the field must load with the mode off");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Load_RestoresFlag_AndFiresCoherentModeRestoredCallback()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"coherent_{Guid.NewGuid():N}.lun");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();
            saveCanvas.ConnectionManager.EnableCoherentPropagationPhase = true;
            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas) = CreateSetup();
            int callbackCount = 0;
            loadVm.CoherentModeRestoredAfterLoad = () => callbackCount++;
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeTrue();
            callbackCount.ShouldBe(1,
                "the load must fire the callback so views can re-sync their toggle");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private (FileOperationsViewModel vm, DesignCanvasViewModel canvas) CreateSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            _library,
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!);
        return (vm, canvas);
    }

    private static async Task SaveToFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("Design file must be created during save");
    }

    private static async Task LoadFromFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.LoadDesignCommand.ExecuteAsync(null);
    }
}
