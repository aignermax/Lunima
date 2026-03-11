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
        var draggingVm = CreateComponentViewModel(x: 0, y: 0, pinX: 10, pinY: 100.0, pinAngle: 0);
        var otherVm = CreateComponentViewModel(x: 50, y: 50, pinX: 10, pinY: 51.5, pinAngle: 180);
        // Pins at (10, 100) and (60, 101.5) - 1.5µm apart on Y - pointing at each other

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
        var draggingVm = CreateComponentViewModel(x: 0, y: 0, pinX: 10, pinY: 100, pinAngle: 0);
        var comp1 = CreateComponentViewModel(x: 100, y: 0, pinX: 10, pinY: 100, pinAngle: 180);
        var comp2 = CreateComponentViewModel(x: 200, y: 0, pinX: 10, pinY: 100, pinAngle: 180);

        // Act
        viewModel.UpdateAlignments(draggingVm, new[] { comp1, comp2 });

        // Assert
        viewModel.HasAlignments.ShouldBeTrue();
        viewModel.HorizontalAlignments.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void CalculateComponentPosition_WithNoSnap_ReturnsMouseRelativePosition()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel();
        var draggingVm = CreateComponentViewModel(x: 100, y: 100, pinX: 10, pinY: 10);
        var otherVm = CreateComponentViewModel(x: 500, y: 500, pinX: 10, pinY: 10); // Far away, no alignment

        double mouseX = 150;
        double mouseY = 150;
        double offsetX = 100 - 150; // -50
        double offsetY = 100 - 150; // -50
        double zoom = 1.0;

        // Act
        var (targetX, targetY) = viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, mouseX, mouseY, offsetX, offsetY, zoom);

        // Assert
        targetX.ShouldBe(100.0); // mouseX (150) + offsetX (-50) = 100
        targetY.ShouldBe(100.0); // mouseY (150) + offsetY (-50) = 100
    }

    [Fact]
    public void CalculateComponentPosition_WhenSnapped_MaintainsSnapUntilBreak()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel
        {
            SnapToleranceMicrometers = 20.0,
            SnapBreakDistancePixels = 10.0
        };

        var draggingVm = CreateComponentViewModel(x: 0, y: 15, pinX: 10, pinY: 100, pinAngle: 0);
        var otherVm = CreateComponentViewModel(x: 100, y: 15, pinX: 10, pinY: 100, pinAngle: 180);
        // Pins aligned at Y = 115

        double offsetX = 0;
        double offsetY = 15;
        double zoom = 2.0; // 2x zoom

        // First call - engage snap
        var (targetX1, targetY1) = viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, 0, 0, offsetX, offsetY, zoom);

        // Second call - move mouse slightly on free axis (X) - should maintain snap
        var (targetX2, targetY2) = viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, 5, 0, offsetX, offsetY, zoom);

        // Third call - move mouse on free axis within break tolerance - should still maintain snap
        var (targetX3, targetY3) = viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, 4, 0, offsetX, offsetY, zoom);

        // Assert - Y should remain snapped in all cases
        targetY1.ShouldBe(15.0); // Snapped
        targetY2.ShouldBe(15.0); // Still snapped (X moved but Y locked)
        targetX2.ShouldBe(5.0); // X follows mouse
        targetY3.ShouldBe(15.0); // Still snapped
        targetX3.ShouldBe(4.0); // X follows mouse
    }

    [Fact]
    public void CalculateComponentPosition_SnapBreak_RestoresToMouseRelativePosition()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel
        {
            SnapToleranceMicrometers = 20.0,
            SnapBreakDistancePixels = 10.0
        };

        var draggingVm = CreateComponentViewModel(x: 0, y: 15, pinX: 10, pinY: 100, pinAngle: 0);
        var otherVm = CreateComponentViewModel(x: 100, y: 15, pinX: 10, pinY: 100, pinAngle: 180);

        double offsetX = 0;
        double offsetY = 15;
        double zoom = 1.0;

        // First call - engage snap (Y snapped)
        viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, 0, 0, offsetX, offsetY, zoom);

        // Second call - move mouse beyond break distance on free axis (X)
        // With zoom=1.0, 10 pixels = 10 canvas units
        // Moving from X=0 to X=15 is 15 pixels > 10 pixel break threshold
        var (targetX, targetY) = viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, 15, 0, offsetX, offsetY, zoom);

        // Assert - snap should break and return to mouse-relative position
        targetX.ShouldBe(15.0); // mouseX (15) + offsetX (0)
        targetY.ShouldBe(15.0); // mouseY (0) + offsetY (15) - snap is broken, back to mouse-relative
    }

    [Fact]
    public void CalculateComponentPosition_SnapBreak_IsZoomIndependent()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel
        {
            SnapToleranceMicrometers = 20.0,
            SnapBreakDistancePixels = 10.0
        };

        var draggingVm = CreateComponentViewModel(x: 0, y: 15, pinX: 10, pinY: 100, pinAngle: 0);
        var otherVm = CreateComponentViewModel(x: 100, y: 15, pinX: 10, pinY: 100, pinAngle: 180);

        double offsetX = 0;
        double offsetY = 15;

        // Test at 1x zoom - engage snap at mouse (0,0), then move to (15, 0)
        viewModel.ResetSnapState();
        viewModel.CalculateComponentPosition(draggingVm, new[] { otherVm }, 0, 0, offsetX, offsetY, 1.0);
        var (x1, y1) = viewModel.CalculateComponentPosition(draggingVm, new[] { otherVm }, 15, 0, offsetX, offsetY, 1.0);
        // At 1x zoom: moved 15 canvas units on X = 15 screen pixels > 10 pixel threshold
        // Snap should break, Y returns to mouse-relative: mouseY (0) + offsetY (15) = 15
        bool snapBrokenAt1x = (y1 == 15.0);

        // Test at 2x zoom - engage snap at (0,0), then move to (15, 0)
        viewModel.ResetSnapState();
        viewModel.CalculateComponentPosition(draggingVm, new[] { otherVm }, 0, 0, offsetX, offsetY, 2.0);
        var (x2, y2) = viewModel.CalculateComponentPosition(draggingVm, new[] { otherVm }, 15, 0, offsetX, offsetY, 2.0);
        // At 2x zoom: moved 15 canvas units on X = 30 screen pixels > 10 pixel threshold
        // Snap should break
        bool snapBrokenAt2x = (y2 == 15.0);

        // Test at 0.5x zoom - engage snap at (0,0), then move to (8, 0)
        viewModel.ResetSnapState();
        viewModel.CalculateComponentPosition(draggingVm, new[] { otherVm }, 0, 0, offsetX, offsetY, 0.5);
        var (x3, y3) = viewModel.CalculateComponentPosition(draggingVm, new[] { otherVm }, 8, 0, offsetX, offsetY, 0.5);
        // At 0.5x zoom: moved 8 canvas units on X = 4 screen pixels < 10 pixel threshold
        // Snap should NOT break, Y stays locked at 15
        bool snapMaintainedAt05x = (y3 == 15.0);

        // Assert
        snapBrokenAt1x.ShouldBeTrue("Snap should break at 1x zoom with 15 canvas units (15 pixels)");
        snapBrokenAt2x.ShouldBeTrue("Snap should break at 2x zoom with 15 canvas units (30 pixels)");
        snapMaintainedAt05x.ShouldBeTrue("Snap should be maintained at 0.5x zoom with 8 canvas units (4 pixels)");
    }

    [Fact]
    public void CalculateComponentPosition_WithSnapDisabled_NeverSnaps()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel
        {
            SnapEnabled = false, // Disable snapping
            SnapToleranceMicrometers = 20.0
        };

        var draggingVm = CreateComponentViewModel(x: 0, y: 0, pinX: 10, pinY: 100, pinAngle: 0);
        var otherVm = CreateComponentViewModel(x: 100, y: 15, pinX: 10, pinY: 100, pinAngle: 180);

        double mouseX = 0;
        double mouseY = 0;
        double offsetX = 0;
        double offsetY = 0;
        double zoom = 1.0;

        // Act
        var (targetX, targetY) = viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, mouseX, mouseY, offsetX, offsetY, zoom);

        // Assert - should return mouse-relative position without snapping
        targetX.ShouldBe(0.0);
        targetY.ShouldBe(0.0); // No snap applied even though pins are close
    }

    [Fact]
    public void ResetSnapState_ClearsAllSnapState()
    {
        // Arrange
        var viewModel = new AlignmentGuideViewModel
        {
            SnapToleranceMicrometers = 20.0
        };

        var draggingVm = CreateComponentViewModel(x: 0, y: 0, pinX: 10, pinY: 100, pinAngle: 0);
        var otherVm = CreateComponentViewModel(x: 100, y: 15, pinX: 10, pinY: 100, pinAngle: 180);

        // Engage snap
        viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, 0, 0, 0, 0, 1.0);

        // Act - Reset snap state
        viewModel.ResetSnapState();

        // Calculate position again - should act as if no previous snap existed
        var (targetX, targetY) = viewModel.CalculateComponentPosition(
            draggingVm, new[] { otherVm }, 50, 50, 0, 0, 1.0);

        // Assert - should snap again from fresh state (not maintain previous snap)
        // Since pins would be close at this new position, it should calculate a new snap
        targetX.ShouldBe(50.0);
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
        // Create two components with pins aligned on Y axis (Y = 100) that point at each other
        // dragging at (0,0) with pin at (10, 100) pointing right (0°)
        // other at (100,0) with pin at (50, 100) - absolute position (150, 100) pointing left (180°)
        var dragging = CreateComponentViewModel(x: 0, y: 0, pinX: 10, pinY: 100, pinAngle: 0);
        var other = CreateComponentViewModel(x: 100, y: 0, pinX: 50, pinY: 100, pinAngle: 180);
        return (dragging, other);
    }

    #endregion
}
