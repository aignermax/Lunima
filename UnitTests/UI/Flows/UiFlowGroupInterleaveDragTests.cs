using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// Through the real input pipeline: a group whose waveguide runs diagonally is dragged so
/// its bounding box swallows a part of another group — the part sits in an empty corner,
/// well clear of the waveguide. The drop must stick (placement is judged by the real
/// geometry, not by bounding boxes); a drop where the parts genuinely collide must still
/// snap back.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — CI covers it, local runs exclude Slow.
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowGroupInterleaveDragTests
{
    private const string PdkName = "Demo PDK";
    private const string ComponentName = "1x2 MMI Splitter"; // 80 × 55 µm

    [AvaloniaFact]
    public void DraggingGroupOverAnotherGroupsPart_KeepsDropWhenGeometryIsClear()
    {
        using var host = new UiFlowTestHost();
        var (groupA, groupB) = BuildScene(host.Vm);
        var target = InterleavedTarget(groupB);

        DragGroup(host, groupA, target.X - groupA.PhysicalX, target.Y - groupA.PhysicalY);

        Overlaps(Box(groupA), Box(groupB)).ShouldBeTrue("the scenario must make the bounding boxes overlap");
        groupA.PhysicalX.ShouldBe(target.X, 1.0, "the group must stay where it was dropped");
        groupA.PhysicalY.ShouldBe(target.Y, 1.0);
    }

    [AvaloniaFact]
    public void DraggingGroupOntoAnotherGroupsPart_SnapsBack()
    {
        using var host = new UiFlowTestHost();
        var (groupA, groupB) = BuildScene(host.Vm);
        var start = (groupA.PhysicalX, groupA.PhysicalY);
        var bPart = groupB.ChildComponents.OrderByDescending(c => c.PhysicalX).First();
        var aPart = groupA.ChildComponents.OrderBy(c => c.PhysicalX).First();

        // Put A's left part right on top of B's right part.
        DragGroup(host, groupA, bPart.PhysicalX - aPart.PhysicalX, bPart.PhysicalY - aPart.PhysicalY);

        groupA.PhysicalX.ShouldBe(start.PhysicalX, 1.0, "a real overlap must be refused");
        groupA.PhysicalY.ShouldBe(start.PhysicalY, 1.0);
    }

    /// <summary>
    /// Group A: two parts joined by a diagonal waveguide (its bounding box is mostly empty).
    /// Group B: two parts side by side joined by a straight waveguide.
    /// </summary>
    internal static (ComponentGroup A, ComponentGroup B) BuildScene(MainViewModel vm)
    {
        var groupA = BuildGroup(vm, (30, 30), (330, 330));
        var groupB = BuildGroup(vm, (250, 450), (500, 450));
        return (groupA, groupB);
    }

    /// <summary>
    /// Position for group A that puts B's right part into the empty lower-left corner of
    /// A's bounding box, ~45 µm away from A's diagonal waveguide.
    /// </summary>
    internal static (double X, double Y) InterleavedTarget(ComponentGroup groupB)
    {
        var bRight = groupB.ChildComponents.OrderByDescending(c => c.PhysicalX).First();
        return (bRight.PhysicalX - 100, bRight.PhysicalY - 240);
    }

    private static void DragGroup(UiFlowTestHost host, ComponentGroup group, double dx, double dy)
    {
        var vm = host.Vm;
        var win = host.Window;
        var canvasControl = UiInput.Descendants<DesignCanvas>(win).First();
        Point CanvasPoint(double x, double y) =>
            canvasControl.TranslatePoint(
                new Point(x * canvasControl.Zoom + vm.Canvas.PanX, y * canvasControl.Zoom + vm.Canvas.PanY), win)!.Value;

        UiInput.PressKey(win, Key.S);
        var part = group.ChildComponents.OrderBy(c => c.PhysicalX).First();
        double gx = part.PhysicalX + 40, gy = part.PhysicalY + 27;
        UiInput.DragMouse(win, CanvasPoint(gx, gy), CanvasPoint(gx + dx, gy + dy));
        UiInput.RunJobs();
    }

    private static ComponentGroup BuildGroup(MainViewModel vm, (double X, double Y) first, (double X, double Y) second)
    {
        var template = vm.LeftPanel.AllTemplates.Single(t => t.PdkSource == PdkName && t.Name == ComponentName);
        var a = vm.Canvas.AddComponent(ComponentTemplates.CreateFromTemplate(template, first.X, first.Y));
        var b = vm.Canvas.AddComponent(ComponentTemplates.CreateFromTemplate(template, second.X, second.Y));
        var start = a.Component.PhysicalPins.OrderByDescending(p => p.GetAbsolutePosition().Item1).First();
        var end = b.Component.PhysicalPins.OrderBy(p => p.GetAbsolutePosition().Item1).First();
        var (sx, sy) = start.GetAbsolutePosition();
        var (ex, ey) = end.GetAbsolutePosition();
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));
        vm.Canvas.ConnectPinsWithCachedRoute(start, end, route).ShouldNotBeNull();
        var command = new CreateGroupCommand(vm.Canvas, new List<ComponentViewModel> { a, b });
        vm.CommandManager.ExecuteCommand(command);
        return command.CreatedGroup!;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Box(ComponentGroup g) =>
        (g.PhysicalX, g.PhysicalY, g.PhysicalX + g.WidthMicrometers, g.PhysicalY + g.HeightMicrometers);

    private static bool Overlaps(
        (double MinX, double MinY, double MaxX, double MaxY) a, (double MinX, double MinY, double MaxX, double MaxY) b) =>
        a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY;
}
