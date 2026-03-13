using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Tests for WaveguideFilteringHelper to verify correct filtering of internal group connections.
/// </summary>
public class WaveguideFilteringHelperTests
{
    [Fact]
    public void IsConnectionInternalToGroup_BothComponentsInGroup_ReturnsTrue()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);

        // Act
        var result = WaveguideFilteringHelper.IsConnectionInternalToGroup(connection, group);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsConnectionInternalToGroup_OnlyStartComponentInGroup_ReturnsFalse()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp1);
        // comp2 is NOT in the group

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);

        // Act
        var result = WaveguideFilteringHelper.IsConnectionInternalToGroup(connection, group);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void IsConnectionInternalToGroup_OnlyEndComponentInGroup_ReturnsFalse()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp2);
        // comp1 is NOT in the group

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);

        // Act
        var result = WaveguideFilteringHelper.IsConnectionInternalToGroup(connection, group);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void IsConnectionInternalToGroup_NeitherComponentInGroup_ReturnsFalse()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";

        var group = new ComponentGroup("TestGroup");
        // Neither component is in the group

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);

        // Act
        var result = WaveguideFilteringHelper.IsConnectionInternalToGroup(connection, group);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void IsConnectionInternalToGroup_NullConnection_ReturnsFalse()
    {
        // Arrange
        var group = new ComponentGroup("TestGroup");

        // Act
        var result = WaveguideFilteringHelper.IsConnectionInternalToGroup(null!, group);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void IsConnectionInternalToGroup_NullGroup_ReturnsFalse()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var connection = TestComponentFactory.CreateConnection(comp1, comp2);

        // Act
        var result = WaveguideFilteringHelper.IsConnectionInternalToGroup(connection, null!);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void IsConnectionInternalToAnyGroup_InternalToFirstGroup_ReturnsTrue()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";

        var group1 = new ComponentGroup("Group1");
        group1.AddChild(comp1);
        group1.AddChild(comp2);

        var group2 = new ComponentGroup("Group2");

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);

        var allGroups = new[] { group1, group2 };

        // Act
        var result = WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(connection, allGroups);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsConnectionInternalToAnyGroup_NotInternalToAnyGroup_ReturnsFalse()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.Identifier = "comp1";
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.Identifier = "comp2";
        var comp3 = TestComponentFactory.CreateBasicComponent();
        comp3.Identifier = "comp3";

        var group1 = new ComponentGroup("Group1");
        group1.AddChild(comp1);

        var group2 = new ComponentGroup("Group2");
        group2.AddChild(comp2);

        // comp3 is not in any group

        var connection = TestComponentFactory.CreateConnection(comp1, comp3);

        var allGroups = new[] { group1, group2 };

        // Act
        var result = WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(connection, allGroups);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void CollectAllGroups_NoGroups_ReturnsEmptyList()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var components = new[] { comp1, comp2 };

        // Act
        var result = WaveguideFilteringHelper.CollectAllGroups(components);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void CollectAllGroups_OneGroup_ReturnsGroup()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var group = new ComponentGroup("Group1");
        group.AddChild(comp1);

        var components = new Component[] { comp1, group };

        // Act
        var result = WaveguideFilteringHelper.CollectAllGroups(components);

        // Assert
        result.Count.ShouldBe(1);
        result[0].ShouldBe(group);
    }

    [Fact]
    public void CollectAllGroups_NestedGroups_ReturnsAllGroups()
    {
        // Arrange
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        var nestedGroup = new ComponentGroup("NestedGroup");
        nestedGroup.AddChild(comp1);

        var outerGroup = new ComponentGroup("OuterGroup");
        outerGroup.AddChild(nestedGroup);
        outerGroup.AddChild(comp2);

        var components = new Component[] { outerGroup };

        // Act
        var result = WaveguideFilteringHelper.CollectAllGroups(components);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(outerGroup);
        result.ShouldContain(nestedGroup);
    }
}
