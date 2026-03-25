using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for the complete save → load → place workflow.
/// Verifies that group templates survive disk persistence and can be placed on canvas
/// after being loaded from disk (the root cause of issue #243).
/// </summary>
public class GroupTemplatePersistenceTests : IDisposable
{
    private readonly string _testLibraryPath;

    public GroupTemplatePersistenceTests()
    {
        _testLibraryPath = Path.Combine(
            Path.GetTempPath(),
            $"GroupPersistenceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testLibraryPath))
        {
            Directory.Delete(_testLibraryPath, true);
        }
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_TemplateGroupIsNotNull()
    {
        // Arrange - Save a group template
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroup("TestGroup", 2);
        saveManager.SaveTemplate(group, "My Template");

        // Act - Load from disk with a new manager instance (simulates app restart)
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();

        // Assert
        loadManager.Templates.Count.ShouldBe(1);
        var loaded = loadManager.Templates[0];
        loaded.Name.ShouldBe("My Template");
        loaded.TemplateGroup.ShouldNotBeNull();
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_PreservesChildComponents()
    {
        // Arrange
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroup("TestGroup", 3);
        saveManager.SaveTemplate(group, "3-Component Group");

        // Act
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();

        // Assert
        var loaded = loadManager.Templates[0].TemplateGroup!;
        loaded.ChildComponents.Count.ShouldBe(3);
        loaded.GroupName.ShouldBe("TestGroup");
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_PreservesChildPositions()
    {
        // Arrange
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroup("PositionTest", 2);
        group.ChildComponents[0].PhysicalX = 100;
        group.ChildComponents[0].PhysicalY = 200;
        group.ChildComponents[1].PhysicalX = 300;
        group.ChildComponents[1].PhysicalY = 400;
        saveManager.SaveTemplate(group, "Position Test");

        // Act
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();

        // Assert
        var loaded = loadManager.Templates[0].TemplateGroup!;
        loaded.ChildComponents[0].PhysicalX.ShouldBe(100);
        loaded.ChildComponents[0].PhysicalY.ShouldBe(200);
        loaded.ChildComponents[1].PhysicalX.ShouldBe(300);
        loaded.ChildComponents[1].PhysicalY.ShouldBe(400);
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_PreservesChildPins()
    {
        // Arrange
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroup("PinTest", 2);
        saveManager.SaveTemplate(group, "Pin Test");

        // Act
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();

        // Assert
        var loaded = loadManager.Templates[0].TemplateGroup!;
        foreach (var child in loaded.ChildComponents)
        {
            child.PhysicalPins.Count.ShouldBe(2);
            child.PhysicalPins[0].Name.ShouldBe("a0");
            child.PhysicalPins[1].Name.ShouldBe("b0");
        }
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_PreservesInternalPaths()
    {
        // Arrange
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroupWithConnection("PathTest");
        saveManager.SaveTemplate(group, "Path Test");

        // Act
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();

        // Assert
        var loaded = loadManager.Templates[0].TemplateGroup!;
        loaded.InternalPaths.Count.ShouldBe(1);

        var path = loaded.InternalPaths[0];
        path.StartPin.ShouldNotBeNull();
        path.EndPin.ShouldNotBeNull();
        path.StartPin.ParentComponent.ShouldBe(loaded.ChildComponents[0]);
        path.EndPin.ParentComponent.ShouldBe(loaded.ChildComponents[1]);
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_PreservesExternalPins()
    {
        // Arrange
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroupWithExternalPins("PinGroup");
        saveManager.SaveTemplate(group, "External Pin Test");

        // Act
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();

        // Assert
        var loaded = loadManager.Templates[0].TemplateGroup!;
        loaded.ExternalPins.Count.ShouldBe(1);

        var extPin = loaded.ExternalPins[0];
        extPin.Name.ShouldBe("ext_a0");
        extPin.InternalPin.ShouldNotBeNull();
        extPin.InternalPin.ParentComponent.ShouldBe(loaded.ChildComponents[0]);
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_CanInstantiateTemplate()
    {
        // Arrange
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroup("InstTest", 2);
        saveManager.SaveTemplate(group, "Instantiate Test");

        // Act
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();
        var loaded = loadManager.Templates[0];
        var instance = loadManager.InstantiateTemplate(loaded, 500, 500);

        // Assert
        instance.ShouldNotBeNull();
        instance.PhysicalX.ShouldBe(500);
        instance.PhysicalY.ShouldBe(500);
        instance.ChildComponents.Count.ShouldBe(2);
        instance.Identifier.ShouldNotBe(loaded.TemplateGroup!.Identifier);
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_CanPlaceOnCanvas()
    {
        // Arrange - Save a template
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroup("PlaceTest", 2);
        saveManager.SaveTemplate(group, "Place Test");

        // Act - Load with new manager and try to place
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();
        var template = loadManager.Templates[0];

        var canvas = new DesignCanvasViewModel();
        var cmd = PlaceGroupTemplateCommand.TryCreate(
            canvas, loadManager, template, 500, 500);

        // Assert - Command should succeed (not null)
        cmd.ShouldNotBeNull();
        cmd!.Execute();

        canvas.Components.Count.ShouldBe(1);
        var placedGroup = (ComponentGroup)canvas.Components[0].Component;
        placedGroup.ChildComponents.Count.ShouldBe(2);
        placedGroup.IsPrefab.ShouldBeFalse();
    }

    [Fact]
    public void FullWorkflow_CreateGroupSavePlaceFromDisk()
    {
        // Step 1: Create components and group on canvas
        var canvas1 = new DesignCanvasViewModel();
        var comp1 = CreateTestComponentWithTwoPins("comp1", 0, 0);
        var comp2 = CreateTestComponentWithTwoPins("comp2", 100, 0);
        var vm1 = canvas1.AddComponent(comp1);
        var vm2 = canvas1.AddComponent(comp2);

        var createCmd = new CreateGroupCommand(
            canvas1, new List<ComponentViewModel> { vm1, vm2 });
        createCmd.Execute();

        var createdGroup = (ComponentGroup)canvas1.Components
            .First(c => c.Component is ComponentGroup).Component;

        // Step 2: Save as prefab to library
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var libraryVm = new ComponentLibraryViewModel(saveManager);
        var saveCmd = new SaveGroupAsPrefabCommand(
            libraryVm,
            new GroupPreviewGenerator(),
            createdGroup,
            "My Prefab");
        saveCmd.Execute();

        // Step 3: Simulate app restart - load from disk with new manager
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();
        var loadedTemplate = loadManager.Templates[0];

        // Step 4: Place loaded template on a new canvas
        var canvas2 = new DesignCanvasViewModel();
        var placeCmd = PlaceGroupTemplateCommand.TryCreate(
            canvas2, loadManager, loadedTemplate, 500, 500);

        // Assert: full workflow succeeds
        placeCmd.ShouldNotBeNull();
        placeCmd!.Execute();

        canvas2.Components.Count.ShouldBe(1);
        var placedGroup = (ComponentGroup)canvas2.Components[0].Component;
        placedGroup.ChildComponents.Count.ShouldBe(2);
        placedGroup.IsPrefab.ShouldBeFalse();
    }

    [Fact]
    public void SaveTemplate_ThenLoadFromDisk_PreservesPathGeometry()
    {
        // Arrange
        var saveManager = new GroupLibraryManager(_testLibraryPath);
        var group = CreateTestGroupWithStraightPath("GeomTest");
        saveManager.SaveTemplate(group, "Geometry Test");

        // Act
        var loadManager = new GroupLibraryManager(_testLibraryPath);
        loadManager.LoadTemplates();

        // Assert
        var loaded = loadManager.Templates[0].TemplateGroup!;
        loaded.InternalPaths.Count.ShouldBe(1);

        var path = loaded.InternalPaths[0];
        path.Path.Segments.Count.ShouldBe(1);

        var segment = path.Path.Segments[0].ShouldBeOfType<StraightSegment>();
        segment.StartPoint.X.ShouldBe(50, tolerance: 0.01);
        segment.StartPoint.Y.ShouldBe(0, tolerance: 0.01);
        segment.EndPoint.X.ShouldBe(100, tolerance: 0.01);
        segment.EndPoint.Y.ShouldBe(0, tolerance: 0.01);
    }

    /// <summary>
    /// Creates a ComponentGroup with the specified number of children, each with 2 pins.
    /// </summary>
    private ComponentGroup CreateTestGroup(string name, int childCount)
    {
        var group = new ComponentGroup(name)
        {
            PhysicalX = 0,
            PhysicalY = 0
        };

        for (int i = 0; i < childCount; i++)
        {
            var child = CreateTestComponentWithTwoPins(
                $"comp_{i}_{Guid.NewGuid():N}",
                i * 100, 0);
            group.AddChild(child);
        }

        return group;
    }

    /// <summary>
    /// Creates a group with two children connected by a frozen path (no segments).
    /// </summary>
    private ComponentGroup CreateTestGroupWithConnection(string name)
    {
        var group = CreateTestGroup(name, 2);
        var comp1 = group.ChildComponents[0];
        var comp2 = group.ChildComponents[1];

        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            StartPin = comp1.PhysicalPins[1], // b0
            EndPin = comp2.PhysicalPins[0],   // a0
            Path = new RoutedPath()
        };

        group.AddInternalPath(frozenPath);
        return group;
    }

    /// <summary>
    /// Creates a group with two children connected by a frozen path with geometry.
    /// </summary>
    private ComponentGroup CreateTestGroupWithStraightPath(string name)
    {
        var group = CreateTestGroup(name, 2);
        var comp1 = group.ChildComponents[0];
        var comp2 = group.ChildComponents[1];

        var routedPath = new RoutedPath();
        routedPath.Segments.Add(new StraightSegment(50, 0, 100, 0, 0));

        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            StartPin = comp1.PhysicalPins[1], // b0
            EndPin = comp2.PhysicalPins[0],   // a0
            Path = routedPath
        };

        group.AddInternalPath(frozenPath);
        return group;
    }

    /// <summary>
    /// Creates a group with an external pin.
    /// </summary>
    private ComponentGroup CreateTestGroupWithExternalPins(string name)
    {
        var group = CreateTestGroup(name, 2);
        var comp1 = group.ChildComponents[0];

        var extPin = new GroupPin
        {
            PinId = Guid.NewGuid(),
            Name = "ext_a0",
            InternalPin = comp1.PhysicalPins[0], // a0
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        };

        group.AddExternalPin(extPin);
        return group;
    }

    /// <summary>
    /// Creates a test component with two pins (a0, b0).
    /// </summary>
    private Component CreateTestComponentWithTwoPins(
        string identifier, double x, double y)
    {
        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test_component",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            DiscreteRotation.R0,
            new List<PhysicalPin>
            {
                new PhysicalPin
                {
                    Name = "a0",
                    OffsetXMicrometers = 0,
                    OffsetYMicrometers = 0,
                    AngleDegrees = 180
                },
                new PhysicalPin
                {
                    Name = "b0",
                    OffsetXMicrometers = 50,
                    OffsetYMicrometers = 0,
                    AngleDegrees = 0
                }
            })
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };
    }
}
