using CAP_Contracts;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using CAP_DataAccess.Persistence;
using Moq;
using Shouldly;
using System.Text;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for ComponentGroup persistence with GridPersistenceWithGroupsManager.
/// Tests full save/load round-trip scenarios.
/// </summary>
public class GridPersistenceIntegrationTests
{
    [Fact]
    public async Task SaveAndLoad_SimpleComponents_RoundTripSucceeds()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var comp1 = CreateTestComponent("comp1");
        var comp2 = CreateTestComponent("comp2");

        gridManager.ComponentMover.PlaceComponent(0, 0, comp1);
        gridManager.ComponentMover.PlaceComponent(2, 2, comp2);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        // Configure factory to return components when loading
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateTestComponent("comp1"));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateTestComponent("comp2"));

        // Act - Save
        var saveResult = await persistence.SaveAsync("test.json");

        // Assert - Save succeeded
        saveResult.ShouldBeTrue();
        savedJson.ShouldNotBeNullOrEmpty();

        // Act - Load
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test.json", componentFactory.Object);

        // Assert - Components restored
        var tile1 = gridManager.TileManager.Tiles[0, 0];
        var tile2 = gridManager.TileManager.Tiles[2, 2];

        tile1.ShouldNotBeNull();
        tile2.ShouldNotBeNull();
        tile1.Component.Identifier.ShouldBe("comp1");
        tile2.Component.Identifier.ShouldBe("comp2");
    }

    [Fact]
    public async Task SaveAndLoad_ComponentGroup_RoundTripSucceeds()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var comp1 = CreateTestComponent("comp1");
        var comp2 = CreateTestComponent("comp2");

        var group = new ComponentGroup("TestGroup")
        {
            Description = "Test group description",
            PhysicalX = 10,
            PhysicalY = 20
        };
        group.AddChild(comp1);
        group.AddChild(comp2);

        gridManager.ComponentMover.PlaceComponent(1, 1, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        // Configure factory to return the child components when loading
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateTestComponent("comp1"));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateTestComponent("comp2"));

        // Act - Save
        var saveResult = await persistence.SaveAsync("test.json");

        // Assert - Save succeeded
        saveResult.ShouldBeTrue();
        savedJson.ShouldContain("TestGroup");
        savedJson.ShouldContain("comp1");
        savedJson.ShouldContain("comp2");

        // Act - Load
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test.json", componentFactory.Object);

        // Assert - Group restored
        var tile = gridManager.TileManager.Tiles[1, 1];
        tile.ShouldNotBeNull();
        tile.Component.ShouldBeOfType<ComponentGroup>();

        var loadedGroup = (ComponentGroup)tile.Component;
        loadedGroup.GroupName.ShouldBe("TestGroup");
        loadedGroup.Description.ShouldBe("Test group description");
        loadedGroup.ChildComponents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SaveAndLoad_GroupWithFrozenPaths_RestoresPaths()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var comp1 = CreateTestComponent("comp1");
        var comp2 = CreateTestComponent("comp2");

        var group = new ComponentGroup("PathGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Create frozen path
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 50, 0, 0));
        path.Segments.Add(new BendSegment(50, 10, 10, 0, 90));

        var frozenPath = new FrozenWaveguidePath
        {
            Path = path,
            StartPin = comp1.PhysicalPins[0],
            EndPin = comp2.PhysicalPins[0]
        };
        group.AddInternalPath(frozenPath);

        gridManager.ComponentMover.PlaceComponent(0, 0, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateTestComponent("comp1"));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateTestComponent("comp2"));

        // Act - Save and Load
        await persistence.SaveAsync("test.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test.json", componentFactory.Object);

        // Assert - Frozen paths restored
        var tile = gridManager.TileManager.Tiles[0, 0];
        var loadedGroup = (ComponentGroup)tile.Component;

        loadedGroup.InternalPaths.Count.ShouldBe(1);
        var loadedPath = loadedGroup.InternalPaths[0];
        loadedPath.Path.Segments.Count.ShouldBe(2);
        loadedPath.Path.Segments[0].ShouldBeOfType<StraightSegment>();
        loadedPath.Path.Segments[1].ShouldBeOfType<BendSegment>();
    }

    [Fact]
    public async Task SaveAndLoad_GroupWithExternalPins_RestoresPins()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var comp1 = CreateTestComponent("comp1");
        var group = new ComponentGroup("PinGroup");
        group.AddChild(comp1);

        var groupPin = new GroupPin
        {
            Name = "ExternalOut",
            InternalPin = comp1.PhysicalPins[0],
            RelativeX = 10,
            RelativeY = 20,
            AngleDegrees = 90
        };
        group.AddExternalPin(groupPin);

        gridManager.ComponentMover.PlaceComponent(0, 0, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateTestComponent("comp1"));

        // Act - Save and Load
        await persistence.SaveAsync("test.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test.json", componentFactory.Object);

        // Assert - External pins restored
        var tile = gridManager.TileManager.Tiles[0, 0];
        var loadedGroup = (ComponentGroup)tile.Component;

        loadedGroup.ExternalPins.Count.ShouldBe(1);
        var loadedPin = loadedGroup.ExternalPins[0];
        loadedPin.Name.ShouldBe("ExternalOut");
        loadedPin.RelativeX.ShouldBe(10);
        loadedPin.RelativeY.ShouldBe(20);
        loadedPin.AngleDegrees.ShouldBe(90);
    }

    [Fact]
    public async Task SaveAndLoad_NestedGroups_RestoresHierarchy()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var comp1 = CreateTestComponent("comp1");
        var comp2 = CreateTestComponent("comp2");

        var innerGroup = new ComponentGroup("InnerGroup");
        innerGroup.AddChild(comp1);

        var outerGroup = new ComponentGroup("OuterGroup");
        outerGroup.AddChild(comp2);
        outerGroup.AddChild(innerGroup);

        gridManager.ComponentMover.PlaceComponent(0, 0, outerGroup);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp1"))
            .Returns(() => CreateTestComponent("comp1"));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("comp2"))
            .Returns(() => CreateTestComponent("comp2"));

        // Act - Save and Load
        await persistence.SaveAsync("test.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test.json", componentFactory.Object);

        // Assert - Nested hierarchy restored
        var tile = gridManager.TileManager.Tiles[0, 0];
        var loadedOuterGroup = (ComponentGroup)tile.Component;

        loadedOuterGroup.GroupName.ShouldBe("OuterGroup");
        loadedOuterGroup.ChildComponents.Count.ShouldBe(2);

        var loadedInnerGroup = loadedOuterGroup.ChildComponents
            .OfType<ComponentGroup>()
            .FirstOrDefault();

        loadedInnerGroup.ShouldNotBeNull();
        loadedInnerGroup!.GroupName.ShouldBe("InnerGroup");
        loadedInnerGroup.ChildComponents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SaveAndLoad_MixedComponentsAndGroups_AllRestored()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        var comp1 = CreateTestComponent("standalone1");
        var comp2 = CreateTestComponent("grouped1");
        var comp3 = CreateTestComponent("grouped2");

        var group = new ComponentGroup("MixedGroup");
        group.AddChild(comp2);
        group.AddChild(comp3);

        gridManager.ComponentMover.PlaceComponent(0, 0, comp1);
        gridManager.ComponentMover.PlaceComponent(2, 2, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier("standalone1"))
            .Returns(() => CreateTestComponent("standalone1"));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("grouped1"))
            .Returns(() => CreateTestComponent("grouped1"));
        componentFactory.Setup(f => f.CreateComponentByIdentifier("grouped2"))
            .Returns(() => CreateTestComponent("grouped2"));

        // Act - Save and Load
        await persistence.SaveAsync("test.json");
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test.json", componentFactory.Object);

        // Assert - Both standalone and grouped components restored
        var tile1 = gridManager.TileManager.Tiles[0, 0];
        var tile2 = gridManager.TileManager.Tiles[2, 2];

        tile1.Component.Identifier.ShouldBe("standalone1");
        tile2.Component.ShouldBeOfType<ComponentGroup>();

        var loadedGroup = (ComponentGroup)tile2.Component;
        loadedGroup.ChildComponents.Count.ShouldBe(2);
    }

    /// <summary>
    /// Creates a test setup with GridManager, mock IDataAccessor, and mock IComponentFactory.
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
    /// Creates a test component with a single pin.
    /// </summary>
    private Component CreateTestComponent(string identifier)
    {
        var pin = new PhysicalPin
        {
            Name = "o1",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0
        };

        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test_type",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin> { pin }
        );

        pin.ParentComponent = component;
        return component;
    }
}
