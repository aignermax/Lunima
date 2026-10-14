using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// The owner's report "right-click sometimes needs two clicks" (seen on and near dies): a right
/// press that jitters by a pixel before release was taken for a pan and swallowed the context
/// menu, and a finished middle-button pan left the pan flag set for the next right-click. Both
/// must open the canvas context menu on the first right-click.
/// </summary>
[Trait("Category", "UiFlows")]
[Collection("LocalizationSingleton")]
public class UiFlowCanvasContextMenuTests
{
    [AvaloniaFact]
    public void RightClickWithJitter_OnADie_OpensTheMenuFirstTime()
    {
        using var host = new UiFlowTestHost();
        var (win, canvas, point) = AssemblyWithDie(host);

        RightClick(win, point, jitter: 1.5);

        canvas.ContextMenu!.IsOpen.ShouldBeTrue("one right-click opens the menu, a pixel of jitter is not a pan");
        canvas.ContextMenu.Close();
    }

    [AvaloniaFact]
    public void RightClickAfterAMiddleButtonPan_OpensTheMenuFirstTime()
    {
        using var host = new UiFlowTestHost();
        var (win, canvas, point) = AssemblyWithDie(host);
        win.MouseDown(point, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
        win.MouseMove(point + new Point(60, 40), RawInputModifiers.MiddleMouseButton);
        win.MouseUp(point + new Point(60, 40), MouseButton.Middle);
        UiInput.RunJobs();

        RightClick(win, point, jitter: 0);

        canvas.ContextMenu!.IsOpen.ShouldBeTrue("a finished middle-button pan must not swallow the next context menu");
        canvas.ContextMenu.Close();
    }

    [AvaloniaFact]
    public void RightDrag_StillPans_WithoutAMenu()
    {
        using var host = new UiFlowTestHost();
        var (win, canvas, point) = AssemblyWithDie(host);
        double panX = host.Vm.Canvas.PanX;

        win.MouseDown(point, MouseButton.Right, RawInputModifiers.RightMouseButton);
        win.MouseMove(point + new Point(80, 0), RawInputModifiers.RightMouseButton);
        win.MouseUp(point + new Point(80, 0), MouseButton.Right);
        UiInput.RunJobs();

        host.Vm.Canvas.PanX.ShouldBe(panX + 80, 1e-6);
        canvas.ContextMenu!.IsOpen.ShouldBeFalse("a right-drag pans and opens no menu");
    }

    private static (MainWindow Window, DesignCanvas Canvas, Point Point) AssemblyWithDie(UiFlowTestHost host)
    {
        var vm = host.Vm;
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.NewDie.Prepare();
        vm.NewDie.CreateCommand.Execute(null);
        UiInput.RunJobs();
        var die2 = vm.Canvas.Dies.RootDies[1];
        DieFlowShots.Fit(host.Window, vm, vm.Canvas.Dies.RootDies.ToArray());
        var o = DieAssembly.OutlineOf(die2);
        var canvas = UiInput.Descendants<DesignCanvas>(host.Window).First();
        return (host.Window, canvas, DieFlowShots.CanvasPoint(host.Window, vm, o.X + 60, o.Y + 60));
    }

    private static void RightClick(MainWindow win, Point point, double jitter)
    {
        win.MouseMove(point);
        win.MouseDown(point, MouseButton.Right, RawInputModifiers.RightMouseButton);
        if (jitter > 0)
            win.MouseMove(point + new Point(jitter, jitter / 2), RawInputModifiers.RightMouseButton);
        win.MouseUp(point + new Point(jitter, jitter / 2), MouseButton.Right);
        UiInput.RunJobs();
    }
}
