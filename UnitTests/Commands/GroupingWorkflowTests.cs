using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for component grouping workflow (CreateGroup / Ungroup).
/// Verifies that components can be grouped into reusable templates
/// and that undo/redo works correctly.
/// </summary>
public class GroupingWorkflowTests : IDisposable
{
    private readonly string _testCatalogPath;

    public GroupingWorkflowTests()
    {
        _testCatalogPath = Path.Combine(Path.GetTempPath(), $"test-groups-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_testCatalogPath))
        {
            File.Delete(_testCatalogPath);
        }
    }

    /// <summary>
    /// Verifies that creating a group from 2 components saves it to the catalog.
    /// </summary>
    [Fact]
    public void CreateGroup_TwoComponents_SavesToCatalog()
    {
        var canvas = new DesignCanvasViewModel();
        var groupVm = CreateGroupViewModel();

        var comp1 = CreateComponent(100, 50, 100, 100);
        var comp2 = CreateComponent(100, 50, 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.SelectSingle(vm1);
        canvas.Selection.AddToSelection(vm2);

        int initialGroupCount = groupVm.AvailableGroups.Count;

        var cmd = new CreateGroupCommand(
            canvas,
            groupVm,
            canvas.Selection.SelectedComponents.ToList(),
            "Test Group",
            "Test Category",
            "Test Description");

        cmd.Execute();

        groupVm.AvailableGroups.Count.ShouldBe(initialGroupCount + 1,
            "A new group should be added to the catalog");

        var savedGroup = groupVm.AvailableGroups.Last();
        savedGroup.Name.ShouldBe("Test Group");
        savedGroup.Category.ShouldBe("Test Category");
        savedGroup.ComponentCount.ShouldBe(2);
    }

    /// <summary>
    /// Verifies that creating a group with an internal connection captures the connection.
    /// </summary>
    [Fact]
    public void CreateGroup_WithInternalConnection_CapturesConnection()
    {
        var canvas = new DesignCanvasViewModel();
        var groupVm = CreateGroupViewModel();

        var comp1 = CreateComponentWithPins(100, 50, 100, 100);
        var comp2 = CreateComponentWithPins(100, 50, 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Connect the components
        var pin1 = comp1.PhysicalPins[1]; // east0
        var pin2 = comp2.PhysicalPins[0]; // west0
        canvas.ConnectPins(pin1, pin2);

        canvas.Selection.SelectSingle(vm1);
        canvas.Selection.AddToSelection(vm2);

        var cmd = new CreateGroupCommand(
            canvas,
            groupVm,
            canvas.Selection.SelectedComponents.ToList(),
            "Connected Group");

        cmd.Execute();

        var savedGroup = groupVm.AvailableGroups.Last();
        savedGroup.ConnectionCount.ShouldBe(1,
            "Internal connection should be captured in the group");
    }

    /// <summary>
    /// Verifies that undo removes the group from the catalog.
    /// </summary>
    [Fact]
    public void CreateGroup_Undo_RemovesFromCatalog()
    {
        var canvas = new DesignCanvasViewModel();
        var groupVm = CreateGroupViewModel();

        var comp1 = CreateComponent(100, 50, 100, 100);
        var comp2 = CreateComponent(100, 50, 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.SelectSingle(vm1);
        canvas.Selection.AddToSelection(vm2);

        int initialGroupCount = groupVm.AvailableGroups.Count;

        var cmd = new CreateGroupCommand(
            canvas,
            groupVm,
            canvas.Selection.SelectedComponents.ToList(),
            "Temporary Group");

        cmd.Execute();
        groupVm.AvailableGroups.Count.ShouldBe(initialGroupCount + 1);

        cmd.Undo();
        groupVm.AvailableGroups.Count.ShouldBe(initialGroupCount,
            "Undo should remove the group from the catalog");
    }

    /// <summary>
    /// Verifies that redo re-adds the group to the catalog.
    /// </summary>
    [Fact]
    public void CreateGroup_Redo_ReAddsToGroup()
    {
        var canvas = new DesignCanvasViewModel();
        var groupVm = CreateGroupViewModel();

        var comp1 = CreateComponent(100, 50, 100, 100);
        var comp2 = CreateComponent(100, 50, 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.SelectSingle(vm1);
        canvas.Selection.AddToSelection(vm2);

        int initialGroupCount = groupVm.AvailableGroups.Count;

        var cmd = new CreateGroupCommand(
            canvas,
            groupVm,
            canvas.Selection.SelectedComponents.ToList(),
            "Redo Test Group");

        cmd.Execute();
        cmd.Undo();
        cmd.Execute(); // Redo

        groupVm.AvailableGroups.Count.ShouldBe(initialGroupCount + 1,
            "Redo should re-add the group to the catalog");

        var restoredGroup = groupVm.AvailableGroups.Last();
        restoredGroup.Name.ShouldBe("Redo Test Group");
    }

    /// <summary>
    /// Verifies that creating a group calculates the correct bounding box.
    /// </summary>
    [Fact]
    public void CreateGroup_CalculatesCorrectBoundingBox()
    {
        var canvas = new DesignCanvasViewModel();
        var groupVm = CreateGroupViewModel();

        // Place components at specific positions
        var comp1 = CreateComponent(100, 50, 100, 100); // 100x50 at (100,100)
        var comp2 = CreateComponent(80, 60, 300, 200);  // 80x60 at (300,200)

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.SelectSingle(vm1);
        canvas.Selection.AddToSelection(vm2);

        var cmd = new CreateGroupCommand(
            canvas,
            groupVm,
            canvas.Selection.SelectedComponents.ToList(),
            "Bounding Box Test");

        cmd.Execute();

        var savedGroup = groupVm.AvailableGroups.Last();

        // Bounding box should span from (100,100) to (300+80, 200+60) = (380, 260)
        // Width = 380 - 100 = 280
        // Height = 260 - 100 = 160
        savedGroup.WidthMicrometers.ShouldBe(280, "Width should span both components");
        savedGroup.HeightMicrometers.ShouldBe(160, "Height should span both components");
    }

    /// <summary>
    /// Verifies that the group description is stored correctly.
    /// </summary>
    [Fact]
    public void CreateGroup_StoresDescription()
    {
        var canvas = new DesignCanvasViewModel();
        var groupVm = CreateGroupViewModel();

        var comp1 = CreateComponent(100, 50, 100, 100);
        var comp2 = CreateComponent(100, 50, 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        canvas.Selection.SelectSingle(vm1);
        canvas.Selection.AddToSelection(vm2);

        var description = "This is a test group for verification";

        var cmd = new CreateGroupCommand(
            canvas,
            groupVm,
            canvas.Selection.SelectedComponents.ToList(),
            "Described Group",
            "Test",
            description);

        cmd.Execute();

        // Verify description by loading from catalog
        var catalogPath = GetCatalogPath();
        var manager = new ComponentGroupManager(catalogPath);
        var loadedGroup = manager.Groups.Last();

        loadedGroup.Description.ShouldBe(description,
            "Group description should be persisted to catalog");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ComponentGroupViewModel CreateGroupViewModel()
    {
        // Create a temporary ComponentGroupViewModel with a test catalog path
        Environment.SetEnvironmentVariable("APPDATA", Path.GetTempPath());
        return new ComponentGroupViewModel();
    }

    private string GetCatalogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConnectAPicPro",
            "component-groups.json");
    }

    private static Component CreateComponent(
        double widthMicrometers,
        double heightMicrometers,
        double x = 0,
        double y = 0)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right),
        });

        var component = new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_component",
            "",
            parts,
            0,
            "TestComp",
            DiscreteRotation.R0);

        component.WidthMicrometers = widthMicrometers;
        component.HeightMicrometers = heightMicrometers;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    private static Component CreateComponentWithPins(
        double widthMicrometers,
        double heightMicrometers,
        double x,
        double y)
    {
        var physicalPins = new List<PhysicalPin>
        {
            new() { Name = "west0", OffsetXMicrometers = 0, OffsetYMicrometers = heightMicrometers / 2 },
            new() { Name = "east0", OffsetXMicrometers = widthMicrometers, OffsetYMicrometers = heightMicrometers / 2 },
        };

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right),
        });

        var component = new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_component",
            "",
            parts,
            0,
            "TestComp",
            DiscreteRotation.R0,
            physicalPins);

        component.WidthMicrometers = widthMicrometers;
        component.HeightMicrometers = heightMicrometers;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
