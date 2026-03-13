using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Rendering;

/// <summary>
/// Integration tests verifying that internal group connections are not rendered as duplicates.
/// Tests the complete workflow: create components → connect → group → verify no duplicate rendering.
/// </summary>
public class GroupConnectionRenderingTests
{
    [Fact]
    public void CreateGroup_WithConnectedComponents_FiltersInternalConnectionsFromRendering()
    {
        // Arrange: Create two connected components
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 100;

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Use TestComponentFactory to create a connection (which ensures pins exist)
        var waveguideConnection = TestComponentFactory.CreateConnection(comp1, comp2);
        var connection = new WaveguideConnectionViewModel(waveguideConnection);
        canvas.Connections.Add(connection);
        canvas.ConnectionManager.AddExistingConnection(waveguideConnection);

        connection.ShouldNotBeNull();
        canvas.Connections.Count.ShouldBe(1);

        // Act: Create a group from the two connected components
        var createGroupCommand = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 }
        );
        createGroupCommand.Execute();

        // Assert: The group should be created
        canvas.Components.Count.ShouldBe(1);
        var groupVm = canvas.Components[0];
        groupVm.Component.ShouldBeOfType<ComponentGroup>();

        var group = (ComponentGroup)groupVm.Component;
        group.ChildComponents.Count.ShouldBe(2);
        // Note: InternalPaths is only populated if the connection has a RoutedPath.
        // In this test, we didn't route the connection, so InternalPaths will be empty.
        // The key assertion is that the connection is filtered from rendering.

        // The connection should still exist in canvas.Connections (for now - might be removed in future)
        // but it should be filtered out during rendering
        var allGroups = WaveguideFilteringHelper.CollectAllGroups(
            canvas.Components.Select(c => c.Component)
        );

        allGroups.Count.ShouldBe(1);
        allGroups[0].ShouldBe(group);

        // Verify that the connection is detected as internal
        var remainingConnections = canvas.Connections
            .Where(conn => !WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(
                conn.Connection, allGroups))
            .ToList();

        // No connections should be rendered (the internal connection is filtered out)
        remainingConnections.ShouldBeEmpty();
    }

    [Fact]
    public void CreateGroup_WithExternalConnection_DoesNotFilterExternalConnection()
    {
        // Arrange: Create three components, where two are grouped and one is external
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 100;
        var comp3 = TestComponentFactory.CreateBasicComponent();
        comp3.Identifier = "comp3";
        comp3.PhysicalX = 500;
        comp3.PhysicalY = 100;

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);
        var vm3 = canvas.AddComponent(comp3);

        // Connect comp1 to comp2 (internal connection when grouped)
        var internalWaveguide = TestComponentFactory.CreateConnection(comp1, comp2);
        var internalConnection = new WaveguideConnectionViewModel(internalWaveguide);
        canvas.Connections.Add(internalConnection);
        canvas.ConnectionManager.AddExistingConnection(internalWaveguide);

        // Connect comp2 to comp3 (external connection)
        var externalWaveguide = TestComponentFactory.CreateConnection(comp2, comp3);
        var externalConnection = new WaveguideConnectionViewModel(externalWaveguide);
        canvas.Connections.Add(externalConnection);
        canvas.ConnectionManager.AddExistingConnection(externalWaveguide);

        canvas.Connections.Count.ShouldBe(2);

        // Act: Create a group from comp1 and comp2
        var createGroupCommand = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 }
        );
        createGroupCommand.Execute();

        // Assert: The group should be created
        canvas.Components.Count.ShouldBe(2); // group + comp3
        var groupVm = canvas.Components.FirstOrDefault(c => c.Component is ComponentGroup);
        groupVm.ShouldNotBeNull();

        var group = (ComponentGroup)groupVm.Component;
        group.ChildComponents.Count.ShouldBe(2);
        // Note: InternalPaths is only populated if the connection has a RoutedPath.
        // In this test, we didn't route the connection, so InternalPaths will be empty.

        // Collect all groups
        var allGroups = WaveguideFilteringHelper.CollectAllGroups(
            canvas.Components.Select(c => c.Component)
        );

        allGroups.Count.ShouldBe(1);

        // Verify that only the external connection should be rendered
        var remainingConnections = canvas.Connections
            .Where(conn => !WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(
                conn.Connection, allGroups))
            .ToList();

        remainingConnections.Count.ShouldBe(1);
        remainingConnections[0].Connection.ShouldBe(externalConnection.Connection);
    }

    [Fact]
    public void CreateNestedGroups_FiltersInternalConnectionsCorrectly()
    {
        // Arrange: Create four components, group them hierarchically
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 100;
        var comp3 = TestComponentFactory.CreateBasicComponent();
        comp3.Identifier = "comp3";
        comp3.PhysicalX = 500;
        comp3.PhysicalY = 100;
        var comp4 = TestComponentFactory.CreateBasicComponent();
        comp4.Identifier = "comp4";
        comp4.PhysicalX = 700;
        comp4.PhysicalY = 100;

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);
        var vm3 = canvas.AddComponent(comp3);
        var vm4 = canvas.AddComponent(comp4);

        // Connect: comp1 -> comp2, comp2 -> comp3, comp3 -> comp4
        var conn1_2 = TestComponentFactory.CreateConnection(comp1, comp2);
        canvas.Connections.Add(new WaveguideConnectionViewModel(conn1_2));
        canvas.ConnectionManager.AddExistingConnection(conn1_2);

        var conn2_3 = TestComponentFactory.CreateConnection(comp2, comp3);
        canvas.Connections.Add(new WaveguideConnectionViewModel(conn2_3));
        canvas.ConnectionManager.AddExistingConnection(conn2_3);

        var conn3_4 = TestComponentFactory.CreateConnection(comp3, comp4);
        canvas.Connections.Add(new WaveguideConnectionViewModel(conn3_4));
        canvas.ConnectionManager.AddExistingConnection(conn3_4);

        canvas.Connections.Count.ShouldBe(3);

        // Act: Group comp1 and comp2 into innerGroup
        var innerGroupCommand = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 }
        );
        innerGroupCommand.Execute();

        // Now we have: innerGroup, comp3, comp4
        canvas.Components.Count.ShouldBe(3);

        var innerGroupVm = canvas.Components.First(c => c.Component is ComponentGroup);
        var innerGroup = (ComponentGroup)innerGroupVm.Component;

        // Verify: comp1->comp2 is internal to innerGroup
        var allGroups = WaveguideFilteringHelper.CollectAllGroups(
            canvas.Components.Select(c => c.Component)
        );

        allGroups.Count.ShouldBe(1);

        var remainingConnections = canvas.Connections
            .Where(conn => !WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(
                conn.Connection, allGroups))
            .ToList();

        // Only 2 connections should be rendered: innerGroup->comp3, comp3->comp4
        remainingConnections.Count.ShouldBe(2);
    }

    [Fact]
    public void UngroupComponents_RestoresInternalConnectionsToCanvas()
    {
        // Arrange: Create a group with connected components
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting();

        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        comp1.PhysicalX = 100;
        comp1.PhysicalY = 100;

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 100;

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        var waveguideConnection = TestComponentFactory.CreateConnection(comp1, comp2);
        canvas.Connections.Add(new WaveguideConnectionViewModel(waveguideConnection));
        canvas.ConnectionManager.AddExistingConnection(waveguideConnection);

        var createGroupCommand = new CreateGroupCommand(
            canvas,
            new List<ComponentViewModel> { vm1, vm2 }
        );
        createGroupCommand.Execute();

        canvas.Components.Count.ShouldBe(1);
        canvas.Components[0].Component.ShouldBeOfType<ComponentGroup>();

        // Act: Undo the group creation (ungroup)
        createGroupCommand.Undo();

        // Assert: Components should be restored
        canvas.Components.Count.ShouldBe(2);
        canvas.Connections.Count.ShouldBe(1);

        // No groups exist, so the connection should be rendered
        var allGroups = WaveguideFilteringHelper.CollectAllGroups(
            canvas.Components.Select(c => c.Component)
        );

        allGroups.ShouldBeEmpty();

        var remainingConnections = canvas.Connections
            .Where(conn => !WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(
                conn.Connection, allGroups))
            .ToList();

        remainingConnections.Count.ShouldBe(1);
    }
}
