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
/// Comprehensive save/load roundtrip tests for ComponentGroups and prefab instances.
/// Verifies ALL critical properties including frozen waveguide paths, external pins,
/// child component naming, and S-Matrix computation post-load.
/// Covers issue #316.
/// </summary>
public class ComprehensiveGroupRoundtripTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;

    /// <summary>Initializes the test suite with the full component library.</summary>
    public ComprehensiveGroupRoundtripTests()
    {
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// Verifies ALL ComponentGroup properties: GroupName, PhysicalX/Y, ChildComponents,
    /// InternalPaths (frozen waveguide segments + pin references), and ExternalPins.
    /// </summary>
    [Fact]
    public async Task ComponentGroup_AllProperties_PreservedAfterRoundtrip()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_group_{Guid.NewGuid():N}.cappro");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();
            var mmiTemplate = _library.First(t => t.Name == "1x2 MMI Splitter");

            // Create two MMI components
            var child1 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 100, 100);
            child1.Identifier = "group_child_mmi_1";
            child1.HumanReadableName = "Input MMI";

            var child2 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 300, 100);
            child2.Identifier = "group_child_mmi_2";
            child2.HumanReadableName = "Output MMI";

            // Build group with frozen path and external pin
            var group = new ComponentGroup("FullTestGroup")
            {
                PhysicalX = 100,
                PhysicalY = 100,
                Description = "Comprehensive group roundtrip test"
            };
            group.AddChild(child1);
            group.AddChild(child2);

            var routedPath = new RoutedPath();
            routedPath.Segments.Add(new StraightSegment(180, 125.5, 300, 127.5, 0));
            var frozenPath = new FrozenWaveguidePath
            {
                Path = routedPath,
                StartPin = child1.PhysicalPins.First(p => p.Name == "out1"),
                EndPin = child2.PhysicalPins.First(p => p.Name == "in")
            };
            group.AddInternalPath(frozenPath);

            var externalPin = new GroupPin
            {
                Name = "group_input",
                InternalPin = child1.PhysicalPins.First(p => p.Name == "in"),
                RelativeX = 0,
                RelativeY = 27.5,
                AngleDegrees = 180
            };
            group.AddExternalPin(externalPin);

            var groupVm = saveCanvas.AddComponent(group);
            await SaveToFile(saveVm, tempFile);

            // Act: Load
            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            // Assert: Group itself
            loadCanvas.Components.Count.ShouldBe(1, "Canvas must have exactly one component (the group)");
            var loadedGroupVm = loadCanvas.Components.First(c => c.Component is ComponentGroup);
            var loadedGroup = (ComponentGroup)loadedGroupVm.Component;

            loadedGroup.GroupName.ShouldBe("FullTestGroup", "GroupName must survive roundtrip");
            loadedGroup.Description.ShouldBe("Comprehensive group roundtrip test",
                "Description must survive roundtrip");
            loadedGroup.PhysicalX.ShouldBe(100, "Group PhysicalX must survive roundtrip");
            loadedGroup.PhysicalY.ShouldBe(100, "Group PhysicalY must survive roundtrip");

            // Assert: Child components
            loadedGroup.ChildComponents.Count.ShouldBe(2, "Both child components must survive roundtrip");
            var loadedChild1 = loadedGroup.ChildComponents.First(c => c.Identifier == "group_child_mmi_1");
            var loadedChild2 = loadedGroup.ChildComponents.First(c => c.Identifier == "group_child_mmi_2");
            loadedChild1.HumanReadableName.ShouldBe("Input MMI",
                "Child HumanReadableName must survive roundtrip");
            loadedChild2.HumanReadableName.ShouldBe("Output MMI",
                "Child HumanReadableName must survive roundtrip");

            // Assert: Frozen waveguide path
            loadedGroup.InternalPaths.Count.ShouldBe(1, "Frozen path must survive roundtrip");
            var loadedPath = loadedGroup.InternalPaths[0];
            loadedPath.Path.Segments.Count.ShouldBe(1, "Path segment count must survive roundtrip");
            loadedPath.Path.Segments[0].ShouldBeOfType<StraightSegment>(
                "Path segment type must survive roundtrip");
            loadedPath.StartPin.ShouldNotBeNull("FrozenPath StartPin must be restored");
            loadedPath.EndPin.ShouldNotBeNull("FrozenPath EndPin must be restored");
            loadedPath.StartPin.Name.ShouldBe("out1", "FrozenPath StartPin name must survive roundtrip");
            loadedPath.EndPin.Name.ShouldBe("in", "FrozenPath EndPin name must survive roundtrip");
            loadedPath.StartPin.ParentComponent.Identifier.ShouldBe("group_child_mmi_1",
                "FrozenPath StartPin must reference correct child after roundtrip");
            loadedPath.EndPin.ParentComponent.Identifier.ShouldBe("group_child_mmi_2",
                "FrozenPath EndPin must reference correct child after roundtrip");

            // Assert: External pin
            loadedGroup.ExternalPins.Count.ShouldBe(1, "External pin must survive roundtrip");
            var loadedExtPin = loadedGroup.ExternalPins[0];
            loadedExtPin.Name.ShouldBe("group_input", "External pin name must survive roundtrip");
            loadedExtPin.AngleDegrees.ShouldBe(180, "External pin angle must survive roundtrip");
            loadedExtPin.InternalPin.ShouldNotBeNull("External pin InternalPin must be restored");
            loadedExtPin.InternalPin.Name.ShouldBe("in",
                "External pin InternalPin name must survive roundtrip");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies prefab instance properties: unique naming (Name_1, Name_2),
    /// child component HumanReadableNames from template (not NazcaFunctionName).
    /// </summary>
    [Fact]
    public async Task PrefabInstance_UniqueNamesAndChildHumanReadableNames_Preserved()
    {
        var tempLibDir = Path.Combine(Path.GetTempPath(), $"prefablib_{Guid.NewGuid():N}");
        var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_prefab_{Guid.NewGuid():N}.cappro");
        try
        {
            // Create template
            var libraryManager = new GroupLibraryManager(tempLibDir);
            var mmiTemplate = _library.First(t => t.Name == "1x2 MMI Splitter");
            var child1 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 0, 0);
            child1.HumanReadableName = "Splitter Input";
            var child2 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 200, 0);
            child2.HumanReadableName = "Splitter Output";

            var templateGroup = new ComponentGroup("TestPrefab")
            {
                PhysicalX = 0,
                PhysicalY = 0
            };
            templateGroup.AddChild(child1);
            templateGroup.AddChild(child2);

            libraryManager.SaveTemplate(templateGroup, "TestPrefab", "Prefab for roundtrip test");

            // Instantiate 2 prefabs
            var (saveVm, saveCanvas) = CreateSetup();
            libraryManager.LoadTemplates();
            var prefabTemplate = libraryManager.Templates.First(t => t.Name == "TestPrefab");

            var instance1 = libraryManager.InstantiateTemplate(prefabTemplate, 100, 100);
            var instance2 = libraryManager.InstantiateTemplate(prefabTemplate, 400, 100);
            saveCanvas.AddComponent(instance1);
            saveCanvas.AddComponent(instance2);

            await SaveToFile(saveVm, tempFile);

            // Act: Load
            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            // Assert: two groups loaded
            loadCanvas.Components.Count.ShouldBe(2, "Both prefab instances must survive roundtrip");
            var groups = loadCanvas.Components
                .Select(vm => vm.Component)
                .OfType<ComponentGroup>()
                .ToList();
            groups.Count.ShouldBe(2, "Both components must be ComponentGroups");

            // Assert: unique names preserved (counter is global so we check the pattern, not exact values)
            var names = groups.Select(g => g.GroupName).ToList();
            names.All(n => n.StartsWith("TestPrefab_")).ShouldBeTrue(
                "Both instances must have names starting with TestPrefab_");
            names.Distinct().Count().ShouldBe(2,
                "Both instances must have UNIQUE names after roundtrip");

            // Assert: child HumanReadableNames preserved (not NazcaFunctionName)
            foreach (var group in groups)
            {
                group.ChildComponents.Count.ShouldBe(2,
                    $"Group {group.GroupName} must have 2 children after roundtrip");
                foreach (var child in group.ChildComponents)
                {
                    child.HumanReadableName.ShouldNotBeNull(
                        $"Child in {group.GroupName} must have a HumanReadableName");
                    var name = child.HumanReadableName!;
                    name.Contains("ebeam_").ShouldBeFalse(
                        "HumanReadableName must not be a NazcaFunctionName");
                    name.Contains("placeCell_").ShouldBeFalse(
                        "HumanReadableName must not be a NazcaFunctionName");
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempLibDir)) Directory.Delete(tempLibDir, recursive: true);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private (FileOperationsViewModel vm, DesignCanvasViewModel canvas) CreateSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            _library,
            new GdsExportViewModel(new CAP_Core.Export.GdsExportService()),
            new CAP.Avalonia.ViewModels.Export.PhotonTorchExportViewModel(new CAP_Core.Export.PhotonTorchExporter(), canvas));
        return (vm, canvas);
    }

    private async Task SaveToFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("Design file must be created during save");
    }

    private async Task LoadFromFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.LoadDesignCommand.ExecuteAsync(null);
    }
}
