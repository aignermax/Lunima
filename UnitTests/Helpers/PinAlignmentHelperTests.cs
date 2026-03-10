using CAP_Core.Components;
using CAP_Core.Helpers;
using Shouldly;
using Xunit;

namespace UnitTests.Helpers;

/// <summary>
/// Unit tests for PinAlignmentHelper - validates pin alignment detection logic.
/// </summary>
public class PinAlignmentHelperTests
{
    [Fact]
    public void FindHorizontalAlignments_WithAlignedPins_ReturnsAlignment()
    {
        // Arrange
        var helper = new PinAlignmentHelper();
        var (draggingComp, otherComp) = CreateTwoComponentsWithAlignedY(100.0);

        // Act
        var alignments = helper.FindHorizontalAlignments(draggingComp, new[] { otherComp });

        // Assert
        alignments.Count.ShouldBe(1);
        alignments[0].YCoordinate.ShouldBe(100.0, 0.01);
        alignments[0].DraggingPin.ShouldBe(draggingComp.PhysicalPins[0]);
        alignments[0].AlignedPin.ShouldBe(otherComp.PhysicalPins[0]);
    }

    [Fact]
    public void FindVerticalAlignments_WithAlignedPins_ReturnsAlignment()
    {
        // Arrange
        var helper = new PinAlignmentHelper();
        var (draggingComp, otherComp) = CreateTwoComponentsWithAlignedX(200.0);

        // Act
        var alignments = helper.FindVerticalAlignments(draggingComp, new[] { otherComp });

        // Assert
        alignments.Count.ShouldBe(1);
        alignments[0].XCoordinate.ShouldBe(200.0, 0.01);
        alignments[0].DraggingPin.ShouldBe(draggingComp.PhysicalPins[0]);
        alignments[0].AlignedPin.ShouldBe(otherComp.PhysicalPins[0]);
    }

    [Fact]
    public void FindHorizontalAlignments_WithNoAlignment_ReturnsEmpty()
    {
        // Arrange
        var helper = new PinAlignmentHelper();
        var draggingComp = CreateComponent(x: 0, y: 0, pinOffsetX: 10, pinOffsetY: 10);
        var otherComp = CreateComponent(x: 100, y: 100, pinOffsetX: 10, pinOffsetY: 20);

        // Act
        var alignments = helper.FindHorizontalAlignments(draggingComp, new[] { otherComp });

        // Assert
        alignments.ShouldBeEmpty();
    }

    [Fact]
    public void FindVerticalAlignments_WithNoAlignment_ReturnsEmpty()
    {
        // Arrange
        var helper = new PinAlignmentHelper();
        var draggingComp = CreateComponent(x: 0, y: 0, pinOffsetX: 10, pinOffsetY: 10);
        var otherComp = CreateComponent(x: 100, y: 100, pinOffsetX: 20, pinOffsetY: 10);

        // Act
        var alignments = helper.FindVerticalAlignments(draggingComp, new[] { otherComp });

        // Assert
        alignments.ShouldBeEmpty();
    }

    [Fact]
    public void FindAllAlignments_WithBothAlignments_ReturnsBoth()
    {
        // Arrange
        var helper = new PinAlignmentHelper();
        var draggingComp = CreateComponent(x: 0, y: 0, pinOffsetX: 100, pinOffsetY: 200);
        var otherComp = CreateComponent(x: 200, y: 100, pinOffsetX: -100, pinOffsetY: 100);
        // Pins are at: dragging=(100, 200), other=(100, 200) - perfect alignment

        // Act
        var (horizontal, vertical) = helper.FindAllAlignments(draggingComp, new[] { otherComp });

        // Assert
        horizontal.Count.ShouldBe(1);
        vertical.Count.ShouldBe(1);
        horizontal[0].YCoordinate.ShouldBe(200.0, 0.01);
        vertical[0].XCoordinate.ShouldBe(100.0, 0.01);
    }

    [Fact]
    public void FindHorizontalAlignments_WithinTolerance_ReturnsAlignment()
    {
        // Arrange
        var helper = new PinAlignmentHelper { AlignmentToleranceMicrometers = 2.0 };
        var draggingComp = CreateComponent(x: 0, y: 0, pinOffsetX: 10, pinOffsetY: 100.0);
        var otherComp = CreateComponent(x: 50, y: 50, pinOffsetX: 10, pinOffsetY: 51.5);
        // Pins are at: (10, 100) and (60, 101.5) - within 2µm tolerance on Y

        // Act
        var alignments = helper.FindHorizontalAlignments(draggingComp, new[] { otherComp });

        // Assert
        alignments.Count.ShouldBe(1);
    }

    [Fact]
    public void FindHorizontalAlignments_OutsideTolerance_ReturnsEmpty()
    {
        // Arrange
        var helper = new PinAlignmentHelper { AlignmentToleranceMicrometers = 1.0 };
        var draggingComp = CreateComponent(x: 0, y: 0, pinOffsetX: 10, pinOffsetY: 100.0);
        var otherComp = CreateComponent(x: 50, y: 50, pinOffsetX: 10, pinOffsetY: 52.5);
        // Pins are at: (10, 100) and (60, 102.5) - outside 1µm tolerance on Y

        // Act
        var alignments = helper.FindHorizontalAlignments(draggingComp, new[] { otherComp });

        // Assert
        alignments.ShouldBeEmpty();
    }

