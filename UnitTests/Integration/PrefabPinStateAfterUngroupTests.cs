using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Tests for issue #394: Prefab pins turn red after ungroup on app restart.
///
/// Root cause: GroupTemplateSerializer did not serialize PhysicalPin.LogicalPin data.
/// After deserialization (simulating app restart), LogicalPin was null, causing pins
/// to render red in the UI (red = no LogicalPin, green = has LogicalPin).
/// </summary>
public class PrefabPinStateAfterUngroupTests
{
    [Fact]
    public void PinsRetainLogicalPinAfterSerializationRoundtrip()
    {
        // Arrange: create component with LogicalPins set (as PDK components would have)
        var comp1 = CreateComponentWithLogicalPins("GC1", 0, 0);
        var comp2 = CreateComponentWithLogicalPins("GC2", 100, 0);

        var group = new ComponentGroup("TestPrefab");
        group.AddChild(comp1);
        group.AddChild(comp2);

        // Verify LogicalPins exist before serialization
        comp1.PhysicalPins.ShouldAllBe(p => p.LogicalPin != null);
        comp2.PhysicalPins.ShouldAllBe(p => p.LogicalPin != null);

        // Act: serialize and deserialize (simulates app restart)
        var json = GroupTemplateSerializer.Serialize(group);
        var loadedGroup = GroupTemplateSerializer.Deserialize(json);

        // Assert: LogicalPins should be restored after deserialization
        loadedGroup.ShouldNotBeNull();
        foreach (var child in loadedGroup!.ChildComponents)
        {
            foreach (var pin in child.PhysicalPins)
            {
                pin.LogicalPin.ShouldNotBeNull(
                    $"Pin '{pin.Name}' on '{child.Identifier}' lost LogicalPin after deserialization");
            }
        }
    }

    [Fact]
    public void PinsRetainLogicalPinAfterPrefabInstantiateAndUngroup()
    {
        // Arrange: create group, serialize as template, deserialize (simulates restart)
        var comp1 = CreateComponentWithLogicalPins("GC1", 0, 0);
        var comp2 = CreateComponentWithLogicalPins("GC2", 100, 0);

        var frozenPath = CreateFrozenPath(
            comp1.PhysicalPins[1], comp2.PhysicalPins[0]);

        var group = new ComponentGroup("TestPrefab");
        group.AddChild(comp1);
        group.AddChild(comp2);
        group.AddInternalPath(frozenPath);

        // Serialize and deserialize (app restart)
        var json = GroupTemplateSerializer.Serialize(group);
        var loadedTemplate = GroupTemplateSerializer.Deserialize(json);
        loadedTemplate.ShouldNotBeNull();

        // Instantiate from template (simulates dragging prefab from library)
        var libraryManager = new GroupLibraryManager();
        var template = new GroupTemplate
        {
            Name = "TestPrefab",
            TemplateGroup = loadedTemplate!,
            Source = "User"
        };
        var instance = libraryManager.InstantiateTemplate(template, 50, 50);

        // Act: simulate ungroup by extracting children
        var children = instance.ChildComponents.ToList();

        // Assert: all pins on ungrouped children should have LogicalPin (not red)
        foreach (var child in children)
        {
            foreach (var pin in child.PhysicalPins)
            {
                pin.LogicalPin.ShouldNotBeNull(
                    $"Pin '{pin.Name}' on '{child.Identifier}' is null (would render red)");
                pin.LogicalPin.MatterType.ShouldBe(MatterType.Light);
            }
        }
    }

