using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Regression tests for issue #311: Components lose HumanReadableName after disk save/load in prefab instances.
/// Root cause: CreateFromTemplate did not set HumanReadableName, so prefab templates stored null.
/// On instantiation, RenameComponentsWithSequentialNames fell back to NazcaFunctionName.
/// </summary>
public class PrefabHumanReadableNameTests : IDisposable
{
    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly string _testLibraryPath;

    /// <summary>Initializes test fixtures with component library and temp directory.</summary>
    public PrefabHumanReadableNameTests()
    {
        _library = new ObservableCollection<ComponentTemplate>(
            TestPdkLoader.LoadAllTemplates());
        _testLibraryPath = Path.Combine(
            Path.GetTempPath(),
            $"PrefabNameTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_testLibraryPath))
            Directory.Delete(_testLibraryPath, true);
    }

    /// <summary>
    /// Verifies the core fix: CreateFromTemplate now sets HumanReadableName to the template display name.
    /// </summary>
    [Fact]
    public void CreateFromTemplate_SetsHumanReadableName_ToTemplateName()
    {
        // Arrange
        var template = _library.First(t => t.Name == "1x2 MMI Splitter");

        // Act
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        // Assert
        component.HumanReadableName.ShouldBe(
            "1x2 MMI Splitter",
            "HumanReadableName must equal the template display name so prefabs show the correct name");
    }

    /// <summary>
    /// Verifies that a prefab instantiated from a GroupLibraryManager has human-readable names,
    /// not NazcaFunctionName-based fallback names.
    /// </summary>
    [Fact]
    public void InstantiatePrefab_PreservesHumanReadableName_NotNazcaFunctionName()
    {
        // Arrange: Create a component that would come from the PDK (simulate NazcaFunctionName != Name)
        var template = _library.First(t => t.Name == "Grating Coupler");
        var component = ComponentTemplates.CreateFromTemplate(template, 10, 20);

        // Create a group with this component and save as template
        var group = new ComponentGroup("TestPrefab")
        {
            PhysicalX = 10,
            PhysicalY = 20
        };
        group.AddChild(component);

        var libraryManager = new GroupLibraryManager(_testLibraryPath);
        libraryManager.SaveTemplate(group, "My Grating Coupler Prefab");

        // Act: Load and instantiate the template
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();
        var template2 = loadManager.Templates.Single();
        var instance = loadManager.InstantiateTemplate(template2, 50, 50);

        // Assert: Child HumanReadableName should be display name, NOT NazcaFunctionName
        var child = instance.ChildComponents.Single();
        child.HumanReadableName.ShouldNotBeNull(
            "HumanReadableName should not be null after prefab instantiation");
        child.HumanReadableName!.Contains("Grating Coupler").ShouldBeTrue(
            "HumanReadableName should be based on the template display name, not NazcaFunctionName");
        child.HumanReadableName.ShouldNotBe(
            child.NazcaFunctionName,
            "HumanReadableName should not equal the raw NazcaFunctionName");
    }

    /// <summary>
    /// End-to-end test: create prefab instance, save to .lun, load, verify HumanReadableName preserved.
    /// This is the exact workflow described in issue #311.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_PrefabInstance_PreservesHumanReadableName()
    {
        // Arrange: Create component and build prefab
        var template = _library.First(t => t.Name == "1x2 MMI Splitter");
        var component = ComponentTemplates.CreateFromTemplate(template, 100, 100);

        var group = new ComponentGroup("PrefabGroup")
        {
            PhysicalX = 100,
            PhysicalY = 100
        };
        group.AddChild(component);

        var libraryManager = new GroupLibraryManager(_testLibraryPath);
        libraryManager.SaveTemplate(group, "My Splitter Prefab");

        // Reload library and instantiate prefab (simulates step 5: instantiate from library)
        var loadLibrary = new GroupLibraryManager(_testLibraryPath);
        loadLibrary.LoadTemplates();
        var groupTemplate = loadLibrary.Templates.Single();
        var prefabInstance = loadLibrary.InstantiateTemplate(groupTemplate, 200, 200);

        // Set up FileOperationsViewModel for save/load
        var (saveVm, saveCanvas) = CreateFileOperationsSetup();
        saveCanvas.AddComponent(prefabInstance);

        var tempFile = Path.Combine(Path.GetTempPath(), $"prefab_test_{Guid.NewGuid()}.cappro");

        try
        {
            // Capture HumanReadableName before save
            var childBeforeSave = prefabInstance.ChildComponents.Single();
            var nameBeforeSave = childBeforeSave.HumanReadableName;
            nameBeforeSave.ShouldNotBeNull(
                "HumanReadableName should be set before save (fix to CreateFromTemplate)");

            // Act: Save to file (step 6)
            var saveDialog = new Mock<IFileDialogService>();
            saveDialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tempFile);
            saveVm.FileDialogService = saveDialog.Object;
            await saveVm.SaveDesignAsCommand.ExecuteAsync(null);

            // Verify file contains the HumanReadableName
            var json = await File.ReadAllTextAsync(tempFile);
            json.Contains(nameBeforeSave).ShouldBeTrue(
                "Saved file should contain the HumanReadableName");

            // Act: Load from file (step 7)
            var (loadVm, loadCanvas) = CreateFileOperationsSetup();
            var loadDialog = new Mock<IFileDialogService>();
            loadDialog.Setup(f => f.ShowOpenFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tempFile);
            loadVm.FileDialogService = loadDialog.Object;
            await loadVm.LoadDesignCommand.ExecuteAsync(null);

            // Assert: HumanReadableName preserved after load (step 8)
            var loadedGroupVm = loadCanvas.Components
                .Single(c => c.Component is ComponentGroup);
            var loadedGroup = (ComponentGroup)loadedGroupVm.Component;
            var loadedChild = loadedGroup.ChildComponents.Single();

            loadedChild.HumanReadableName.ShouldBe(
                nameBeforeSave,
                "HumanReadableName must be preserved through save/load cycle");
            loadedChild.HumanReadableName.ShouldNotBe(
                loadedChild.NazcaFunctionName,
                "HumanReadableName should not fall back to NazcaFunctionName after load");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that HumanReadableName is stable through a save/load cycle for a newly created prefab.
    /// Specifically: name set at instantiation time must survive file persistence.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_NewPrefabInstance_HumanReadableNameStaysSameAfterRoundTrip()
    {
        // Arrange: Create component with CreateFromTemplate (now sets HumanReadableName)
        var template = _library.First(t => t.Name == "Grating Coupler");
        var component = ComponentTemplates.CreateFromTemplate(template, 50, 50);

        var group = new ComponentGroup("RoundTripGroup") { PhysicalX = 50, PhysicalY = 50 };
        group.AddChild(component);

        var libraryManager = new GroupLibraryManager(_testLibraryPath);
        libraryManager.SaveTemplate(group, "Round Trip Prefab");

        var loadLibrary = new GroupLibraryManager(_testLibraryPath);
        loadLibrary.LoadTemplates();
        var groupTemplate = loadLibrary.Templates.Single();
        var prefabInstance = loadLibrary.InstantiateTemplate(groupTemplate, 100, 100);

        var nameAfterInstantiation = prefabInstance.ChildComponents.Single().HumanReadableName;
        nameAfterInstantiation.ShouldNotBeNull("Name must be set after instantiation");

        // Act: Save and reload
        var (saveVm, saveCanvas) = CreateFileOperationsSetup();
        saveCanvas.AddComponent(prefabInstance);
        var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_{Guid.NewGuid()}.cappro");

        try
        {
            var saveDialog = new Mock<IFileDialogService>();
            saveDialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tempFile);
            saveVm.FileDialogService = saveDialog.Object;
            await saveVm.SaveDesignAsCommand.ExecuteAsync(null);

            var (loadVm, loadCanvas) = CreateFileOperationsSetup();
            var loadDialog = new Mock<IFileDialogService>();
            loadDialog.Setup(f => f.ShowOpenFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tempFile);
            loadVm.FileDialogService = loadDialog.Object;
            await loadVm.LoadDesignCommand.ExecuteAsync(null);

            // Assert: Same name before and after round trip
            var loadedGroup = (ComponentGroup)loadCanvas.Components
                .Single(c => c.Component is ComponentGroup).Component;
            var loadedChild = loadedGroup.ChildComponents.Single();

            loadedChild.HumanReadableName.ShouldBe(
                nameAfterInstantiation,
                "HumanReadableName must be identical after save/load round trip");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>Creates a FileOperationsViewModel backed by the real component library.</summary>
    private (FileOperationsViewModel vm, DesignCanvasViewModel canvas) CreateFileOperationsSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var nazcaExporter = new SimpleNazcaExporter();
        var gdsExport = new GdsExportViewModel(new CAP_Core.Export.GdsExportService());

        var vm = new FileOperationsViewModel(
            canvas, commandManager, nazcaExporter, _library, gdsExport);

        return (vm, canvas);
    }
}
