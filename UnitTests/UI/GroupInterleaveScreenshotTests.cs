using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using Shouldly;
using UnitTests.UI.Flows;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Captures the canvas after a group is dragged so its bounding box swallows a part of
/// another group while the real geometry stays clear (the scene of
/// <see cref="UiFlowGroupInterleaveDragTests"/>). PNG lands in
/// <c>docs/pr-media/group-overlap-geometry/</c> with <c>CAP_UPDATE_PR_MEDIA=1</c>, a temp
/// dir otherwise (see <see cref="ScreenshotArtifacts.ResolvePrMediaDirectory"/>).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class GroupInterleaveScreenshotTests
{
    private const int CaptureAttempts = 3;

    [AvaloniaFact]
    public void CaptureInterleavedGroupsAfterDrop()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("group-overlap-geometry");
        Directory.CreateDirectory(dir);
        using var host = new UiFlowTestHost();
        var (groupA, groupB) = UiFlowGroupInterleaveDragTests.BuildScene(host.Vm);
        var target = UiFlowGroupInterleaveDragTests.InterleavedTarget(groupB);

        UiFlowGroupInterleaveDragTests.DragGroup(
            host, groupA, target.X - groupA.PhysicalX, target.Y - groupA.PhysicalY);

        string name = Environment.GetEnvironmentVariable("CAP_GROUP_OVERLAP_SHOT") ?? "after";
        Capture(host, Path.Combine(dir, $"interleave-drop-{name}.png"));
    }

    private static void Capture(UiFlowTestHost host, string path)
    {
        var window = host.Window;
        Dispatcher.UIThread.RunJobs();
        foreach (var designCanvas in window.GetVisualDescendants().OfType<DesignCanvas>())
            designCanvas.InvalidateVisual();

        WriteableBitmap? bitmap = null;
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            bitmap?.Dispose();
            bitmap = frame;
        }

        bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null for {path}");
        using (bitmap)
        {
            ScreenshotArtifacts.SavePng(bitmap, path);
        }
    }
}
