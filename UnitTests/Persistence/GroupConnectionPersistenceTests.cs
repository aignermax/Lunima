using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Persistence;

/// <summary>
/// Tests for persisting waveguide connections between ComponentGroups.
/// Covers the bug where group-to-group connections were silently dropped on save
/// because WaveguideConnection.StartPin.ParentComponent points to an internal child
/// component (not the group), causing FindIndex to return -1.
/// </summary>
public class GroupConnectionPersistenceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a simple component with one named physical pin.</summary>
    private static Component CreateComponent(string id, string pinName, double x = 0, double y = 0)
    {
        var pin = new PhysicalPin
        {
            Name = pinName,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0
        };
        var comp = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test",
            "",
            new CAP_Core.Components.Core.Part[1, 1] { { new CAP_Core.Components.Core.Part() } },
            -1,
            id,
            new DiscreteRotation(),
            new List<PhysicalPin> { pin })
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 10,
            HeightMicrometers = 10
        };
        pin.ParentComponent = comp;
        return comp;
    }

    /// <summary>
    /// Creates a group containing the given component and exposes its pin as an external pin.
    /// </summary>
    private static ComponentGroup CreateGroupWithExternalPin(
        Component child, string externalPinName)
    {
        var group = new ComponentGroup($"Group_{child.Identifier}");
        group.AddChild(child);

        var groupPin = new GroupPin
        {
            Name = externalPinName,
            InternalPin = child.PhysicalPins[0],
            RelativeX = child.PhysicalPins[0].OffsetXMicrometers,
            RelativeY = child.PhysicalPins[0].OffsetYMicrometers,
            AngleDegrees = child.PhysicalPins[0].AngleDegrees
        };
        group.AddExternalPin(groupPin);
        return group;
    }

    // ── bug-reproduction tests ────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates the original bug: FindIndex by ParentComponent returns -1 for a group
    /// connection because the internal component is not on the canvas.
    /// </summary>
    [Fact]
    public void OldSaveLogic_GroupConnection_ReturnsNegativeIndex()
    {
        // Arrange
        var comp1 = CreateComponent("comp1", "pin_out");
        var groupA = CreateGroupWithExternalPin(comp1, "A_ext");
        var components = new List<ComponentViewModel>
        {
            new ComponentViewModel(groupA)
        };

        // The InternalPin's ParentComponent is comp1, not the group
        var internalPin = groupA.ExternalPins[0].InternalPin;

        // Act – old (buggy) logic
        int oldIndex = components.FindIndex(c => c.Component == internalPin.ParentComponent);

        // Assert – confirms the bug: -1 means the connection would be silently dropped
        oldIndex.ShouldBe(-1);
    }

    // ── save-side fix tests ───────────────────────────────────────────────────

    /// <summary>
    /// ResolveConnectionEndpoint finds the correct group index and external pin name
    /// when the pin is an InternalPin of a group's external pin.
    /// </summary>
    [Fact]
    public void ResolveConnectionEndpoint_GroupPin_ReturnsGroupIndexAndExternalPinName()
    {
        // Arrange
        var comp1 = CreateComponent("comp1", "pin_out");
        var groupA = CreateGroupWithExternalPin(comp1, "A_ext");
        var components = new List<ComponentViewModel>
        {
            new ComponentViewModel(groupA)
        };
        var internalPin = groupA.ExternalPins[0].InternalPin;

        // Act
        var (index, pinName) = FileOperationsViewModel.ResolveConnectionEndpoint(components, internalPin);

        // Assert
        index.ShouldBe(0);
        pinName.ShouldBe("A_ext");
    }

    /// <summary>
    /// ResolveConnectionEndpoint finds the correct component and pin name
    /// for a regular (non-group) component.
    /// </summary>
    [Fact]
    public void ResolveConnectionEndpoint_RegularComponent_ReturnsDirectIndexAndPinName()
    {
        // Arrange
        var comp = CreateComponent("comp1", "pin_out");
        var vm = new ComponentViewModel(comp);
        var components = new List<ComponentViewModel> { vm };

        // Act
        var (index, pinName) = FileOperationsViewModel.ResolveConnectionEndpoint(
            components, comp.PhysicalPins[0]);

        // Assert
        index.ShouldBe(0);
        pinName.ShouldBe("pin_out");
    }

    /// <summary>
    /// ResolveConnectionEndpoint returns -1 when the pin belongs to neither a direct
    /// component nor any group's external pins.
    /// </summary>
    [Fact]
    public void ResolveConnectionEndpoint_UnknownPin_ReturnsNegativeIndex()
    {
        // Arrange
        var comp = CreateComponent("comp1", "pin_out");
        var orphanPin = new PhysicalPin { Name = "orphan", ParentComponent = comp };
        var components = new List<ComponentViewModel>();

        // Act
        var (index, _) = FileOperationsViewModel.ResolveConnectionEndpoint(components, orphanPin);

        // Assert
        index.ShouldBe(-1);
    }

    // ── load-side fix tests ───────────────────────────────────────────────────

    /// <summary>
    /// ResolvePin finds the InternalPin for a group when given the external pin name.
    /// </summary>
    [Fact]
    public void ResolvePin_GroupWithExternalPinName_ReturnsInternalPin()
    {
        // Arrange
        var comp = CreateComponent("comp1", "pin_out");
        var group = CreateGroupWithExternalPin(comp, "A_ext");
        var expectedPin = group.ExternalPins[0].InternalPin;

        // Act
        var resolved = FileOperationsViewModel.ResolvePin(group, "A_ext");

        // Assert
        resolved.ShouldNotBeNull();
        resolved.ShouldBe(expectedPin);
    }

    /// <summary>
    /// ResolvePin finds the physical pin for a regular component by pin name.
    /// </summary>
    [Fact]
    public void ResolvePin_RegularComponent_ReturnsPinByName()
    {
        // Arrange
        var comp = CreateComponent("comp1", "pin_out");

        // Act
        var resolved = FileOperationsViewModel.ResolvePin(comp, "pin_out");

        // Assert
        resolved.ShouldNotBeNull();
        resolved.ShouldBe(comp.PhysicalPins[0]);
    }

    /// <summary>
    /// ResolvePin returns null for a group when the external pin name does not exist.
    /// </summary>
    [Fact]
    public void ResolvePin_GroupUnknownPinName_ReturnsNull()
    {
        // Arrange
        var comp = CreateComponent("comp1", "pin_out");
        var group = CreateGroupWithExternalPin(comp, "A_ext");

        // Act
        var resolved = FileOperationsViewModel.ResolvePin(group, "nonexistent");

        // Assert
        resolved.ShouldBeNull();
    }

    // ── full round-trip test ──────────────────────────────────────────────────

    /// <summary>
    /// Full round-trip test: a connection between two groups' external pins is correctly
    /// serialized and deserialized via ConnectionData.
    /// This is the primary regression test for issue #276.
    /// </summary>
    [Fact]
    public void GroupToGroupConnection_RoundTrip_ConnectionIsPreserved()
    {
        // Arrange: two groups, each with one external pin
        var comp1 = CreateComponent("comp1", "pin_out", x: 0);
        var comp2 = CreateComponent("comp2", "pin_in", x: 50);
        var groupA = CreateGroupWithExternalPin(comp1, "A_ext");
        var groupB = CreateGroupWithExternalPin(comp2, "B_ext");

        var components = new List<ComponentViewModel>
        {
            new ComponentViewModel(groupA),
            new ComponentViewModel(groupB)
        };

        // The connection uses InternalPins as the UI does
        var internalPinA = groupA.ExternalPins[0].InternalPin;
        var internalPinB = groupB.ExternalPins[0].InternalPin;

        // ── Save (serialize) ──
        var (startIdx, startPinName) = FileOperationsViewModel.ResolveConnectionEndpoint(
            components, internalPinA);
        var (endIdx, endPinName) = FileOperationsViewModel.ResolveConnectionEndpoint(
            components, internalPinB);

        startIdx.ShouldBe(0, "Group A should be found at index 0");
        startPinName.ShouldBe("A_ext");
        endIdx.ShouldBe(1, "Group B should be found at index 1");
        endPinName.ShouldBe("B_ext");

        // ── Load (deserialize) ──
        var resolvedStartPin = FileOperationsViewModel.ResolvePin(
            components[startIdx].Component, startPinName);
        var resolvedEndPin = FileOperationsViewModel.ResolvePin(
            components[endIdx].Component, endPinName);

        resolvedStartPin.ShouldNotBeNull("Start pin should be resolved after load");
        resolvedEndPin.ShouldNotBeNull("End pin should be resolved after load");
        resolvedStartPin.ShouldBe(internalPinA, "Resolved start pin should be the same InternalPin");
        resolvedEndPin.ShouldBe(internalPinB, "Resolved end pin should be the same InternalPin");
    }

    /// <summary>
    /// Verifies that a connection between a group's external pin and a regular component
    /// is also correctly handled by the round-trip logic.
    /// </summary>
    [Fact]
    public void GroupToComponentConnection_RoundTrip_ConnectionIsPreserved()
    {
        // Arrange
        var comp1 = CreateComponent("comp1", "pin_out");
        var comp2 = CreateComponent("comp2", "pin_in", x: 50);
        var groupA = CreateGroupWithExternalPin(comp1, "A_ext");

        var components = new List<ComponentViewModel>
        {
            new ComponentViewModel(groupA),
            new ComponentViewModel(comp2)
        };

        var internalPinA = groupA.ExternalPins[0].InternalPin;
        var pinComp2 = comp2.PhysicalPins[0];

        // ── Save ──
        var (startIdx, startPinName) = FileOperationsViewModel.ResolveConnectionEndpoint(
            components, internalPinA);
        var (endIdx, endPinName) = FileOperationsViewModel.ResolveConnectionEndpoint(
            components, pinComp2);

        startIdx.ShouldBe(0);
        startPinName.ShouldBe("A_ext");
        endIdx.ShouldBe(1);
        endPinName.ShouldBe("pin_in");

        // ── Load ──
        var resolvedStart = FileOperationsViewModel.ResolvePin(
            components[startIdx].Component, startPinName);
        var resolvedEnd = FileOperationsViewModel.ResolvePin(
            components[endIdx].Component, endPinName);

        resolvedStart.ShouldNotBeNull();
        resolvedEnd.ShouldNotBeNull();
        resolvedStart.ShouldBe(internalPinA);
        resolvedEnd.ShouldBe(pinComp2);
    }
}
