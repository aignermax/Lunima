using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>Copies of background geometry (copy/paste, group templates) stay background.</summary>
public class ComponentCloneRoutingObstacleTests
{
    [Fact]
    public void Clone_KeepsTheBackgroundFlag()
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.IsRoutingObstacle = false;

        ((Component)component.Clone()).IsRoutingObstacle.ShouldBeFalse();
    }

    [Fact]
    public void Clone_OfARegularComponent_StaysAnObstacle()
    {
        ((Component)TestComponentFactory.CreateStraightWaveGuide().Clone()).IsRoutingObstacle.ShouldBeTrue();
    }
}
