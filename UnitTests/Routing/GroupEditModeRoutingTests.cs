using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;

namespace UnitTests.Routing;

/// <summary>
/// Tests for issue #242: A* routing fails after re-entering group edit mode.
/// Verifies ConnectionManager state is properly cleared during mode transitions.
/// </summary>
public class GroupEditModeRoutingTests
{
    private static Component CreateComponentWithPins(
        double x, double y, string id)
    {
        const double size = 15;
        var pins = new List<PhysicalPin>
        {
            new() { Name = "left", OffsetXMicrometers = 0, OffsetYMicrometers = size / 2,
                     AngleDegrees = 180, LogicalPin = new Pin("left", 0, MatterType.Light, RectSide.Left) },
            new() { Name = "right", OffsetXMicrometers = size, OffsetYMicrometers = size / 2,
                     AngleDegrees = 0, LogicalPin = new Pin("right", 1, MatterType.Light, RectSide.Right) }
        };

        return new Component(
            new Dictionary<int, SMatrix>(), new List<Slider>(), "test", "",
            new Part[1, 1] { { new Part() } }, -1, id, new DiscreteRotation(), pins)
        {
            PhysicalX = x, PhysicalY = y, WidthMicrometers = size, HeightMicrometers = size
        };
    }

    private static RoutedPath CreateSimpleRoutedPath(double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        return path;
    }

    private static ComponentGroup CreateGroupWithFrozenPath()
    {
        var comp1 = CreateComponentWithPins(100, 100, "comp1");
        var comp2 = CreateComponentWithPins(200, 100, "comp2");

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);

        group.AddInternalPath(new FrozenWaveguidePath
        {
            StartPin = comp1.PhysicalPins[1],
            EndPin = comp2.PhysicalPins[0],
            Path = CreateSimpleRoutedPath(115, 107.5, 200, 107.5)
        });

        return group;
    }

    [Fact]
    public void LoadGroupAsSubCanvas_ClearsConnectionManager()
    {
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins(100, 100, "comp1");
        var comp2 = CreateComponentWithPins(200, 100, "comp2");

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);
        canvas.AddComponent(group);

        // Add a stale connection before entering edit mode
        canvas.ConnectionManager.AddExistingConnection(
            new WaveguideConnection { StartPin = comp1.PhysicalPins[1], EndPin = comp2.PhysicalPins[0] });
        canvas.ConnectionManager.Connections.Count.ShouldBe(1);

        // Act
        canvas.EnterGroupEditMode(group);

        // Assert - stale connection cleared; only frozen path connections remain
        canvas.ConnectionManager.Connections.Count.ShouldBe(group.InternalPaths.Count);
    }

    [Fact]
    public void RestoreCanvasState_ClearsStaleSubCanvasConnections()
    {
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins(100, 100, "comp1");
        var comp2 = CreateComponentWithPins(200, 100, "comp2");
        canvas.AddComponent(comp1);
        canvas.AddComponent(comp2);

        var connection = canvas.ConnectionManager.AddConnectionDeferred(
            comp1.PhysicalPins[1], comp2.PhysicalPins[0]);

        var group = new ComponentGroup("EditGroup");
        group.AddChild(CreateComponentWithPins(50, 50, "child1"));
        canvas.EnterGroupEditMode(group);

        // Simulate sub-canvas connection added during editing
        var subConn = new WaveguideConnection
        {
            StartPin = group.ChildComponents[0].PhysicalPins[0],
            EndPin = group.ChildComponents[0].PhysicalPins[1]
        };
        canvas.ConnectionManager.AddExistingConnection(subConn);

        // Act
        canvas.ExitGroupEditMode();

        // Assert - root connections restored, sub-canvas connections removed
        canvas.ConnectionManager.Connections.ShouldContain(connection);
        canvas.ConnectionManager.Connections.ShouldNotContain(subConn);
        canvas.ConnectionManager.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public void ReenterGroupEditMode_NoStaleConnectionAccumulation()
    {
        // Core reproduction of issue #242
        var canvas = new DesignCanvasViewModel();
        var group = CreateGroupWithFrozenPath();
        canvas.AddComponent(group);

        // First enter/exit cycle
        canvas.EnterGroupEditMode(group);
        int firstEntryCount = canvas.ConnectionManager.Connections.Count;
        canvas.ExitGroupEditMode();

        // Second enter (re-enter)
        canvas.EnterGroupEditMode(group);
        int secondEntryCount = canvas.ConnectionManager.Connections.Count;

        secondEntryCount.ShouldBe(firstEntryCount,
            "Re-entering group edit mode should not accumulate stale connections");
    }

    [Fact]
    public void MultipleEnterExitCycles_NoConnectionAccumulation()
    {
        var canvas = new DesignCanvasViewModel();
        var group = CreateGroupWithFrozenPath();
        canvas.AddComponent(group);

        canvas.EnterGroupEditMode(group);
        int firstCount = canvas.ConnectionManager.Connections.Count;
        canvas.ExitGroupEditMode();

        canvas.EnterGroupEditMode(group);
        int secondCount = canvas.ConnectionManager.Connections.Count;
        canvas.ExitGroupEditMode();

        canvas.EnterGroupEditMode(group);
        int thirdCount = canvas.ConnectionManager.Connections.Count;
        canvas.ExitGroupEditMode();

        secondCount.ShouldBe(firstCount);
        thirdCount.ShouldBe(firstCount);
    }

    [Fact]
    public void EnterGroupEditMode_InitializesAStarGrid()
    {
        var canvas = new DesignCanvasViewModel();
        var group = CreateGroupWithFrozenPath();
        canvas.AddComponent(group);

        canvas.EnterGroupEditMode(group);

        WaveguideConnection.SharedRouter.PathfindingGrid.ShouldNotBeNull(
            "Pathfinding grid should be initialized after entering group edit mode");
    }

    [Fact]
    public void BackupAndRestore_PreservesRootConnectionManagerState()
    {
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponentWithPins(100, 100, "comp1");
        var comp2 = CreateComponentWithPins(200, 100, "comp2");
        canvas.AddComponent(comp1);
        canvas.AddComponent(comp2);

        canvas.ConnectionManager.AddConnectionDeferred(
            comp1.PhysicalPins[1], comp2.PhysicalPins[0]);
        canvas.ConnectionManager.Connections.Count.ShouldBe(1);

        var group = new ComponentGroup("EditGroup");
        group.AddChild(CreateComponentWithPins(50, 50, "child1"));

        canvas.EnterGroupEditMode(group);
        canvas.ExitGroupEditMode();

        canvas.ConnectionManager.Connections.Count.ShouldBe(1,
            "Root connections should be restored after exiting group edit mode");
    }
}
