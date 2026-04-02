using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation;

/// <summary>
/// Regression tests for issue #445: Phase shifter changes don't update light simulation results.
/// Root cause: ComponentGroup cached its S-Matrix and never invalidated it when a child
/// component's slider changed.
/// </summary>
public class PhaseShifterCacheInvalidationTests
{
    /// <summary>
    /// Creates a component that has a slider (simulating a phase shifter).
    /// The slider is wired into the SMatrix SliderReference.
    /// </summary>
    private static Component CreateComponentWithSlider(out Slider slider)
    {
        var sliderId = Guid.NewGuid();
        slider = new Slider(sliderId, 0, value: 0.0, maxValue: 360.0, minValue: 0.0);

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right)
        });

        var leftIn = parts[0, 0].GetPinAt(RectSide.Left).IDInFlow;
        var rightOut = parts[0, 0].GetPinAt(RectSide.Right).IDOutFlow;
        var rightIn = parts[0, 0].GetPinAt(RectSide.Right).IDInFlow;
        var leftOut = parts[0, 0].GetPinAt(RectSide.Left).IDOutFlow;

        var allPins = Component.GetAllPins(parts).SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var matrix = new SMatrix(allPins, new List<(Guid, double)> { (sliderId, 0.0) });
        matrix.SetValues(new Dictionary<(Guid, Guid), System.Numerics.Complex>
        {
            { (leftIn, rightOut), 1 },
            { (rightIn, leftOut), 1 }
        });

        var connections = new Dictionary<int, SMatrix>
        {
            { StandardWaveLengths.RedNM, matrix }
        };

        var component = new Component(
            connections,
            new List<Slider> { slider },
            "phase_shifter",
            "deltaLength = SLIDER0",
            parts,
            0,
            "PhaseShifterTest",
            new DiscreteRotation());

        component.WidthMicrometers = 10;
        component.HeightMicrometers = 1;

        var logicalIn = parts[0, 0].GetPinAt(RectSide.Left);
        var logicalOut = parts[0, 0].GetPinAt(RectSide.Right);

        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "in",
            ParentComponent = component,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0.5,
            AngleDegrees = 180,
            LogicalPin = logicalIn
        });

        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "out",
            ParentComponent = component,
            OffsetXMicrometers = 10,
            OffsetYMicrometers = 0.5,
            AngleDegrees = 0,
            LogicalPin = logicalOut
        });

        return component;
    }

    [Fact]
    public void GroupSMatrixCache_IsInvalidated_WhenChildSliderChanges()
    {
        // Arrange: create a group with a child that has a slider
        var group = new ComponentGroup("MZI");
        var child = CreateComponentWithSlider(out var slider);
        group.AddChild(child);

        var externalPin = new GroupPin
        {
            PinId = Guid.NewGuid(),
            Name = "ext_in",
            InternalPin = child.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        };
        group.AddExternalPin(externalPin);

        // Act: compute the S-Matrix (populates cache)
        group.EnsureSMatrixComputed();
        group.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0);

        // Act: change the slider value (simulates user adjusting phase)
        slider.Value = 90.0;

        // Assert: group cache must be cleared so next simulation recomputes
        group.WaveLengthToSMatrixMap.Count.ShouldBe(0,
            "Group S-Matrix cache should be invalidated when a child slider changes");
    }

    [Fact]
    public void GroupSMatrixCache_IsRecomputed_AfterChildSliderChange()
    {
        // Arrange
        var group = new ComponentGroup("MZI");
        var child = CreateComponentWithSlider(out var slider);
        group.AddChild(child);

        var externalPin = new GroupPin
        {
            PinId = Guid.NewGuid(),
            Name = "ext_in",
            InternalPin = child.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        };
        group.AddExternalPin(externalPin);

        // Compute initial S-Matrix
        group.EnsureSMatrixComputed();

        // Change slider — invalidates cache
        slider.Value = 180.0;

        // Call EnsureSMatrixComputed again (as SimulationService does)
        group.EnsureSMatrixComputed();

        // Assert: cache is repopulated after recomputation
        group.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0,
            "Group S-Matrix should be recomputed after slider change when EnsureSMatrixComputed is called");
    }

    [Fact]
    public void NestedGroupSMatrixCache_IsInvalidated_WhenGrandchildSliderChanges()
    {
        // Arrange: outer group contains inner group which contains a phase shifter
        var outerGroup = new ComponentGroup("Outer");
        var innerGroup = new ComponentGroup("Inner");
        var child = CreateComponentWithSlider(out var slider);

        innerGroup.AddChild(child);
        outerGroup.AddChild(innerGroup);

        var innerExternalPin = new GroupPin
        {
            PinId = Guid.NewGuid(),
            Name = "ext_in",
            InternalPin = child.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        };
        innerGroup.AddExternalPin(innerExternalPin);

        // Compute both group S-Matrices
        innerGroup.EnsureSMatrixComputed();
        outerGroup.EnsureSMatrixComputed();

        // Verify caches are populated
        innerGroup.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0);
        // outerGroup may or may not compute (depends on external pins), just check inner

        // Act: change the grandchild slider
        slider.Value = 45.0;

        // Assert: inner group cache is invalidated
        innerGroup.WaveLengthToSMatrixMap.Count.ShouldBe(0,
            "Inner group S-Matrix cache should be invalidated when grandchild slider changes");
    }

    [Fact]
    public void GroupSMatrixCache_IsNotInvalidated_WhenChildIsRemoved_AndSliderChanges()
    {
        // Arrange: add child to group, then remove it
        var group = new ComponentGroup("Group");
        var child = CreateComponentWithSlider(out var slider);
        group.AddChild(child);

        var externalPin = new GroupPin
        {
            PinId = Guid.NewGuid(),
            Name = "ext_in",
            InternalPin = child.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        };
        group.AddExternalPin(externalPin);

        group.EnsureSMatrixComputed();

        // Remove child — should unsubscribe from slider events
        group.RemoveChild(child);

        // Re-populate cache by adding new child (to have something to check)
        var newChild = UnitTests.TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(newChild);
        group.EnsureSMatrixComputed();
        var cacheCountBefore = group.WaveLengthToSMatrixMap.Count;

        // Act: change the removed child's slider — should NOT affect the group
        slider.Value = 90.0;

        // Assert: group cache should NOT be cleared since the child was removed
        group.WaveLengthToSMatrixMap.Count.ShouldBe(cacheCountBefore,
            "Group cache should not be invalidated by slider changes from a removed child");
    }
}