    [Fact]
    public void LogicalPinPropertiesPreservedAfterRoundtrip()
    {
        // Arrange: component with specific LogicalPin properties
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "left",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 15,
                AngleDegrees = 180,
                LogicalPin = new Pin("left", 0, MatterType.Light, RectSide.Left)
            },
            new PhysicalPin
            {
                Name = "right",
                OffsetXMicrometers = 30,
                OffsetYMicrometers = 15,
                AngleDegrees = 0,
                LogicalPin = new Pin("right", 1, MatterType.Light, RectSide.Right)
            }
        };

        var comp = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test_comp", "", new Part[1, 1] { { new Part() } },
            1, "comp_test", DiscreteRotation.R0, pins)
        {
            PhysicalX = 0, PhysicalY = 0,
            WidthMicrometers = 30, HeightMicrometers = 30
        };

        var group = new ComponentGroup("Test");
        group.AddChild(comp);

        // Act
        var json = GroupTemplateSerializer.Serialize(group);
        var loaded = GroupTemplateSerializer.Deserialize(json);

        // Assert: LogicalPin properties match original
        loaded.ShouldNotBeNull();
        var loadedComp = loaded!.ChildComponents[0];

        var leftPin = loadedComp.PhysicalPins.First(p => p.Name == "left");
        leftPin.LogicalPin.ShouldNotBeNull();
        leftPin.LogicalPin.PinNumber.ShouldBe(0);
        leftPin.LogicalPin.MatterType.ShouldBe(MatterType.Light);
        leftPin.LogicalPin.Side.ShouldBe(RectSide.Left);

        var rightPin = loadedComp.PhysicalPins.First(p => p.Name == "right");
        rightPin.LogicalPin.ShouldNotBeNull();
        rightPin.LogicalPin.PinNumber.ShouldBe(1);
        rightPin.LogicalPin.MatterType.ShouldBe(MatterType.Light);
        rightPin.LogicalPin.Side.ShouldBe(RectSide.Right);
    }

    [Fact]
    public void ConnectionsIntactAfterPrefabRoundtripAndUngroup()
    {
        // Arrange
        var comp1 = CreateComponentWithLogicalPins("GC1", 0, 0);
        var comp2 = CreateComponentWithLogicalPins("GC2", 100, 0);

        var frozenPath = CreateFrozenPath(
            comp1.PhysicalPins[1], comp2.PhysicalPins[0]);

        var group = new ComponentGroup("TestPrefab");
        group.AddChild(comp1);
        group.AddChild(comp2);
        group.AddInternalPath(frozenPath);

        // Act: serialize/deserialize roundtrip
        var json = GroupTemplateSerializer.Serialize(group);
        var loaded = GroupTemplateSerializer.Deserialize(json);

        // Assert: frozen path is preserved with valid pin references
        loaded.ShouldNotBeNull();
        loaded!.InternalPaths.Count.ShouldBe(1);

        var path = loaded.InternalPaths[0];
        path.StartPin.ShouldNotBeNull();
        path.EndPin.ShouldNotBeNull();
        path.StartPin.ParentComponent.ShouldNotBeNull();
        path.EndPin.ParentComponent.ShouldNotBeNull();
        path.StartPin.LogicalPin.ShouldNotBeNull();
        path.EndPin.LogicalPin.ShouldNotBeNull();
    }

    [Fact]
    public void PinsWithoutLogicalPinStillDeserializeCorrectly()
    {
        // Ensure backward compatibility: pins without LogicalPin stay null
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "port1",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 0,
                AngleDegrees = 0
                // No LogicalPin set
            }
        };

        var comp = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test", "", new Part[1, 1] { { new Part() } },
            1, "comp_none", DiscreteRotation.R0, pins)
        {
            PhysicalX = 0, PhysicalY = 0,
            WidthMicrometers = 10, HeightMicrometers = 10
        };

        var group = new ComponentGroup("Test");
        group.AddChild(comp);

        var json = GroupTemplateSerializer.Serialize(group);
        var loaded = GroupTemplateSerializer.Deserialize(json);

        loaded.ShouldNotBeNull();
        loaded!.ChildComponents[0].PhysicalPins[0].LogicalPin.ShouldBeNull();
    }

    private Component CreateComponentWithLogicalPins(string name, double x, double y)
    {
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "left",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 15,
                AngleDegrees = 180,
                LogicalPin = new Pin("left", 0, MatterType.Light, RectSide.Left)
            },
            new PhysicalPin
            {
                Name = "right",
                OffsetXMicrometers = 30,
                OffsetYMicrometers = 15,
                AngleDegrees = 0,
                LogicalPin = new Pin("right", 1, MatterType.Light, RectSide.Right)
            }
        };

        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            $"ebeam_{name.ToLower()}", "",
            new Part[1, 1] { { new Part() } },
            1, $"comp_{Guid.NewGuid():N}",
            DiscreteRotation.R0, pins)
        {
            PhysicalX = x, PhysicalY = y,
            WidthMicrometers = 30, HeightMicrometers = 30,
            HumanReadableName = name
        };
    }

    private FrozenWaveguidePath CreateFrozenPath(
        PhysicalPin startPin, PhysicalPin endPin)
    {
        var routedPath = new RoutedPath();
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();
        routedPath.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));

        return new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = routedPath,
            StartPin = startPin,
            EndPin = endPin
        };
    }
}
