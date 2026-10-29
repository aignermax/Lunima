using CAP.Avalonia.Controls.Rendering;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The canvas cue for length-locked routes: a route belongs to the lock group of its length
/// target, and the groups to glow are those of the selected and the hovered routes.
/// </summary>
public class LengthLockOverlayTests
{
    [Fact]
    public async Task ActiveGroups_AreTheSelectedAndHoveredRoutesTargets()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(4);
        connections[0].Connection.TargetLengthMicrometers = 1800.0001;
        connections[1].Connection.TargetLengthMicrometers = 1800.0002;
        connections[2].Connection.TargetLengthMicrometers = 2500;

        LengthLockOverlay.GroupOf(connections[3]).ShouldBeNull();
        LengthLockOverlay.GroupOf(connections[0]).ShouldBe(LengthLockOverlay.GroupOf(connections[1]));
        LengthLockOverlay.ActiveGroups(canvas, null).ShouldBeEmpty();

        connections[0].IsSelected = true;
        var groups = LengthLockOverlay.ActiveGroups(canvas, connections[2]);

        groups.ShouldBe(new[] { 1800.0, 2500.0 }, ignoreOrder: true);
    }
}
