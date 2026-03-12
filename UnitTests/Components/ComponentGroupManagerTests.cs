using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Unit tests for ComponentGroupManager.
/// </summary>
public class ComponentGroupManagerTests : IDisposable
{
    private readonly string _testCatalogPath;
    private readonly ComponentGroupManager _manager;

    public ComponentGroupManagerTests()
    {
        _testCatalogPath = Path.Combine(Path.GetTempPath(), $"test-catalog-{Guid.NewGuid()}.json");
        _manager = new ComponentGroupManager(_testCatalogPath);
    }

    public void Dispose()
    {
        if (File.Exists(_testCatalogPath))
        {
            File.Delete(_testCatalogPath);
        }
    }

    [Fact]
    public void CreateGroupFromComponents_WithValidComponents_CreatesGroup()
    {
        // Arrange
        var comp1 = CreateTestComponent("MMI1", 100, 100, 80, 55);
        var comp2 = CreateTestComponent("MMI2", 200, 150, 80, 55);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection>();

        // Act
        var group = _manager.CreateGroupFromComponents("Test Group", "Test Category", components, connections);

        // Assert
        group.ShouldNotBeNull();
        group.Name.ShouldBe("Test Group");
        group.Category.ShouldBe("Test Category");
        group.Components.Count.ShouldBe(2);
        group.WidthMicrometers.ShouldBe(180); // 200-100+80
        group.HeightMicrometers.ShouldBe(105); // 150-100+55
    }

    [Fact]
    public void CreateGroupFromComponents_WithRelativePositions_CalculatesCorrectly()
    {
        // Arrange
        var comp1 = CreateTestComponent("MMI1", 100, 200, 80, 55);
        var comp2 = CreateTestComponent("MMI2", 300, 400, 80, 55);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection>();

        // Act
        var group = _manager.CreateGroupFromComponents("Test", "Test", components, connections);

        // Assert
        group.Components[0].RelativeX.ShouldBe(0); // 100-100
        group.Components[0].RelativeY.ShouldBe(0); // 200-200
        group.Components[1].RelativeX.ShouldBe(200); // 300-100
        group.Components[1].RelativeY.ShouldBe(200); // 400-200
    }

    [Fact]
    public void CreateGroupFromComponents_WithZeroComponents_ThrowsException()
    {
        // Arrange
        var components = new List<Component>();
        var connections = new List<WaveguideConnection>();

        // Act & Assert
        Should.Throw<ArgumentException>(() =>
            _manager.CreateGroupFromComponents("Test", "Test", components, connections));
    }

    [Fact]
    public void SaveGroup_PersistsToFile()
    {
        // Arrange
        var comp = CreateTestComponent("MMI", 100, 100, 80, 55);
        var components = new List<Component> { comp };
        var group = _manager.CreateGroupFromComponents("Saved Group", "Test", components, new List<WaveguideConnection>());

        // Act
        _manager.SaveGroup(group);

        // Assert
        File.Exists(_testCatalogPath).ShouldBeTrue();
        var content = File.ReadAllText(_testCatalogPath);
        content.ShouldContain("Saved Group");
    }

    [Fact]
    public void SaveGroup_ThenLoad_RestoresGroup()
    {
        // Arrange
        var comp = CreateTestComponent("MMI", 100, 100, 80, 55);
        var components = new List<Component> { comp };
        var group = _manager.CreateGroupFromComponents("Persistent Group", "Test", components, new List<WaveguideConnection>());
        _manager.SaveGroup(group);

        // Act - Create new manager (simulates app restart)
        var newManager = new ComponentGroupManager(_testCatalogPath);

        // Assert
        newManager.Groups.Count.ShouldBe(1);
        newManager.Groups[0].Name.ShouldBe("Persistent Group");
        newManager.Groups[0].Components.Count.ShouldBe(1);
    }

    [Fact]
    public void DeleteGroup_RemovesFromCatalog()
    {
        // Arrange
        var comp = CreateTestComponent("MMI", 100, 100, 80, 55);
        var components = new List<Component> { comp };
        var group = _manager.CreateGroupFromComponents("To Delete", "Test", components, new List<WaveguideConnection>());
        _manager.SaveGroup(group);
        _manager.Groups.Count.ShouldBe(1);

        // Act
        _manager.DeleteGroup(group.Id);

        // Assert
        _manager.Groups.Count.ShouldBe(0);
    }

    [Fact]
    public void CreateGroupFromComponents_WithConnections_StoresConnectionData()
    {
        // Arrange
        var comp1 = CreateTestComponent("MMI1", 100, 100, 80, 55);
        var comp2 = CreateTestComponent("MMI2", 200, 150, 80, 55);

        var pin1 = comp1.PhysicalPins[0];
        var pin2 = comp2.PhysicalPins[0];

        var connection = new WaveguideConnection
        {
            StartPin = pin1,
            EndPin = pin2
        };

        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection> { connection };

        // Act
        var group = _manager.CreateGroupFromComponents("Connected", "Test", components, connections);

        // Assert
        group.Connections.Count.ShouldBe(1);
        group.Connections[0].SourceComponentId.ShouldBe(0);
        group.Connections[0].TargetComponentId.ShouldBe(1);
        group.Connections[0].SourcePinName.ShouldBe(pin1.Name);
        group.Connections[0].TargetPinName.ShouldBe(pin2.Name);
    }

    private Component CreateTestComponent(string name, double x, double y, double width, double height)
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
                OffsetYMicrometers = height / 2,
                AngleDegrees = 180,
                LogicalPin = pins[0]
            },
            new PhysicalPin
            {
                Name = "out",
                OffsetXMicrometers = width,
                OffsetYMicrometers = height / 2,
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
            name,
            DiscreteRotation.R0,
            physicalPins);

        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;

        return component;
    }
}
