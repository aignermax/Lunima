using CAP_Contracts;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Grid;
using CAP_Core.Tiles;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using Shouldly;
using Xunit;

namespace UnitTests.Persistence;

/// <summary>
/// Integration tests for ComponentGroup persistence with ViewModels.
/// Tests the full flow from creating groups in UI to saving and loading them.
/// </summary>
public class ComponentGroupPersistenceIntegrationTests : IDisposable
{
    private readonly string _testCatalogPath;
    private readonly string _testDesignPath;
    private readonly ComponentGroupViewModel _groupViewModel;

    public ComponentGroupPersistenceIntegrationTests()
    {
        _testCatalogPath = Path.Combine(Path.GetTempPath(), $"test-catalog-{Guid.NewGuid()}.json");
        _testDesignPath = Path.Combine(Path.GetTempPath(), $"test-design-{Guid.NewGuid()}.cappro");
        _groupViewModel = new ComponentGroupViewModel();
    }

    public void Dispose()
    {
        if (File.Exists(_testCatalogPath))
            File.Delete(_testCatalogPath);
        if (File.Exists(_testDesignPath))
            File.Delete(_testDesignPath);
    }

    [Fact]
    public void ComponentGroupManager_SaveAndReload_PreservesGroups()
    {
        // Arrange - Create test components
        var comp1 = CreateTestComponent("MMI1", 100, 100);
        var comp2 = CreateTestComponent("MMI2", 200, 150);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection>();

        // Create group via manager with test catalog path
        var manager = new ComponentGroupManager(_testCatalogPath);
        var group = manager.CreateGroupFromComponents("Test Group", "Test", components, connections);

        // Save via manager
        manager.SaveGroup(group);

        // Act - Create new manager (simulates app restart)
        var newManager = new ComponentGroupManager(_testCatalogPath);

        // Assert
        newManager.Groups.Count.ShouldBe(1);
        newManager.Groups[0].Name.ShouldBe("Test Group");
        newManager.Groups[0].Components.Count.ShouldBe(2);
    }

    [Fact]
    public void GridPersistenceManager_WithComponentGroups_SavesAndLoadsSuccessfully()
    {
        // Arrange
        var dataAccessor = new TestDataAccessor();
        var gridManager = new GridManager(100, 100);
        var persistenceManager = new GridPersistenceManager(gridManager, dataAccessor);

        // Create and add a component group
        var group = CreateTestGroup("Integration Test Group", 3, 2);
        persistenceManager.AddComponentGroup(group);

        // Act - Save
        var saveResult = persistenceManager.SaveAsync(_testDesignPath).Result;

        // Assert - Save succeeded
        saveResult.ShouldBeTrue();
        dataAccessor.LastWrittenContent.ShouldNotBeNullOrEmpty();

        // Act - Load into new persistence manager
        var newGridManager = new GridManager(100, 100);
        var newPersistenceManager = new GridPersistenceManager(newGridManager, dataAccessor);
        var mockFactory = new TestComponentFactory();
        newPersistenceManager.LoadAsync(_testDesignPath, mockFactory).Wait();

        // Assert - Groups loaded correctly
        newPersistenceManager.ComponentGroups.Count.ShouldBe(1);
        var loadedGroup = newPersistenceManager.ComponentGroups[0];
        loadedGroup.Name.ShouldBe("Integration Test Group");
        loadedGroup.Components.Count.ShouldBe(3);
        loadedGroup.Connections.Count.ShouldBe(2);
    }

    [Fact]
    public void GridPersistenceManager_WithEmptyGroups_HandlesGracefully()
    {
        // Arrange
        var dataAccessor = new TestDataAccessor();
        var gridManager = new GridManager(100, 100);
        var persistenceManager = new GridPersistenceManager(gridManager, dataAccessor);

        // No groups added - just save empty design

        // Act
        var saveResult = persistenceManager.SaveAsync(_testDesignPath).Result;

        // Assert
        saveResult.ShouldBeTrue();

        // Load back
        var newGridManager = new GridManager(100, 100);
        var newPersistenceManager = new GridPersistenceManager(newGridManager, dataAccessor);
        var mockFactory = new TestComponentFactory();
        newPersistenceManager.LoadAsync(_testDesignPath, mockFactory).Wait();

        newPersistenceManager.ComponentGroups.Count.ShouldBe(0);
    }

