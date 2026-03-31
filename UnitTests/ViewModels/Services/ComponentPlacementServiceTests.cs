using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels.Services;

/// <summary>
/// Unit tests for ComponentPlacementService - collision detection, placement, and movement.
/// </summary>
public class ComponentPlacementServiceTests
{
    private readonly ObservableCollection<ComponentViewModel> _components = new();
    private readonly ObservableCollection<WaveguideConnectionViewModel> _connections = new();
    private readonly ComponentPlacementService _service;

    public ComponentPlacementServiceTests()
    {
        _service = new ComponentPlacementService(_components, _connections);
    }

    [Fact]
    public void CanPlaceComponent_WithinBounds_ReturnsTrue()
    {
        var result = _service.CanPlaceComponent(100, 100, 50, 50);
        result.ShouldBeTrue();
    }

    [Fact]
    public void CanPlaceComponent_OutsideBounds_ReturnsFalse()
    {
        _service.CanPlaceComponent(-10, 0, 50, 50).ShouldBeFalse();
        _service.CanPlaceComponent(0, -10, 50, 50).ShouldBeFalse();
        _service.CanPlaceComponent(4980, 0, 50, 50).ShouldBeFalse();
        _service.CanPlaceComponent(0, 4980, 50, 50).ShouldBeFalse();
    }

    [Fact]
    public void CanPlaceComponent_OverlappingExisting_ReturnsFalse()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.WidthMicrometers = 50;
        comp.HeightMicrometers = 50;
        _components.Add(new ComponentViewModel(comp));

        // Overlapping position (within gap tolerance)
        _service.CanPlaceComponent(120, 120, 50, 50).ShouldBeFalse();
    }

    [Fact]
    public void CanPlaceComponent_ExcludesSelf_ReturnsTrue()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.WidthMicrometers = 50;
        comp.HeightMicrometers = 50;
        var vm = new ComponentViewModel(comp);
        _components.Add(vm);

        _service.CanPlaceComponent(100, 100, 50, 50, excludeComponent: vm).ShouldBeTrue();
    }

    [Fact]
    public void FindValidPlacement_ExactPositionFree_ReturnsExact()
    {
        var result = _service.FindValidPlacement(100, 100, 50, 50);
        result.ShouldNotBeNull();
        result.Value.x.ShouldBe(100);
        result.Value.y.ShouldBe(100);
    }

    [Fact]
    public void FindValidPlacement_ExactPositionBlocked_FindsAlternative()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.WidthMicrometers = 50;
        comp.HeightMicrometers = 50;
        _components.Add(new ComponentViewModel(comp));

        var result = _service.FindValidPlacement(100, 100, 50, 50);
        result.ShouldNotBeNull();
        // Should find a position that doesn't overlap
        _service.CanPlaceComponent(result.Value.x, result.Value.y, 50, 50).ShouldBeTrue();
    }

    [Fact]
    public void ChipBoundaries_AreConfigurable()
    {
        _service.ChipMinX = 0;
        _service.ChipMinY = 0;
        _service.ChipMaxX = 100;
        _service.ChipMaxY = 100;

        _service.CanPlaceComponent(0, 0, 50, 50).ShouldBeTrue();
        _service.CanPlaceComponent(60, 60, 50, 50).ShouldBeFalse();
    }

    [Fact]
    public void IsDragging_BypassesCollisionCheck_InMoveComponent()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.WidthMicrometers = 50;
        comp.HeightMicrometers = 50;
        var vm = new ComponentViewModel(comp);
        _components.Add(vm);

        var blocker = TestComponentFactory.CreateStraightWaveGuide();
        blocker.PhysicalX = 160;
        blocker.PhysicalY = 100;
        blocker.WidthMicrometers = 50;
        blocker.HeightMicrometers = 50;
        _components.Add(new ComponentViewModel(blocker));

        // Without dragging, can't move into overlapping position
        _service.IsDragging = false;
        var result = _service.MoveComponent(vm, 50, 0, false, null, null, null);
        result.ShouldBeFalse();

        // With dragging, movement is allowed (collision checked on drop)
        _service.IsDragging = true;
        result = _service.MoveComponent(vm, 50, 0, false, null, null, null);
        result.ShouldBeTrue();
    }

    [Fact]
    public void MoveComponent_UpdatesPhysicalPosition()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.WidthMicrometers = 50;
        comp.HeightMicrometers = 50;
        var vm = new ComponentViewModel(comp);
        _components.Add(vm);

        _service.MoveComponent(vm, 50, 30, false, null, null, null);

        vm.X.ShouldBe(150);
        vm.Y.ShouldBe(130);
        comp.PhysicalX.ShouldBe(150);
        comp.PhysicalY.ShouldBe(130);
    }

    [Fact]
    public void MoveComponent_LockedComponent_ReturnsFalse()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.IsLocked = true;
        var vm = new ComponentViewModel(comp);
        _components.Add(vm);

        var result = _service.MoveComponent(vm, 50, 0, false, null, null, null);
        result.ShouldBeFalse();
        vm.X.ShouldBe(100); // Position unchanged
    }
}
