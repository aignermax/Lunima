using CAP.Avalonia.ViewModels;
using CAP_Core.Components;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for AlignmentGuideViewModel - validates ViewModel + Core integration.
/// </summary>
public class AlignmentGuideViewModelTests
{
    [Fact]
    public void UpdateAlignments_WithAlignedComponents_DetectsAlignments()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        var (draggingVm, otherVm) = CreateTwoAlignedComponentViewModels();

        // Act
        viewModel.UpdateAlignments(draggingVm, new[] { otherVm });

        // Assert
        viewModel.HasAlignments.ShouldBeTrue();
        viewModel.HorizontalAlignments.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void UpdateAlignments_WithNonAlignedComponents_NoAlignments()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        var draggingVm = CreateComponentViewModel(x: 0, y: 0, pinX: 10, pinY: 10);
        var otherVm = CreateComponentViewModel(x: 100, y: 100, pinX: 20, pinY: 30);

        // Act
        viewModel.UpdateAlignments(draggingVm, new[] { otherVm });

        // Assert
        viewModel.HasAlignments.ShouldBeFalse();
        viewModel.HorizontalAlignments.ShouldBeEmpty();
        viewModel.VerticalAlignments.ShouldBeEmpty();
    }

    [Fact]
    public void ClearAlignments_RemovesAllAlignments()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        var (draggingVm, otherVm) = CreateTwoAlignedComponentViewModels();
        viewModel.UpdateAlignments(draggingVm, new[] { otherVm });
        viewModel.HasAlignments.ShouldBeTrue(); // Precondition

        // Act
        viewModel.ClearAlignments();

        // Assert
        viewModel.HasAlignments.ShouldBeFalse();
        viewModel.HorizontalAlignments.ShouldBeEmpty();
        viewModel.VerticalAlignments.ShouldBeEmpty();
    }

    [Fact]
    public void Toggle_ChangesEnabledState()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        var initialState = viewModel.IsEnabled;

        // Act
        viewModel.Toggle();

        // Assert
        viewModel.IsEnabled.ShouldBe(!initialState);
    }

    [Fact]
    public void Toggle_WhenDisabled_ClearsAlignments()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel { IsEnabled = true };
        var (draggingVm, otherVm) = CreateTwoAlignedComponentViewModels();
        viewModel.UpdateAlignments(draggingVm, new[] { otherVm });
        viewModel.HasAlignments.ShouldBeTrue(); // Precondition

        // Act
        viewModel.Toggle(); // Disable

        // Assert
        viewModel.IsEnabled.ShouldBeFalse();
        viewModel.HasAlignments.ShouldBeFalse();
    }

    [Fact]
    public void UpdateAlignments_WhenDisabled_DoesNotDetectAlignments()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel { IsEnabled = false };
        var (draggingVm, otherVm) = CreateTwoAlignedComponentViewModels();

        // Act
        viewModel.UpdateAlignments(draggingVm, new[] { otherVm });

        // Assert
        viewModel.HasAlignments.ShouldBeFalse();
    }

    [Fact]
    public void UpdateAlignments_WithNullDraggingComponent_ClearsAlignments()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        var otherVm = CreateComponentViewModel(x: 100, y: 100, pinX: 10, pinY: 10);

        // Act
        viewModel.UpdateAlignments(null, new[] { otherVm });

        // Assert
        viewModel.HasAlignments.ShouldBeFalse();
    }

    [Fact]
    public void AlignmentToleranceMicrometers_UpdatesHelperTolerance()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        // Dragging component with pin pointing right at (50, 100.0)
        var draggingVm = CreateComponentViewModel(x: 0, y: 0, pinX: 50, pinY: 100.0, pinAngle: 0);
        // Other component with pin pointing left at (100, 101.5) - 1.5µm apart on Y
        var otherVm = CreateComponentViewModel(x: 100, y: 50, pinX: 0, pinY: 51.5, pinAngle: 180);

        // Act - Set tolerance to 2.0µm (should detect)
        viewModel.AlignmentToleranceMicrometers = 2.0;
        viewModel.UpdateAlignments(draggingVm, new[] { otherVm });
        var detectedWith2um = viewModel.HasAlignments;

        // Act - Set tolerance to 1.0µm (should not detect)
        viewModel.ClearAlignments();
        viewModel.AlignmentToleranceMicrometers = 1.0;
        viewModel.UpdateAlignments(draggingVm, new[] { otherVm });
        var detectedWith1um = viewModel.HasAlignments;

        // Assert
        detectedWith2um.ShouldBeTrue();
        detectedWith1um.ShouldBeFalse();
    }

    [Fact]
    public void UpdateAlignments_ExcludesDraggingComponentFromOthers()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        var draggingVm = CreateComponentViewModel(x: 0, y: 0, pinX: 10, pinY: 100);

        // Act - Include dragging component in "others" list (should be ignored)
        viewModel.UpdateAlignments(draggingVm, new[] { draggingVm });

        // Assert
        viewModel.HasAlignments.ShouldBeFalse();
    }

    [Fact]
    public void UpdateAlignments_WithMultipleAlignedComponents_DetectsAll()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        // Dragging component with pin pointing right at (50, 100)
        var draggingVm = CreateComponentViewModel(x: 0, y: 0, pinX: 50, pinY: 100, pinAngle: 0);
        // Two other components with pins pointing left, aligned on Y=100
        var comp1 = CreateComponentViewModel(x: 100, y: 0, pinX: 0, pinY: 100, pinAngle: 180);
        var comp2 = CreateComponentViewModel(x: 200, y: 0, pinX: 0, pinY: 100, pinAngle: 180);

        // Act
        viewModel.UpdateAlignments(draggingVm, new[] { comp1, comp2 });

        // Assert
        viewModel.HasAlignments.ShouldBeTrue();
        viewModel.HorizontalAlignments.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    #region Test Helpers

    private ComponentViewModel CreateComponentViewModel(double x, double y, double pinX, double pinY, double pinAngle = 0)
    {
        var pin = new PhysicalPin
        {
            Name = "TestPin",
            OffsetXMicrometers = pinX,
            OffsetYMicrometers = pinY,
            AngleDegrees = pinAngle
        };

        var component = UnitTests.TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.PhysicalPins.Clear();
        component.PhysicalPins.Add(pin);
        pin.ParentComponent = component;

        return new ComponentViewModel(component, "Test Component");
    }

    private (ComponentViewModel dragging, ComponentViewModel other) CreateTwoAlignedComponentViewModels()
    {
        // Create two components with opposing pins aligned on Y axis (Y = 100)
        // Dragging component at (0, 0) with pin pointing right (0°) at offset (50, 100) -> absolute position (50, 100)
        // Other component at (100, 0) with pin pointing left (180°) at offset (0, 100) -> absolute position (100, 100)
        // Pins are aligned on Y=100 and point at each other (50µm apart, both on same horizontal line)
        var dragging = CreateComponentViewModel(x: 0, y: 0, pinX: 50, pinY: 100, pinAngle: 0);
        var other = CreateComponentViewModel(x: 100, y: 0, pinX: 0, pinY: 100, pinAngle: 180);
        return (dragging, other);
    }

    #endregion
}
