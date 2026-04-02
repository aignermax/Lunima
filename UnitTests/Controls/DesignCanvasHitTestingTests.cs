using Avalonia;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.Controls;

/// <summary>
/// Unit tests for <see cref="DesignCanvasHitTesting"/>.
/// Verifies component, pin, and connection hit-test logic.
/// </summary>
public class DesignCanvasHitTestingTests
{
    [Fact]
    public void HitTestComponent_ReturnsNull_WhenViewModelIsNull()
    {
        var result = DesignCanvasHitTesting.HitTestComponent(new Point(0, 0), null);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestComponent_ReturnsNull_WhenNoComponents()
    {
        var vm = new DesignCanvasViewModel();
        var result = DesignCanvasHitTesting.HitTestComponent(new Point(50, 50), vm);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestComponent_FindsComponentAtPoint()
    {
        var vm = new DesignCanvasViewModel();
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = 100;
        component.PhysicalY = 100;
        vm.AddComponent(component, "Template");

        // Center of component
        var result = DesignCanvasHitTesting.HitTestComponent(
            new Point(component.PhysicalX + component.WidthMicrometers / 2,
                      component.PhysicalY + component.HeightMicrometers / 2),
            vm);

        result.ShouldNotBeNull();
        result!.Component.ShouldBe(component);
    }

    [Fact]
    public void HitTestComponent_ReturnsNull_WhenPointOutsideComponent()
    {
        var vm = new DesignCanvasViewModel();
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.PhysicalX = 100;
        component.PhysicalY = 100;
        vm.AddComponent(component, "Template");

        // Far outside the component
        var result = DesignCanvasHitTesting.HitTestComponent(new Point(0, 0), vm);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestPin_ReturnsNull_WhenViewModelIsNull()
    {
        var result = DesignCanvasHitTesting.HitTestPin(new Point(0, 0), null);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestPin_ReturnsNull_WhenNoComponents()
    {
        var vm = new DesignCanvasViewModel();
        var result = DesignCanvasHitTesting.HitTestPin(new Point(0, 0), vm);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestConnection_ReturnsNull_WhenViewModelIsNull()
    {
        var result = DesignCanvasHitTesting.HitTestConnection(new Point(0, 0), null);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestConnection_ReturnsNull_WhenNoConnections()
    {
        var vm = new DesignCanvasViewModel();
        var result = DesignCanvasHitTesting.HitTestConnection(new Point(50, 50), vm);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestGroupLabel_ReturnsNull_WhenViewModelIsNull()
    {
        var result = DesignCanvasHitTesting.HitTestGroupLabel(new Point(0, 0), null);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestGroupLabel_ReturnsNull_WhenNoGroups()
    {
        var vm = new DesignCanvasViewModel();
        var component = TestComponentFactory.CreateStraightWaveGuide();
        vm.AddComponent(component, "Template");

        // No groups, so no label to hit
        var result = DesignCanvasHitTesting.HitTestGroupLabel(new Point(50, 50), vm);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestGroupLockIcon_ReturnsNull_WhenNoGroups()
    {
        var vm = new DesignCanvasViewModel();
        var result = DesignCanvasHitTesting.HitTestGroupLockIcon(new Point(0, 0), vm);
        result.ShouldBeNull();
    }

    [Fact]
    public void HitTestGroupPin_ReturnsNullPin_WhenGroupIsNull()
    {
        var (pin, _) = DesignCanvasHitTesting.HitTestGroupPin(
            new Point(0, 0), null!, Enumerable.Empty<CAP_Core.Components.Connections.WaveguideConnection>());
        pin.ShouldBeNull();
    }
}