    [Fact]
    public void FindHorizontalAlignments_MultipleComponents_ReturnsAllAlignments()
    {
        // Arrange
        var helper = new PinAlignmentHelper();
        var draggingComp = CreateComponent(x: 0, y: 0, pinOffsetX: 10, pinOffsetY: 100);
        var comp1 = CreateComponent(x: 100, y: 0, pinOffsetX: 10, pinOffsetY: 100);
        var comp2 = CreateComponent(x: 200, y: 0, pinOffsetX: 10, pinOffsetY: 100);
        var comp3 = CreateComponent(x: 300, y: 50, pinOffsetX: 10, pinOffsetY: 60); // Not aligned

        // Act
        var alignments = helper.FindHorizontalAlignments(draggingComp, new[] { comp1, comp2, comp3 });

        // Assert
        alignments.Count.ShouldBe(2); // Aligned with comp1 and comp2, not comp3
    }

    [Fact]
    public void FindHorizontalAlignments_ExcludesDraggingComponent()
    {
        // Arrange
        var helper = new PinAlignmentHelper();
        var draggingComp = CreateComponent(x: 0, y: 0, pinOffsetX: 10, pinOffsetY: 100);

        // Act - pass dragging component in "other" list (should be ignored)
        var alignments = helper.FindHorizontalAlignments(draggingComp, new[] { draggingComp });

        // Assert
        alignments.ShouldBeEmpty();
    }

    [Fact]
    public void FindAllAlignments_WithMultiplePins_DetectsAllAlignments()
    {
        // Arrange
        var helper = new PinAlignmentHelper();
        var draggingComp = CreateComponentWithTwoPins(
            x: 0, y: 0,
            pin1X: 10, pin1Y: 100,
            pin2X: 200, pin2Y: 150);
        var otherComp = CreateComponentWithTwoPins(
            x: 100, y: 100,
            pin1X: 10, pin1Y: 0,   // Aligns with dragging pin1 on X
            pin2X: 100, pin2Y: 50); // Aligns with dragging pin2 on Y

        // Act
        var (horizontal, vertical) = helper.FindAllAlignments(draggingComp, new[] { otherComp });

        // Assert
        horizontal.Count.ShouldBeGreaterThanOrEqualTo(1);
        vertical.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void FindHorizontalAlignments_WithNullComponent_ThrowsArgumentNullException()
    {
        // Arrange
        var helper = new PinAlignmentHelper();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() =>
            helper.FindHorizontalAlignments(null!, Array.Empty<Component>()));
    }

    [Fact]
    public void FindVerticalAlignments_WithNullComponent_ThrowsArgumentNullException()
    {
        // Arrange
        var helper = new PinAlignmentHelper();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() =>
            helper.FindVerticalAlignments(null!, Array.Empty<Component>()));
    }

    #region Test Helpers

    private Component CreateComponent(double x, double y, double pinOffsetX, double pinOffsetY)
    {
        var pin = new PhysicalPin
        {
            Name = "TestPin",
            OffsetXMicrometers = pinOffsetX,
            OffsetYMicrometers = pinOffsetY,
            AngleDegrees = 0
        };

        var component = UnitTests.TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.PhysicalPins.Clear();
        component.PhysicalPins.Add(pin);
        pin.ParentComponent = component;

        return component;
    }

    private Component CreateComponentWithTwoPins(
        double x, double y,
        double pin1X, double pin1Y,
        double pin2X, double pin2Y)
    {
        var pin1 = new PhysicalPin
        {
            Name = "Pin1",
            OffsetXMicrometers = pin1X,
            OffsetYMicrometers = pin1Y,
            AngleDegrees = 0
        };

        var pin2 = new PhysicalPin
        {
            Name = "Pin2",
            OffsetXMicrometers = pin2X,
            OffsetYMicrometers = pin2Y,
            AngleDegrees = 180
        };

        var component = UnitTests.TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.PhysicalPins.Clear();
        component.PhysicalPins.Add(pin1);
        component.PhysicalPins.Add(pin2);
        pin1.ParentComponent = component;
        pin2.ParentComponent = component;

        return component;
    }

    private (Component dragging, Component other) CreateTwoComponentsWithAlignedY(double yCoord)
    {
        var dragging = CreateComponent(x: 0, y: 0, pinOffsetX: 10, pinOffsetY: yCoord);
        var other = CreateComponent(x: 100, y: 0, pinOffsetX: 50, pinOffsetY: yCoord);
        return (dragging, other);
    }

    private (Component dragging, Component other) CreateTwoComponentsWithAlignedX(double xCoord)
    {
        var dragging = CreateComponent(x: 0, y: 0, pinOffsetX: xCoord, pinOffsetY: 10);
        var other = CreateComponent(x: 0, y: 100, pinOffsetX: xCoord, pinOffsetY: 50);
        return (dragging, other);
    }

    #endregion
}
