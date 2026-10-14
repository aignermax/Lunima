using System.Diagnostics;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Grid.GroupCollision;
using UnitTests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.ViewModels.Services;

/// <summary>
/// Canvas-level placement around groups uses their real geometry: plain components may
/// sit in a group's empty corners, group drags interleave, and validation of a large
/// example group stays within the per-pointer-move budget.
/// </summary>
public class GroupGeometryPlacementTests
{
    /// <summary>Per-validation budget during a drag (one call per pointer move).</summary>
    private const double ValidationBudgetMilliseconds = 100;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class with xUnit output for the measured timings.</summary>
    public GroupGeometryPlacementTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void PlainComponent_InGroupsEmptyCorner_CanBePlaced()
    {
        var canvas = new DesignCanvasViewModel();
        var (groupA, _) = CollisionScene.LGroup("A", 100, 100);
        canvas.AddComponent(groupA);

        canvas.CanPlaceComponent(120, 330, 50, 50).ShouldBeTrue("the corner is empty");
        canvas.CanPlaceComponent(330, 200, 50, 50).ShouldBeFalse("this box straddles the group's waveguide");
    }

    [Fact]
    public void PlainComponentDrag_IntoGroupsEmptyCorner_IsAllowed()
    {
        var canvas = new DesignCanvasViewModel();
        var (groupA, _) = CollisionScene.LGroup("A", 100, 100);
        canvas.AddComponent(groupA);
        var part = canvas.AddComponent(CollisionScene.Part("p", 1000, 1000));

        canvas.CanMoveComponentTo(part, 120, 330).ShouldBeTrue();
        canvas.CanMoveComponentTo(part, 110, 110).ShouldBeFalse("on top of the group's part");
    }

    [Fact]
    public void GroupDrag_InterleavingAnotherGroup_IsAllowed()
    {
        var canvas = new DesignCanvasViewModel();
        var (groupA, _) = CollisionScene.LGroup("A", 100, 100);
        var (groupB, _) = CollisionScene.StraightGroup("B", 1000, 1000);
        canvas.AddComponent(groupA);
        var vmB = canvas.AddComponent(groupB);

        canvas.CanMoveComponentTo(vmB, -80, 330).ShouldBeFalse("outside the chip");
        canvas.CanMoveComponentTo(vmB, 100 - 180 + 200, 330).ShouldBeFalse("B's right part on A's waveguide leg");
        canvas.ChipMinX = -1000;
        canvas.CanMoveComponentTo(vmB, -80, 330).ShouldBeTrue("B's right part sits in A's empty corner");
    }

    [Fact]
    public async Task LargeExampleGroup_DragValidation_StaysWithinBudget()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample("Logic Gate ALU + RAM + ACC.lun");
        await fileOps.PostLoadRouting;
        var groupVm = canvas.Components
            .Where(c => c.Component is ComponentGroup)
            .OrderByDescending(c => ((ComponentGroup)c.Component).GetAllComponentsRecursive().Count)
            .First();
        var group = (ComponentGroup)groupVm.Component;
        group.GetAllComponentsRecursive().Count.ShouldBeGreaterThan(20, "the perf scenario needs a large group");

        // Wide chip so every candidate reaches the geometry check instead of the boundary early-out.
        canvas.ChipMinX = canvas.ChipMinY = -1e6;
        canvas.ChipMaxX = canvas.ChipMaxY = 1e6;
        canvas.BeginDragComponent(groupVm);
        try
        {
            canvas.CanMoveComponentTo(groupVm, groupVm.X, groupVm.Y); // warm-up (JIT)
            double worst = 0;
            for (int step = -10; step <= 10; step++)
            {
                var watch = Stopwatch.StartNew();
                canvas.CanMoveComponentTo(groupVm, groupVm.X + step * 37.0, groupVm.Y + step * 23.0);
                worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
            }
            // A full scan never stops at the first contact: the worst case of one validation.
            var all = canvas.Components.Select(c => c.Component).ToList();
            var fullScan = Stopwatch.StartNew();
            new CAP_Core.Grid.GroupCollisionDetector().FindContacts(group, all);
            double fullScanMs = fullScan.Elapsed.TotalMilliseconds;

            _output.WriteLine(
                $"{group.GetAllComponentsRecursive().Count} components, {group.InternalPaths.Count} top-level paths: " +
                $"worst validation {worst:F2} ms, full scan {fullScanMs:F2} ms");
            worst.ShouldBeLessThan(ValidationBudgetMilliseconds);
            fullScanMs.ShouldBeLessThan(ValidationBudgetMilliseconds);
        }
        finally
        {
            canvas.EndDragComponent(groupVm);
        }
    }
}
