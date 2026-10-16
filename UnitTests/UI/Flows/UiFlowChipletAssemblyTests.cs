using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// The chiplet-assembly user story through the real window: System workspace → New die →
/// drop an edge coupler near each facing die edge (both snap on, facet out) → drag the new
/// die toward the chip until the couplers snap and link → Run → the Chiplets panel shows the
/// link with its loss. Screenshots of each stage land in <c>docs/pr-media/chiplet-assembly/</c>
/// with <c>UI_SHOT_DIR</c> + <c>CAP_UPDATE_PR_MEDIA=1</c>.
/// </summary>
[Trait("Category", "UiFlows")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowChipletAssemblyTests
{
    private const string DemoPdk = "Demo PDK";
    private const double CouplerInsetFromEdge = 60;
    private const int CaptureAttempts = 3;

    [AvaloniaFact]
    public async Task NewDie_DropCouplers_DragToLink_Run_ShowsLinkLoss()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var shots = ShotDirectory();
        vm.WorkspaceSwitch.Current = Workspace.System;
        UiInput.RunJobs();

        // New die: the flyout's Opening handler prepares the process list; pick Demo SOI.
        var newDieButton = UiInput.Descendants<Button>(win).Single(b => b.Name == "NewDieButton");
        UiInput.Click(win, newDieButton);
        vm.NewDie.ProcessOptions.ShouldNotBeEmpty("opening the flyout lists the processes");
        vm.NewDie.SelectedProcess = vm.NewDie.ProcessOptions.First(o => o.Selection.MemberPdkNames.Contains(DemoPdk));
        var how = ClickCreate(win, newDieButton, vm);
        vm.Canvas.Dies.RootDies.Count.ShouldBe(2, $"the chip became Die 1 and Die 2 sits next to it ({how}; status '{vm.StatusText}')");
        var chip = vm.Canvas.Dies.RootDies[0];
        var newDie = vm.Canvas.Dies.RootDies[1];
        FitBoth(win, vm, chip, newDie);
        Capture(win, shots, "01-new-die.png");

        // Drop one edge coupler near the chip's east edge and one near the new die's west edge.
        var coupler = vm.LeftPanel.AllTemplates.Single(t => t.PdkSource == DemoPdk && t.Name == "Edge Coupler");
        var chipOutline = DieAssembly.OutlineOf(chip);
        var dieOutline = DieAssembly.OutlineOf(newDie);
        vm.CanvasInteraction.SelectedTemplate = coupler;
        UiInput.ClickAt(win, CanvasPoint(win, vm, chipOutline.Right - CouplerInsetFromEdge, chipOutline.CenterY));
        vm.CanvasInteraction.SelectedTemplate = coupler;
        UiInput.ClickAt(win, CanvasPoint(win, vm, dieOutline.X + CouplerInsetFromEdge, dieOutline.CenterY));
        UiInput.PressKey(win, Key.Escape);
        var chipFacets = DieLinkSnapper.EdgeFacets(chip);
        var dieFacets = DieLinkSnapper.EdgeFacets(newDie);
        chipFacets.Count.ShouldBe(1, "the coupler snapped onto the chip's edge");
        dieFacets.Count.ShouldBe(1, "the coupler snapped onto the new die's edge");
        Capture(win, shots, "02-couplers-on-edges.png");

        // Drag the new die so its facet lands near the chip's facet: it snaps and links.
        var (ax, ay) = chipFacets[0].GetAbsolutePosition();
        var (bx, by) = dieFacets[0].GetAbsolutePosition();
        var grab = new Point(dieOutline.CenterX, dieOutline.Y + dieOutline.Height * 0.25);
        var drop = new Point(grab.X + (ax - bx) + 25, grab.Y + (ay - by) + 15);
        var from = CanvasPoint(win, vm, grab.X, grab.Y);
        var to = CanvasPoint(win, vm, drop.X, drop.Y);
        win.MouseMove(from);
        win.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        win.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        win.MouseMove(to, RawInputModifiers.LeftMouseButton);
        UiInput.RunJobs();
        Capture(win, shots, "03-drag-snap-guide.png");
        win.MouseUp(to, MouseButton.Left);
        UiInput.RunJobs();

        var link = vm.Canvas.ConnectionManager.Connections.ShouldHaveSingleItem("the snap creates the die-to-die link");
        link.IsCrossChipletFacetLink.ShouldBeTrue();
        CAP.Avalonia.Commands.Dies.DieLinkGeometry.Gap(link).ShouldBe(DieLinkSnapper.DefaultGapMicrometers, 1e-3);

        await vm.RunWorkspaceCommand.ExecuteAsync(null);
        UiInput.RunJobs();
        var row = vm.SystemOverview.Links.ShouldHaveSingleItem();
        row.LossText.ShouldEndWith("dB");
        Capture(win, shots, "04-run-link-loss.png");

        vm.Canvas.Selection.SelectSingle(vm.Canvas.Components.Single(c => c.Component == newDie));
        vm.Canvas.SelectedComponent = vm.Canvas.Components.Single(c => c.Component == newDie);
        UiInput.RunJobs();
        vm.DieProperties.IsVisible.ShouldBeTrue("a selected die shows its properties");
        Capture(win, shots, "05-die-properties.png");
    }

    /// <summary>
    /// Presses the flyout's Create button: through the window when the flyout lives in its
    /// overlay layer, otherwise (separate popup root) through keyboard activation.
    /// </summary>
    private static string ClickCreate(MainWindow win, Button newDieButton, CAP.Avalonia.ViewModels.MainViewModel vm)
    {
        var content = (newDieButton.Flyout as Flyout)?.Content as Control;
        var create = content?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CreateDieButton")
                     ?? content?.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "CreateDieButton");
        string how;
        if (create != null && ReferenceEquals(create.GetVisualRoot(), win))
        {
            UiInput.Click(win, create);
            how = "clicked in window";
        }
        else
        {
            create.ShouldNotBeNull("the flyout must expose its Create button");
            create!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            how = "keyboard activation in popup";
        }
        UiInput.RunJobs();
        return how;
    }

    private static void FitBoth(MainWindow win, CAP.Avalonia.ViewModels.MainViewModel vm, ComponentGroup a, ComponentGroup b)
    {
        var all = DieAssembly.OutlineOf(a).Union(DieAssembly.OutlineOf(b));
        ShowcaseCircuit.SetView(win, vm, (all.X - 200, all.Y - 300, all.Width + 400, all.Height + 600));
    }

    private static Point CanvasPoint(MainWindow win, CAP.Avalonia.ViewModels.MainViewModel vm, double x, double y)
    {
        var canvas = UiInput.Descendants<DesignCanvas>(win).First();
        return canvas.TranslatePoint(new Point(x * canvas.Zoom + vm.Canvas.PanX, y * canvas.Zoom + vm.Canvas.PanY), win)!.Value;
    }

    private static string? ShotDirectory()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return null;
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("chiplet-assembly");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Capture(MainWindow window, string? directory, string name)
    {
        if (directory == null)
            return;
        ScreenshotArtifacts.SettleAnimations();
        WriteableBitmap? bitmap = null;
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            bitmap?.Dispose();
            bitmap = frame;
        }
        bitmap.ShouldNotBeNull($"no frame captured for {name}");
        using (bitmap)
            ScreenshotArtifacts.SavePng(bitmap, Path.Combine(directory, name));
    }
}
