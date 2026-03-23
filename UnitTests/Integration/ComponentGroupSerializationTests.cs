using CAP_Contracts;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_DataAccess.Persistence;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for ComponentGroup serialization (save/load) with connections.
/// Simulates user workflow: Create components → Connect → Group → Save → Clear → Load → Verify.
/// </summary>
public class ComponentGroupSerializationTests
{
    /// <summary>
    /// Test 1: Simple group with 2 components and 1 connection.
    /// This is the most basic scenario - verifies that grouping, saving, and loading works.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_SimpleGroupWithConnection_RestoresCorrectly()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        // Create two components with pins
        var comp1 = CreateComponentWithPins("comp1", 0, 0);
        var comp2 = CreateComponentWithPins("comp2", 50, 0);

        gridManager.ComponentMover.PlaceComponent(0, 0, comp1);
        gridManager.ComponentMover.PlaceComponent(5, 0, comp2);

        // Create a group containing both components and the connection
        var group = new ComponentGroup("SimpleGroup")
        {
            PhysicalX = 0,
            PhysicalY = 0,
            Description = "Test group with 2 components"
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Create frozen path representing the connection between components
        var routedPath = CreateSimpleRoutedPath(comp1.PhysicalX + 10, comp1.PhysicalY, comp2.PhysicalX, comp2.PhysicalY);
        var frozenPath = new FrozenWaveguidePath
        {
            Path = routedPath,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        };
        group.AddInternalPath(frozenPath);

        // Place the group on the grid
        gridManager.ComponentMover.DeleteAllComponents();
        gridManager.ComponentMover.PlaceComponent(1, 1, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateComponentWithPins("comp1", 0, 0));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateComponentWithPins("comp2", 50, 0));

        // Act - Save
        var saveResult = await persistence.SaveAsync("test_simple.json");

        // Assert - Save succeeded
        saveResult.ShouldBeTrue();
        savedJson.ShouldNotBeNullOrEmpty();
        savedJson.ShouldContain("SimpleGroup");
        savedJson.ShouldContain("comp1");
        savedJson.ShouldContain("comp2");

        // Act - Clear and Load
        gridManager.ComponentMover.DeleteAllComponents();
        gridManager.TileManager.Tiles.Length.ShouldBeGreaterThan(0); // Grid still exists

        await persistence.LoadAsync("test_simple.json", componentFactory.Object);

        // Assert - Group restored with all components and connections
        var loadedTile = gridManager.TileManager.Tiles[1, 1];
        loadedTile.ShouldNotBeNull();
        loadedTile.Component.ShouldBeOfType<ComponentGroup>();

        var loadedGroup = (ComponentGroup)loadedTile.Component;
        loadedGroup.GroupName.ShouldBe("SimpleGroup");
        loadedGroup.Description.ShouldBe("Test group with 2 components");
        loadedGroup.ChildComponents.Count.ShouldBe(2);

        // CRITICAL: Verify the internal connection was restored
        loadedGroup.InternalPaths.Count.ShouldBe(1, "Internal connection should be restored");
        var loadedPath = loadedGroup.InternalPaths[0];
        loadedPath.Path.ShouldNotBeNull();
        loadedPath.Path.Segments.Count.ShouldBeGreaterThan(0);
        loadedPath.StartPin.ShouldNotBeNull();
        loadedPath.EndPin.ShouldNotBeNull();

        // Verify the components are the correct ones
        loadedGroup.ChildComponents.Any(c => c.Identifier == "comp1").ShouldBeTrue();
        loadedGroup.ChildComponents.Any(c => c.Identifier == "comp2").ShouldBeTrue();

        // Verify bounding box is recalculated
        loadedGroup.WidthMicrometers.ShouldBeGreaterThan(0);
        loadedGroup.HeightMicrometers.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Test 2: Complex group with 4 components and multiple connections.
    /// Tests that multiple internal connections are preserved correctly.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_ComplexGroupWithMultipleConnections_RestoresAll()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        // Create 4 components in a chain: comp1 -> comp2 -> comp3 -> comp4
        var comp1 = CreateComponentWithPins("comp1", 0, 0);
        var comp2 = CreateComponentWithPins("comp2", 20, 0);
        var comp3 = CreateComponentWithPins("comp3", 40, 0);
        var comp4 = CreateComponentWithPins("comp4", 60, 0);

