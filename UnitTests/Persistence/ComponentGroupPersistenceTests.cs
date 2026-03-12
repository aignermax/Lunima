using CAP_Contracts;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Creation;
using CAP_Core.Grid;
using CAP_Core.Tiles;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Persistence;

/// <summary>
/// Unit tests for ComponentGroup persistence in GridPersistenceManager.
/// </summary>
public class ComponentGroupPersistenceTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly Mock<IDataAccessor> _mockDataAccessor;
    private readonly GridManager _gridManager;
    private readonly GridPersistenceManager _persistenceManager;
    private string _savedJson = "";

    public ComponentGroupPersistenceTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test-design-{Guid.NewGuid()}.cappro");
        _mockDataAccessor = new Mock<IDataAccessor>();

        // Setup mock to capture written JSON
        _mockDataAccessor
            .Setup(da => da.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => _savedJson = content)
            .ReturnsAsync(true);

        _mockDataAccessor
            .Setup(da => da.ReadAsText(It.IsAny<string>()))
            .Returns(() => _savedJson);

        _gridManager = new GridManager(100, 100);
        _persistenceManager = new GridPersistenceManager(_gridManager, _mockDataAccessor.Object);
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Fact]
    public void SaveAsync_WithComponentGroup_IncludesGroupInJson()
    {
        // Arrange
        var group = CreateTestGroup("Test MZI", 2, 1);
        _persistenceManager.AddComponentGroup(group);

        // Act
        var result = _persistenceManager.SaveAsync(_testFilePath).Result;

        // Assert
        result.ShouldBeTrue();
        _savedJson.ShouldNotBeNullOrEmpty();
        _savedJson.ShouldContain("componentGroups");
        _savedJson.ShouldContain("Test MZI");
    }

    [Fact]
    public void SaveAsync_WithMultipleGroups_SavesAllGroups()
    {
        // Arrange
        var group1 = CreateTestGroup("MZI", 2, 1);
        var group2 = CreateTestGroup("Ring", 3, 2);
        _persistenceManager.AddComponentGroup(group1);
        _persistenceManager.AddComponentGroup(group2);

        // Act
        var result = _persistenceManager.SaveAsync(_testFilePath).Result;

        // Assert
        result.ShouldBeTrue();
        _savedJson.ShouldContain("MZI");
        _savedJson.ShouldContain("Ring");
        _persistenceManager.ComponentGroups.Count.ShouldBe(2);
    }

    [Fact]
    public void LoadAsync_WithComponentGroups_RestoresGroups()
    {
        // Arrange
        var group = CreateTestGroup("Persistent Group", 2, 1);
        _persistenceManager.AddComponentGroup(group);
        _persistenceManager.SaveAsync(_testFilePath).Wait();

        // Create new persistence manager (simulates app restart)
        var newGridManager = new GridManager(100, 100);
        var newPersistenceManager = new GridPersistenceManager(newGridManager, _mockDataAccessor.Object);
        var mockFactory = new Mock<IComponentFactory>();

        // Act
        newPersistenceManager.LoadAsync(_testFilePath, mockFactory.Object).Wait();

        // Assert
        newPersistenceManager.ComponentGroups.Count.ShouldBe(1);
        newPersistenceManager.ComponentGroups[0].Name.ShouldBe("Persistent Group");
        newPersistenceManager.ComponentGroups[0].Components.Count.ShouldBe(2);
        newPersistenceManager.ComponentGroups[0].Connections.Count.ShouldBe(1);
    }

    [Fact]
    public void LoadAsync_WithOldFormat_DoesNotCrash()
    {
        // Arrange - old format JSON (just component array)
        _savedJson = @"[
            {
                ""x"": 5,
                ""y"": 10,
                ""rotation"": 0,
                ""identifier"": ""TestComponent""
            }
        ]";

        var mockFactory = new Mock<IComponentFactory>();
        var mockComponent = CreateMockComponent("TestComponent");
        mockFactory
            .Setup(f => f.CreateComponentByIdentifier("TestComponent"))
            .Returns(mockComponent);

        // Act
        _persistenceManager.LoadAsync(_testFilePath, mockFactory.Object).Wait();

        // Assert - should load successfully with no groups
        _persistenceManager.ComponentGroups.Count.ShouldBe(0);
    }

    [Fact]
    public void RoundTrip_SaveAndLoad_PreservesGroupData()
    {
        // Arrange
        var originalGroup = CreateTestGroup("Round Trip Test", 3, 2);
        _persistenceManager.AddComponentGroup(originalGroup);
        _persistenceManager.SaveAsync(_testFilePath).Wait();

        // Act
        var newGridManager = new GridManager(100, 100);
        var newPersistenceManager = new GridPersistenceManager(newGridManager, _mockDataAccessor.Object);
        var mockFactory = new Mock<IComponentFactory>();
        newPersistenceManager.LoadAsync(_testFilePath, mockFactory.Object).Wait();

        // Assert
        var loadedGroup = newPersistenceManager.ComponentGroups[0];
        loadedGroup.Id.ShouldBe(originalGroup.Id);
        loadedGroup.Name.ShouldBe(originalGroup.Name);
        loadedGroup.Category.ShouldBe(originalGroup.Category);
        loadedGroup.Description.ShouldBe(originalGroup.Description);
        loadedGroup.WidthMicrometers.ShouldBe(originalGroup.WidthMicrometers);
        loadedGroup.HeightMicrometers.ShouldBe(originalGroup.HeightMicrometers);
        loadedGroup.Components.Count.ShouldBe(originalGroup.Components.Count);
        loadedGroup.Connections.Count.ShouldBe(originalGroup.Connections.Count);
    }

    [Fact]
    public void AddComponentGroup_WithDuplicateId_DoesNotAddTwice()
    {
        // Arrange
        var group = CreateTestGroup("Test", 1, 0);

        // Act
        _persistenceManager.AddComponentGroup(group);
        _persistenceManager.AddComponentGroup(group); // Add same group twice

        // Assert
        _persistenceManager.ComponentGroups.Count.ShouldBe(1);
    }

    [Fact]
    public void RemoveComponentGroup_RemovesFromList()
    {
        // Arrange
        var group = CreateTestGroup("To Remove", 1, 0);
        _persistenceManager.AddComponentGroup(group);
        _persistenceManager.ComponentGroups.Count.ShouldBe(1);

        // Act
        _persistenceManager.RemoveComponentGroup(group.Id);

        // Assert
        _persistenceManager.ComponentGroups.Count.ShouldBe(0);
    }

    [Fact]
    public void ClearComponentGroups_RemovesAllGroups()
    {
        // Arrange
        _persistenceManager.AddComponentGroup(CreateTestGroup("Group1", 1, 0));
        _persistenceManager.AddComponentGroup(CreateTestGroup("Group2", 1, 0));
        _persistenceManager.ComponentGroups.Count.ShouldBe(2);

        // Act
        _persistenceManager.ClearComponentGroups();

        // Assert
        _persistenceManager.ComponentGroups.Count.ShouldBe(0);
    }

    [Fact]
    public void ComponentGroups_ReturnsReadOnlyList()
    {
        // Arrange & Act
        var groups = _persistenceManager.ComponentGroups;

        // Assert
        groups.ShouldNotBeNull();
        groups.ShouldBeAssignableTo<IReadOnlyList<ComponentGroup>>();
    }

    private ComponentGroup CreateTestGroup(string name, int componentCount, int connectionCount)
    {
        var group = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = "Test Category",
            Description = "Test description",
            WidthMicrometers = 100.0,
            HeightMicrometers = 50.0,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        // Add test components
        for (int i = 0; i < componentCount; i++)
        {
            group.Components.Add(new ComponentGroupMember
            {
                LocalId = i,
                TemplateName = $"Component{i}",
                RelativeX = i * 50.0,
                RelativeY = i * 25.0,
                Rotation = DiscreteRotation.R0,
                Parameters = new Dictionary<string, double> { { "Slider0", 1.5 } }
            });
        }

        // Add test connections
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

    private Component CreateMockComponent(string identifier)
    {
        var pins = new List<Pin>
        {
            new Pin("in", 0, MatterType.Light, RectSide.Left),
            new Pin("out", 1, MatterType.Light, RectSide.Right)
        };

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(pins);

        var component = new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_function",
            "",
            parts,
            0,
            identifier,
            DiscreteRotation.R0,
            new List<PhysicalPin>());

        return component;
    }
}
