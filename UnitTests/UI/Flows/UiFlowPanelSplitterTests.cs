using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// User story: drag the splitter next to the left or right panel to make the panel wider —
/// through the real MainWindow input pipeline. The panel column must follow the drag and the
/// new width must land in the view model (where it is persisted).
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default
// runs (CI covers it, the local runners exclude Category=Slow).
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowPanelSplitterTests
{
    private const double DragDistance = 150;
    private const double WidthTolerance = 5;

    [AvaloniaFact]
    public void DragLeftSplitterToTheRight_WidensTheLeftPanel()
    {
        using var host = new UiFlowTestHost();
        var grid = host.Window.FindControl<global::Avalonia.Controls.Grid>("LeftPanelGrid")!;
        var before = grid.ColumnDefinitions[0].Width.Value;

        DragSplitter(host.Window, grid, DragDistance);

        grid.ColumnDefinitions[0].Width.Value.ShouldBe(before + DragDistance, WidthTolerance,
            "the left panel column must follow the splitter drag");
        host.Vm.LeftPanel.LeftPanelWidth.Value.ShouldBe(before + DragDistance, WidthTolerance,
            "the new width must reach the view model so it gets persisted");
    }

    [AvaloniaFact]
    public void DragRightSplitterToTheLeft_WidensTheRightPanel()
    {
        using var host = new UiFlowTestHost();
        var grid = host.Window.FindControl<global::Avalonia.Controls.Grid>("RightPanelGrid")!;
        var before = grid.ColumnDefinitions[1].Width.Value;

        DragSplitter(host.Window, grid, -DragDistance);

        grid.ColumnDefinitions[1].Width.Value.ShouldBe(before + DragDistance, WidthTolerance,
            "the right panel column must grow when its splitter is dragged towards the canvas");
        host.Vm.RightPanel.RightPanelWidth.Value.ShouldBe(before + DragDistance, WidthTolerance,
            "the new width must reach the view model so it gets persisted");
    }

    /// <summary>Drags the grid's splitter horizontally by <paramref name="deltaX"/> pixels.</summary>
    private static void DragSplitter(Window window, global::Avalonia.Controls.Grid grid, double deltaX)
    {
        var splitter = grid.Children.OfType<GridSplitter>().Single();
        var center = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        UiInput.DragMouse(window, center, center + new Vector(deltaX, 0));
    }
}
