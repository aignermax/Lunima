using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for ComponentGroup.DeepCopy() with nested groups and external connections.
/// Reproduces bug where creating a prefab from a super-group that contains a sub-group with
/// external connections fails with "Sequence contains no matching element".
/// </summary>
public class NestedGroupDeepCopyBugTests
{
    /// <summary>
    /// Reproduces the bug: Create 3 MMIs → Group them → Add Grating Coupler → Connect to group's external pin
    /// → Create super-group → Try to create prefab (calls DeepCopy) → Should NOT crash.
    /// </summary>
    [Fact]
    public void DeepCopy_SuperGroupWithSubGroupHavingExternalConnection_ShouldNotThrow()
    {
        // Arrange: Create 3 MMI components
        var mmi1 = CreateTestComponent("MMI_1", 0, 0);
        var mmi2 = CreateTestComponent("MMI_2", 50, 0);
        var mmi3 = CreateTestComponent("MMI_3", 100, 0);

        // Create a group containing the 3 MMIs (sub-group)
        var subGroup = new ComponentGroup("MMI_Group")
        {
            PhysicalX = 0,
            PhysicalY = 0
        };
        subGroup.AddChild(mmi1);
        subGroup.AddChild(mmi2);
        subGroup.AddChild(mmi3);

        // Add a frozen path between mmi1 and mmi2 inside the sub-group
        var path12 = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = CreateSimplePath(10, 0, 50, 0),
            StartPin = mmi1.PhysicalPins[0],
            EndPin = mmi2.PhysicalPins[0]
        };
        subGroup.AddInternalPath(path12);

        // Expose mmi3's pin as an external pin of the sub-group
        var externalPin = new GroupPin
        {
            PinId = Guid.NewGuid(),
            Name = "out",
            InternalPin = mmi3.PhysicalPins[0],
            RelativeX = 110,
            RelativeY = 0,
            AngleDegrees = 0
        };
        subGroup.AddExternalPin(externalPin);
        subGroup.UpdateGroupBounds();

        // Create a Grating Coupler
        var gratingCoupler = CreateTestComponent("GC", 150, 0);

        // Create super-group containing the sub-group AND the grating coupler
        var superGroup = new ComponentGroup("Super_Group")
        {
            PhysicalX = 0,
            PhysicalY = 0
        };
        superGroup.AddChild(subGroup);
        superGroup.AddChild(gratingCoupler);

        // Add a frozen path from sub-group's external pin to grating coupler
        // CRITICAL: This path connects to subGroup's PhysicalPin, but the internal component
        // (mmi3) is NOT a direct child of superGroup, so it won't be in the componentMap!
        var pathGroupToGC = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = CreateSimplePath(110, 0, 150, 0),
            StartPin = mmi3.PhysicalPins[0], // This is the INTERNAL pin of the sub-group
            EndPin = gratingCoupler.PhysicalPins[0]
        };
        superGroup.AddInternalPath(pathGroupToGC);
        superGroup.UpdateGroupBounds();

        // Act & Assert: Try to create a prefab (which calls DeepCopy)
        // This should NOT throw "Sequence contains no matching element"
        ComponentGroup clonedSuperGroup = null;
        var exception = Record.Exception(() => clonedSuperGroup = superGroup.DeepCopy());

        // Assert
        exception.ShouldBeNull("DeepCopy should not throw when super-group contains sub-group with external connections");
        clonedSuperGroup.ShouldNotBeNull();
        clonedSuperGroup.ChildComponents.Count.ShouldBe(2);
        clonedSuperGroup.InternalPaths.Count.ShouldBe(1);

        // Verify the cloned super-group has the correct structure
        var clonedSubGroup = clonedSuperGroup.ChildComponents
            .OfType<ComponentGroup>()
            .FirstOrDefault(g => g.GroupName == "MMI_Group");
        clonedSubGroup.ShouldNotBeNull();
        clonedSubGroup.ChildComponents.Count.ShouldBe(3);
        clonedSubGroup.ExternalPins.Count.ShouldBe(1);
    }

    /// <summary>
    /// Creates a test component with two pins for testing connections.
    /// </summary>
    private Component CreateTestComponent(string identifier, double x, double y)
    {
        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin>()
        )
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 10,
            HeightMicrometers = 10
        };

        // Add physical pins
        var inputPin = new PhysicalPin
        {
            Name = "in",
            ParentComponent = component,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 5,
            AngleDegrees = 180
        };

        var outputPin = new PhysicalPin
        {
            Name = "out",
            ParentComponent = component,
            OffsetXMicrometers = 10,
            OffsetYMicrometers = 5,
            AngleDegrees = 0
        };

        component.PhysicalPins.Add(inputPin);
        component.PhysicalPins.Add(outputPin);

        return component;
    }

    /// <summary>
    /// Creates a simple straight path between two points.
    /// </summary>
    private RoutedPath CreateSimplePath(double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        return path;
    }
}
