using CAP_Contracts;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CAP_DataAccess.Persistence;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests specifically for HumanReadableName persistence through save/load cycles.
/// Tests the bug where components lose their display names after disk persistence.
/// </summary>
public class HumanReadableNamePersistenceTests
{
    [Fact]
    public async Task SaveAndLoad_ComponentWithHumanReadableName_PreservesName()
    {
        // Arrange
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        // Create component with specific HumanReadableName
        var component = CreateTestComponent("test_comp_1");
        component.HumanReadableName = "My Custom Component Name";

        gridManager.ComponentMover.PlaceComponent(5, 5, component);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier(It.IsAny<string>()))
            .Returns(() => CreateTestComponent("test_comp_1"));

        // Act: Save
        var saveResult = await persistence.SaveAsync("test.lun");
        saveResult.ShouldBeTrue();

        // Verify JSON contains HumanReadableName
        savedJson.ShouldContain("My Custom Component Name");

        // Act: Load
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test.lun", componentFactory.Object);

        // Assert: HumanReadableName is preserved
        var loadedTile = gridManager.TileManager.Tiles[5, 5];
        loadedTile.ShouldNotBeNull();
        loadedTile.Component.HumanReadableName.ShouldBe("My Custom Component Name");
    }

    [Fact]
    public async Task SaveAndLoad_GroupWithChildrenHavingHumanReadableNames_PreservesNames()
    {
        // Arrange: This simulates the user's exact scenario
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        // Create components simulating GC and MMI from PDK
        var gc = CreateTestComponent("gc_comp");
        gc.HumanReadableName = "Grating Coupler TE 1550_1";
        gc.NazcaFunctionName = "ebeam_gc_te1550";

        var mmi = CreateTestComponent("mmi_comp");
        mmi.HumanReadableName = "MMI 1x2_1";
        mmi.NazcaFunctionName = "mmi_1x2";

        // Create group (simulating prefab instance)
        var group = new ComponentGroup("MyPrefab_1");
        group.AddChild(gc);
        group.AddChild(mmi);

        gridManager.ComponentMover.PlaceComponent(3, 3, group);

        string savedJson = "";
        dataAccessor.Setup(d => d.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => savedJson = content)
            .ReturnsAsync(true);

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(() => savedJson);

        // Factory returns components - doesn't matter what HumanReadableName they have,
        // it should be overwritten by the loaded value
        componentFactory.Setup(f => f.CreateComponentByIdentifier(It.IsAny<string>()))
            .Returns<string>(id =>
            {
                var comp = CreateTestComponent(id);
                comp.HumanReadableName = "WRONG NAME FROM FACTORY"; // This should be overwritten
                return comp;
            });

        // Act: Save
        var saveResult = await persistence.SaveAsync("test.lun");
        saveResult.ShouldBeTrue();

        // Verify JSON contains the correct HumanReadableNames
        savedJson.ShouldContain("Grating Coupler TE 1550_1");
        savedJson.ShouldContain("MMI 1x2_1");

        // Act: Load
        gridManager.ComponentMover.DeleteAllComponents();
        await persistence.LoadAsync("test.lun", componentFactory.Object);

        // Assert: Find loaded group
        var loadedTile = gridManager.TileManager.Tiles[3, 3];
        loadedTile.ShouldNotBeNull();
        var loadedGroup = loadedTile.Component.ShouldBeOfType<ComponentGroup>();

        loadedGroup.ChildComponents.Count.ShouldBe(2);

        // CRITICAL: HumanReadableNames should be preserved, NOT showing NazcaFunctionName
        var loadedGC = loadedGroup.ChildComponents[0];
        var loadedMMI = loadedGroup.ChildComponents[1];

        loadedGC.HumanReadableName.ShouldBe("Grating Coupler TE 1550_1",
            "GC HumanReadableName should be preserved from disk, not showing 'ebeam_gc_te1550'");

        loadedMMI.HumanReadableName.ShouldBe("MMI 1x2_1",
            "MMI HumanReadableName should be preserved from disk, not showing 'mmi_1x2'");
    }

    [Fact]
    public async Task SaveAndLoad_BackwardCompatibility_NullHumanReadableName_FallsBackToIdentifier()
    {
        // Arrange: Simulate old files that don't have HumanReadableName in JSON
        var (gridManager, dataAccessor, componentFactory) = CreateTestSetup();
        var persistence = new GridPersistenceWithGroupsManager(gridManager, dataAccessor.Object);

        // Manually craft old-style JSON without humanReadableName field
        string oldStyleJson = @"{
  ""components"": [
    {
      ""x"": 5,
      ""y"": 5,
      ""rotation"": 0,
      ""identifier"": ""old_component"",
      ""sliders"": []
    }
  ],
  ""groups"": []
}";

        dataAccessor.Setup(d => d.ReadAsText(It.IsAny<string>()))
            .Returns(oldStyleJson);

        componentFactory.Setup(f => f.CreateComponentByIdentifier(It.IsAny<string>()))
            .Returns(() => CreateTestComponent("old_component"));

        // Act: Load old file
        await persistence.LoadAsync("old_file.lun", componentFactory.Object);

        // Assert: Falls back to Identifier
        var loadedTile = gridManager.TileManager.Tiles[5, 5];
        loadedTile.ShouldNotBeNull();
        loadedTile.Component.HumanReadableName.ShouldBe("old_component",
            "Should fall back to Identifier for backward compatibility with old files");
    }

    private (GridManager, Mock<IDataAccessor>, Mock<IComponentFactory>) CreateTestSetup()
    {
        var gridManager = new GridManager(20, 20);
        var dataAccessor = new Mock<IDataAccessor>();
        var componentFactory = new Mock<IComponentFactory>();
        return (gridManager, dataAccessor, componentFactory);
    }

    private Component CreateTestComponent(string identifier)
    {
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "pin1",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 0,
                AngleDegrees = 0,
                LogicalPin = new Pin("pin1", 1, MatterType.Light, RectSide.Right)
            }
        };

        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test_nazca",
            "",
            new Part[1, 1] { { new Part() } },
            999,
            identifier,
            DiscreteRotation.R0,
            pins)
        {
            WidthMicrometers = 10,
            HeightMicrometers = 10,
            HumanReadableName = identifier, // Default to identifier
            NazcaFunctionName = "test_nazca"
        };

        foreach (var pin in component.PhysicalPins)
        {
            pin.ParentComponent = component;
        }

        return component;
    }
}
