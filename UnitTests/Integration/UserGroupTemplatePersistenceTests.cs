using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Routing;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for the UserGroup template workflow:
/// Create group → Save as template → Place template → Ungroup → Save design → Load design.
/// Covers issue #284: Components from ungrouped UserGroup templates are lost during save/load.
/// </summary>
public class UserGroupTemplatePersistenceTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;

    /// <summary>
    /// Initializes the test suite with the real component library.
    /// </summary>
    public UserGroupTemplatePersistenceTests()
    {
        _library = new ObservableCollection<ComponentTemplate>(
            TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// Reproduces issue #284:
    /// After placing a UserGroup template on canvas and ungrouping it,
    /// saving and reloading the design should preserve both child components.
    /// This test fails before the fix and passes after.
    /// </summary>
    [Fact]
    public async Task UngroupedUserGroupTemplate_SaveAndLoad_PreservesBothComponents()
    {
        var tempLibDir = Path.Combine(Path.GetTempPath(), $"grouplib_{Guid.NewGuid():N}");
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_ungroup_{Guid.NewGuid():N}.cappro");

        try
        {
            // === PHASE 1: Create a ComponentGroup and save it as a UserGroup template ===
            var libraryManager = new GroupLibraryManager(tempLibDir);

            var mmiTemplate = _library.First(t => t.Name == "1x2 MMI Splitter");
            var comp1 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 100, 100);
            var comp2 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 300, 100);

            var group = new ComponentGroup("TestUserGroup")
            {
                PhysicalX = 100,
                PhysicalY = 100,
                Description = "User group for persistence test"
            };
            group.AddChild(comp1);
            group.AddChild(comp2);

            // Add a frozen internal path between the two components
            var routedPath = new RoutedPath();
            routedPath.Segments.Add(new StraightSegment(180, 127.5, 300, 127.5, 0));
            group.AddInternalPath(new FrozenWaveguidePath
            {
                Path = routedPath,
                StartPin = comp1.PhysicalPins.First(p => p.Name == "out1"),
                EndPin = comp2.PhysicalPins.First(p => p.Name == "in")
            });

            var savedTemplate = libraryManager.SaveTemplate(
                group, "TestUserGroup", "Created for issue #284 test");

            savedTemplate.ShouldNotBeNull("Template should be saved successfully");
            savedTemplate.ComponentCount.ShouldBe(2);

            // === PHASE 2: Place the template on a fresh canvas and ungroup it ===
            var (phase2Vm, phase2Canvas) = CreateFileOperationsSetup();

            var instance = libraryManager.InstantiateTemplate(savedTemplate, 100, 100);
            phase2Canvas.AddComponent(instance);

            // Verify the placed instance has child components
            instance.ChildComponents.Count.ShouldBe(2,
                "Instantiated group should have 2 child components");

            // Ungroup the placed instance
            var ungroupCmd = new UngroupCommand(phase2Canvas, instance);
            ungroupCmd.Execute();

            // Verify ungrouping succeeded: 2 standalone components, no groups
            phase2Canvas.Components.Count.ShouldBe(2,
                "Canvas should have 2 standalone components after ungroup");
            phase2Canvas.Components.Any(c => c.Component is ComponentGroup).ShouldBeFalse(
                "There should be no groups on canvas after ungroup");

            // === PHASE 3: Save the design to a .cappro file ===
            var saveDialog = new Mock<IFileDialogService>();
            saveDialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tempFile);
            phase2Vm.FileDialogService = saveDialog.Object;

            await phase2Vm.SaveDesignAsCommand.ExecuteAsync(null);

            File.Exists(tempFile).ShouldBeTrue("Design file should be created");

            // The bug manifests here: if TemplateName is saved as the HumanReadableName
            // (e.g., "nazca_1x2_mmi_splitter_1") instead of the library name
            // ("1x2 MMI Splitter"), then LoadComponentFromData won't find it in the library.
            var savedJson = await File.ReadAllTextAsync(tempFile);
            // Bug: if TemplateName is saved as HumanReadableName ("nazca_1x2_mmi_splitter_1")
            // instead of the library name ("1x2 MMI Splitter"), loading will fail.
            savedJson.ShouldContain("1x2 MMI Splitter");

            // === PHASE 4: Load the design and verify both components are present ===
            var (phase4Vm, phase4Canvas) = CreateFileOperationsSetup();

            var loadDialog = new Mock<IFileDialogService>();
            loadDialog.Setup(f => f.ShowOpenFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tempFile);
            phase4Vm.FileDialogService = loadDialog.Object;

            await phase4Vm.LoadDesignCommand.ExecuteAsync(null);

            // Assert: both components survived the save/load round trip
            phase4Canvas.Components.Count.ShouldBe(2,
                "Both ungrouped components should be present after load. " +
                "Bug: components from ungrouped UserGroup templates are lost because " +
                "their TemplateName is saved as HumanReadableName instead of library name.");

            phase4Canvas.Components.Any(c => c.Component is ComponentGroup).ShouldBeFalse(
                "Components should be standalone (not in a group) after load");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            if (Directory.Exists(tempLibDir))
                Directory.Delete(tempLibDir, true);
        }
    }

    /// <summary>
    /// Verifies that ungrouped template components also preserve their
    /// waveguide connections after save/load.
    /// </summary>
    [Fact]
    public async Task UngroupedUserGroupTemplate_SaveAndLoad_PreservesConnections()
    {
        var tempLibDir = Path.Combine(Path.GetTempPath(), $"grouplib_{Guid.NewGuid():N}");
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_ungroup_conn_{Guid.NewGuid():N}.cappro");

        try
        {
            var libraryManager = new GroupLibraryManager(tempLibDir);

            // Create group with 2 components and an internal path
            var mmiTemplate = _library.First(t => t.Name == "1x2 MMI Splitter");
            var comp1 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 100, 100);
            var comp2 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 300, 100);

            var group = new ComponentGroup("ConnectionTestGroup")
            {
                PhysicalX = 100,
                PhysicalY = 100
            };
            group.AddChild(comp1);
            group.AddChild(comp2);

            var path = new RoutedPath();
            path.Segments.Add(new StraightSegment(180, 127.5, 300, 127.5, 0));
            group.AddInternalPath(new FrozenWaveguidePath
            {
                Path = path,
                StartPin = comp1.PhysicalPins.First(p => p.Name == "out1"),
                EndPin = comp2.PhysicalPins.First(p => p.Name == "in")
            });

            var savedTemplate = libraryManager.SaveTemplate(group, "ConnectionTestGroup");

            // Place, ungroup, and save
            var (saveVm, saveCanvas) = CreateFileOperationsSetup();

            var instance = libraryManager.InstantiateTemplate(savedTemplate, 100, 100);
            saveCanvas.AddComponent(instance);

            var ungroupCmd = new UngroupCommand(saveCanvas, instance);
            ungroupCmd.Execute();

            // The ungroup should have restored the connection
            saveCanvas.Connections.Count.ShouldBe(1,
                "Ungrouping should restore the internal frozen path as a connection");

            // Save
            var saveDialog = new Mock<IFileDialogService>();
            saveDialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tempFile);
            saveVm.FileDialogService = saveDialog.Object;
            await saveVm.SaveDesignAsCommand.ExecuteAsync(null);

            // Load
            var (loadVm, loadCanvas) = CreateFileOperationsSetup();
            var loadDialog = new Mock<IFileDialogService>();
            loadDialog.Setup(f => f.ShowOpenFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tempFile);
            loadVm.FileDialogService = loadDialog.Object;
            await loadVm.LoadDesignCommand.ExecuteAsync(null);

            // Assert components and connection preserved
            loadCanvas.Components.Count.ShouldBe(2,
                "Both components should survive save/load");
            loadCanvas.Connections.Count.ShouldBeGreaterThan(0,
                "The connection between ungrouped components should survive save/load");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            if (Directory.Exists(tempLibDir))
                Directory.Delete(tempLibDir, true);
        }
    }

    /// <summary>
    /// Creates a FileOperationsViewModel with real component library for testing.
    /// </summary>
    private (FileOperationsViewModel vm, DesignCanvasViewModel canvas) CreateFileOperationsSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var nazcaExporter = new SimpleNazcaExporter();
        var gdsExport = new GdsExportViewModel(new CAP_Core.Export.GdsExportService());

        var photonTorchExport = new CAP.Avalonia.ViewModels.Export.PhotonTorchExportViewModel(
            new CAP_Core.Export.PhotonTorchExporter(), canvas);
        var vm = new FileOperationsViewModel(
            canvas, commandManager, nazcaExporter, new CAP_Core.Export.PicWaveExporter(), _library, gdsExport, photonTorchExport);

        return (vm, canvas);
    }
}