    [Fact]
    public void ComponentGroupManager_CreateAndPersist_MaintainsRelativePositions()
    {
        // Arrange
        var manager = new ComponentGroupManager(_testCatalogPath);
        var comp1 = CreateTestComponent("MMI", 100, 200);
        var comp2 = CreateTestComponent("MMI", 300, 400);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection>();

        // Act
        var group = manager.CreateGroupFromComponents("Position Test", "Test", components, connections);
        manager.SaveGroup(group);

        // Load back
        var newManager = new ComponentGroupManager(_testCatalogPath);

        // Assert
        newManager.Groups.Count.ShouldBe(1);
        var loadedGroup = newManager.Groups[0];
        loadedGroup.Components[0].RelativeX.ShouldBe(0);
        loadedGroup.Components[0].RelativeY.ShouldBe(0);
        loadedGroup.Components[1].RelativeX.ShouldBe(200); // 300-100
        loadedGroup.Components[1].RelativeY.ShouldBe(200); // 400-200
    }

    private ComponentGroup CreateTestGroup(string name, int componentCount, int connectionCount)
    {
        var group = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = "Integration Test",
            Description = "Test description",
            WidthMicrometers = 150.0,
            HeightMicrometers = 100.0,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        for (int i = 0; i < componentCount; i++)
        {
            group.Components.Add(new ComponentGroupMember
            {
                LocalId = i,
                TemplateName = $"TestComponent{i}",
                RelativeX = i * 60.0,
                RelativeY = i * 40.0,
                Rotation = DiscreteRotation.R0,
                Parameters = new Dictionary<string, double> { { "Slider0", 2.0 } }
            });
        }

        for (int i = 0; i < connectionCount; i++)
        {
            group.Connections.Add(new GroupConnection
            {
                SourceComponentId = i,
                SourcePinName = "out",
                TargetComponentId = i + 1,
                TargetPinName = "in"
            });
        }

        return group;
    }

    private Component CreateTestComponent(string identifier, double x, double y)
    {
        var pins = new List<Pin>
        {
            new Pin("in", 0, MatterType.Light, RectSide.Left),
            new Pin("out", 1, MatterType.Light, RectSide.Right)
        };

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(pins);

        var physicalPins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "in",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 25,
                AngleDegrees = 180,
                LogicalPin = pins[0]
            },
            new PhysicalPin
            {
                Name = "out",
                OffsetXMicrometers = 80,
                OffsetYMicrometers = 25,
                AngleDegrees = 0,
                LogicalPin = pins[1]
            }
        };

        var component = new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_function",
            "",
            parts,
            0,
            identifier,
            DiscreteRotation.R0,
            physicalPins);

        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = 80;
        component.HeightMicrometers = 50;

        return component;
    }

    /// <summary>
    /// Test implementation of IDataAccessor for integration testing.
    /// </summary>
    private class TestDataAccessor : IDataAccessor
    {
        public string LastWrittenContent { get; private set; } = "";
        private string _storedContent = "";

        public Task<bool> Write(string filePath, string content)
        {
            LastWrittenContent = content;
            _storedContent = content;
            return Task.FromResult(true);
        }

        public string ReadAsText(string filePath)
        {
            return _storedContent;
        }

        public bool DoesResourceExist(string resourcePath)
        {
            return !string.IsNullOrEmpty(_storedContent);
        }
    }

    /// <summary>
    /// Test implementation of IComponentFactory for integration testing.
    /// </summary>
    private class TestComponentFactory : CAP_Core.Components.Creation.IComponentFactory
    {
        public Component CreateComponentByIdentifier(string identifier)
        {
            var pins = new List<Pin>
            {
                new Pin("in", 0, MatterType.Light, RectSide.Left),
                new Pin("out", 1, MatterType.Light, RectSide.Right)
            };

            var parts = new Part[1, 1];
            parts[0, 0] = new Part(pins);

            return new Component(
                new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
                new List<Slider>(),
                "test_function",
                "",
                parts,
                0,
                identifier,
                DiscreteRotation.R0,
                new List<PhysicalPin>());
        }

        public Component CreateComponent(int componentTypeNumber)
        {
            return CreateComponentByIdentifier($"Component{componentTypeNumber}");
        }

        public CAP_Core.Helpers.IntVector GetDimensions(int componentTypeNumber)
        {
            return new CAP_Core.Helpers.IntVector(1, 1);
        }

        public void InitializeComponentDrafts(List<Component> componentDrafts)
        {
            // No-op for testing
        }
    }
}