        // Create a group with all 4 components
        var group = new ComponentGroup("ComplexGroup")
        {
            PhysicalX = 0,
            PhysicalY = 0,
            Description = "Complex group with 4 components and 3 connections"
        };
        group.AddChild(comp1);
        group.AddChild(comp2);
        group.AddChild(comp3);
        group.AddChild(comp4);

        // Create 3 connections: comp1->comp2, comp2->comp3, comp3->comp4
        var path1 = CreateSimpleRoutedPath(10, 0, 20, 0);
        var path2 = CreateSimpleRoutedPath(30, 0, 40, 0);
        var path3 = CreateSimpleRoutedPath(50, 0, 60, 0);

        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = path1,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        });

        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = path2,
            StartPin = comp2.PhysicalPins[0],
            EndPin = comp3.PhysicalPins[0]
        });

        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = path3,
            StartPin = comp3.PhysicalPins[0],
            EndPin = comp4.PhysicalPins[0]
        });

        gridManager.ComponentMover.PlaceComponent(0, 0, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateComponentWithPins("comp1", 0, 0));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateComponentWithPins("comp2", 20, 0));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp3"))
            .Returns(() => CreateComponentWithPins("comp3", 40, 0));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp4"))
            .Returns(() => CreateComponentWithPins("comp4", 60, 0));

        // Act - Save, Clear, Load
        await persistence.SaveAsync("test_complex.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test_complex.json", componentFactory.Object);

        // Assert - All components and connections restored
        var loadedTile = gridManager.TileManager.Tiles[0, 0];
        var loadedGroup = (ComponentGroup)loadedTile.Component;

        loadedGroup.GroupName.ShouldBe("ComplexGroup");
        loadedGroup.ChildComponents.Count.ShouldBe(4);

        // CRITICAL: All 3 connections must be restored
        loadedGroup.InternalPaths.Count.ShouldBe(3, "All 3 internal connections must be preserved");

        // Verify each connection has valid path segments
        foreach (var frozenPath in loadedGroup.InternalPaths)
        {
            frozenPath.Path.ShouldNotBeNull();
            frozenPath.Path.Segments.Count.ShouldBeGreaterThan(0, "Each path should have segments");
            frozenPath.StartPin.ShouldNotBeNull("Start pin should be linked");
            frozenPath.EndPin.ShouldNotBeNull("End pin should be linked");
        }

        // Verify component identifiers
        loadedGroup.ChildComponents.Select(c => c.Identifier)
            .ShouldBe(new[] { "comp1", "comp2", "comp3", "comp4" }, ignoreOrder: true);
    }

    /// <summary>
    /// Test 3: Group with external connections to non-group components.
    /// Tests that external pins are created and preserved.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_GroupWithExternalConnections_RestoresExternalPins()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        // Create a group with 2 components and 1 internal connection
        var comp1 = CreateComponentWithPins("comp1", 0, 0);
        var comp2 = CreateComponentWithPins("comp2", 20, 0);

        var group = new ComponentGroup("GroupWithExternal")
        {
            PhysicalX = 0,
            PhysicalY = 0
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Add internal connection
        var internalPath = CreateSimpleRoutedPath(10, 0, 20, 0);
        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = internalPath,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        });

        // Create external pins for connections to outside components
        var externalPin1 = new GroupPin
        {
            Name = "input_pin",
            InternalPin = comp1.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        };

        var externalPin2 = new GroupPin
        {
            Name = "output_pin",
            InternalPin = comp2.PhysicalPins[0],
            RelativeX = 40,
            RelativeY = 0,
            AngleDegrees = 0
        };

        group.AddExternalPin(externalPin1);
        group.AddExternalPin(externalPin2);

        // Place group and a standalone component
        gridManager.ComponentMover.PlaceComponent(0, 0, group);

        var standaloneComp = CreateComponentWithPins("standalone", 60, 0);
        gridManager.ComponentMover.PlaceComponent(10, 0, standaloneComp);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateComponentWithPins("comp1", 0, 0));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateComponentWithPins("comp2", 20, 0));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("standalone"))
            .Returns(() => CreateComponentWithPins("standalone", 60, 0));

        // Act - Save, Clear, Load
        await persistence.SaveAsync("test_external.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test_external.json", componentFactory.Object);

        // Assert - Group and external pins restored
        var loadedTile = gridManager.TileManager.Tiles[0, 0];
        var loadedGroup = (ComponentGroup)loadedTile.Component;

        loadedGroup.GroupName.ShouldBe("GroupWithExternal");
        loadedGroup.ChildComponents.Count.ShouldBe(2);
        loadedGroup.InternalPaths.Count.ShouldBe(1);

        // CRITICAL: External pins must be preserved
        loadedGroup.ExternalPins.Count.ShouldBe(2, "Both external pins should be restored");

        var inputPin = loadedGroup.ExternalPins.FirstOrDefault(p => p.Name == "input_pin");
        inputPin.ShouldNotBeNull("Input pin should exist");
        inputPin!.InternalPin.ShouldNotBeNull("Input pin should be linked to internal pin");
        inputPin.RelativeX.ShouldBe(0);
        inputPin.AngleDegrees.ShouldBe(180);

        var outputPin = loadedGroup.ExternalPins.FirstOrDefault(p => p.Name == "output_pin");
        outputPin.ShouldNotBeNull("Output pin should exist");
        outputPin!.InternalPin.ShouldNotBeNull("Output pin should be linked to internal pin");
        outputPin.RelativeX.ShouldBe(40);
        outputPin.AngleDegrees.ShouldBe(0);

        // Verify standalone component also restored
        var standaloneTile = gridManager.TileManager.Tiles[10, 0];
        standaloneTile.ShouldNotBeNull();
        standaloneTile.Component.Identifier.ShouldBe("standalone");
    }

    /// <summary>
    /// Test 4: Group with complex routed paths (straights and bends).
    /// Verifies that path geometry is preserved correctly.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_GroupWithComplexPaths_PreservesGeometry()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var comp1 = CreateComponentWithPins("comp1", 0, 0);
        var comp2 = CreateComponentWithPins("comp2", 50, 30);

        var group = new ComponentGroup("GeometryGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Create a complex path with straight and bend segments
        var complexPath = new RoutedPath();
        complexPath.Segments.Add(new StraightSegment(0, 0, 30, 0, 0));
        complexPath.Segments.Add(new BendSegment(30, 10, 10, 0, 90)); // 90° bend
        complexPath.Segments.Add(new StraightSegment(40, 10, 50, 30, 90));

        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = complexPath,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        });

        gridManager.ComponentMover.PlaceComponent(0, 0, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateComponentWithPins("comp1", 0, 0));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateComponentWithPins("comp2", 50, 30));

        // Act - Save, Clear, Load
        await persistence.SaveAsync("test_geometry.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test_geometry.json", componentFactory.Object);

        // Assert - Path geometry preserved
        var loadedTile = gridManager.TileManager.Tiles[0, 0];
        var loadedGroup = (ComponentGroup)loadedTile.Component;

        loadedGroup.InternalPaths.Count.ShouldBe(1);
        var loadedPath = loadedGroup.InternalPaths[0];

        loadedPath.Path.Segments.Count.ShouldBe(3, "All 3 segments should be preserved");
        loadedPath.Path.Segments[0].ShouldBeOfType<StraightSegment>("First segment should be straight");
        loadedPath.Path.Segments[1].ShouldBeOfType<BendSegment>("Second segment should be bend");
        loadedPath.Path.Segments[2].ShouldBeOfType<StraightSegment>("Third segment should be straight");

        // Verify bend segment parameters
        var bendSegment = (BendSegment)loadedPath.Path.Segments[1];
        bendSegment.RadiusMicrometers.ShouldBe(10);
        bendSegment.StartAngleDegrees.ShouldBe(0);
        bendSegment.SweepAngleDegrees.ShouldBe(90);
    }

    /// <summary>
    /// Test 5: Empty group (no components, no connections).
    /// Edge case - should save and load without errors.
    /// </summary>
    [Fact]
    public async Task SaveAndLoad_EmptyGroup_HandlesGracefully()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var emptyGroup = new ComponentGroup("EmptyGroup")
        {
            PhysicalX = 10,
            PhysicalY = 10,
            Description = "Group with no children"
        };

        gridManager.ComponentMover.PlaceComponent(1, 1, emptyGroup);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        // Act - Save, Clear, Load
        await persistence.SaveAsync("test_empty.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test_empty.json", componentFactory.Object);

        // Assert - Empty group restored
        var loadedTile = gridManager.TileManager.Tiles[1, 1];
        loadedTile.ShouldNotBeNull();
        var loadedGroup = (ComponentGroup)loadedTile.Component;

        loadedGroup.GroupName.ShouldBe("EmptyGroup");
        loadedGroup.ChildComponents.Count.ShouldBe(0);
        loadedGroup.InternalPaths.Count.ShouldBe(0);
        loadedGroup.ExternalPins.Count.ShouldBe(0);
    }

    /// <summary>
    /// Test 6: Group movement after loading.
    /// Verifies that loaded groups can be moved and their internal paths follow correctly.
    /// </summary>
    [Fact]
    public async Task LoadedGroup_CanBeMovedCorrectly()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var comp1 = CreateComponentWithPins("comp1", 0, 0);
        var comp2 = CreateComponentWithPins("comp2", 20, 0);

        var group = new ComponentGroup("MovableGroup")
        {
            PhysicalX = 0,
            PhysicalY = 0
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        var path = CreateSimpleRoutedPath(10, 0, 20, 0);
        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = path,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        });

        gridManager.ComponentMover.PlaceComponent(0, 0, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateComponentWithPins("comp1", 0, 0));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateComponentWithPins("comp2", 20, 0));

        // Act - Save, Clear, Load
        await persistence.SaveAsync("test_movable.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test_movable.json", componentFactory.Object);

        var loadedTile = gridManager.TileManager.Tiles[0, 0];
        var loadedGroup = (ComponentGroup)loadedTile.Component;

        var originalGroupX = loadedGroup.PhysicalX;
        var originalChild1X = loadedGroup.ChildComponents[0].PhysicalX;
        var originalPathStartX = loadedGroup.InternalPaths[0].Path.Segments[0].StartPoint.X;

        // Move the group
        const double deltaX = 100;
        const double deltaY = 50;
        loadedGroup.MoveGroup(deltaX, deltaY);

        // Assert - Group and children moved
        loadedGroup.PhysicalX.ShouldBe(originalGroupX + deltaX);
        loadedGroup.ChildComponents[0].PhysicalX.ShouldBe(originalChild1X + deltaX);

        // CRITICAL: Internal paths should also be translated
        var movedPathStartX = loadedGroup.InternalPaths[0].Path.Segments[0].StartPoint.X;
        movedPathStartX.ShouldBe(originalPathStartX + deltaX,
            "Path segments should move with the group");
    }

    /// <summary>
    /// Creates a test setup with GridManager, mocked IDataAccessor, and mocked IComponentFactory.
    /// </summary>
    private (GridManager gridManager, Mock<IDataAccessor> dataAccessor, Mock<IComponentFactory> componentFactory)
        CreateTestSetup()
    {
        var gridManager = new GridManager(100, 100);
        var dataAccessor = new Mock<IDataAccessor>();
        var componentFactory = new Mock<IComponentFactory>();

        return (gridManager, dataAccessor, componentFactory);
    }

    /// <summary>
    /// Creates a test component with a physical pin.
    /// </summary>
    private Component CreateComponentWithPins(string identifier, double x, double y)
    {
        var pin = new PhysicalPin
        {
            Name = "o1",
            OffsetXMicrometers = 10,
            OffsetYMicrometers = 0,
            AngleDegrees = 0
        };

        var component = new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_type",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin> { pin }
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 10,
            HeightMicrometers = 1
        };

        pin.ParentComponent = component;
        return component;
    }

    /// <summary>
    /// Creates a simple routed path with a single straight segment.
    /// </summary>
    private RoutedPath CreateSimpleRoutedPath(double startX, double startY, double endX, double endY)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(startX, startY, endX, endY, 0));
        return path;
    }
}
