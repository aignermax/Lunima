using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Gestures;

public class ImportedRouteBoxSelectionTests
{
    [AvaloniaFact]
    public void Marquee_SelectsImportedRouteAndOrdinaryConnection_DeleteAndUndoRestoreBoth()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var canvas = vm.Canvas;
        canvas.PanX = canvas.PanY = 0;
        var control = new DesignCanvas { ViewModel = canvas, MainViewModel = vm, Zoom = 1 };
        var window = new Window { Width = 600, Height = 400, Content = control };
        var frozen = new CanvasFrozenPathViewModel(GdsFrozenRoutePathFactory.Create(new GdsOutlinePolygon
        {
            Layer = 3, DataType = 0,
            Points = new[] { new GdsOutlinePoint(80, 100), new GdsOutlinePoint(300, 100),
                new GdsOutlinePoint(300, 102), new GdsOutlinePoint(80, 102) }
        }));
        canvas.CanvasFrozenPaths.Add(frozen);
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        start.PhysicalX = -250; start.PhysicalY = 75;
        end.PhysicalX = 450; end.PhysicalY = 75;
        canvas.AddComponent(start, "Start"); canvas.AddComponent(end, "End");
        canvas.ConnectPins(start.PhysicalPins[1], end.PhysicalPins[0]);
        var connection = canvas.Connections.Single();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Drag(window, new Point(150, 50), new Point(250, 300));
            connection.IsSelected.ShouldBeTrue("ordinary waveguides already participate in marquee selection");
            frozen.IsSelected.ShouldBeTrue("pin-less imported routes must participate too");
            canvas.Selection.SelectedComponents.ShouldBeEmpty();

            vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);
            canvas.CanvasFrozenPaths.ShouldBeEmpty();
            canvas.Connections.ShouldBeEmpty();
            canvas.Components.Count.ShouldBe(2);
            vm.CommandManager.Undo();
            canvas.CanvasFrozenPaths.ShouldContain(frozen);
            canvas.Connections.Single().Connection.ShouldBeSameAs(connection.Connection);

            // A mixed box also owns a component and its incident connection. Undo must
            // restore that wire once, even though both were visibly selected.
            control.InteractionState.LastClickTime = DateTime.MinValue;
            Drag(window, new Point(150, 50), new Point(550, 350));
            canvas.Selection.SelectedComponents.Count.ShouldBe(1);
            vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);
            canvas.Components.Count.ShouldBe(1);
            canvas.CanvasFrozenPaths.ShouldBeEmpty();
            canvas.Connections.ShouldBeEmpty();
            vm.CommandManager.Undo();
            canvas.Components.Count.ShouldBe(2);
            canvas.CanvasFrozenPaths.ShouldContain(frozen);
            canvas.Connections.Count.ShouldBe(1);

            vm.CanvasInteraction.CanvasClicked(200, 200);
            vm.CanvasInteraction.SelectedWaveguideConnection.ShouldNotBeNull();
            control.InteractionState.LastClickTime = DateTime.MinValue;
            Drag(window, new Point(150, 160), new Point(250, 250), RawInputModifiers.Alt);
            canvas.Connections.Single().IsSelected.ShouldBeFalse("Alt also removes a directly clicked wire");
            vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);
            canvas.Connections.Count.ShouldBe(1);
            vm.CanvasInteraction.CanvasClicked(200, 200);
            control.InteractionState.LastClickTime = DateTime.MinValue;
            Drag(window, new Point(150, 50), new Point(250, 150));
            vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);
            canvas.Connections.Count.ShouldBe(1, "a replacement marquee must not delete the previously clicked wire");
            canvas.CanvasFrozenPaths.ShouldBeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Marquee_FrozenRoutes_RespectsControlAltAndClearsOnBlankClick()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.Canvas.PanX = vm.Canvas.PanY = 0;
        var control = new DesignCanvas { ViewModel = vm.Canvas, MainViewModel = vm, Zoom = 1 };
        var window = new Window { Width = 600, Height = 400, Content = control };
        CanvasFrozenPathViewModel AddRoute(double y)
        {
            var path = new CanvasFrozenPathViewModel(GdsFrozenRoutePathFactory.Create(new GdsOutlinePolygon
            {
                Points = new[] { new GdsOutlinePoint(80, y), new GdsOutlinePoint(300, y),
                    new GdsOutlinePoint(300, y + 2), new GdsOutlinePoint(80, y + 2) }
            }));
            vm.Canvas.CanvasFrozenPaths.Add(path);
            return path;
        }
        var first = AddRoute(100);
        var second = AddRoute(200);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Drag(window, new Point(150, 50), new Point(250, 150));
            first.IsSelected.ShouldBeTrue(); second.IsSelected.ShouldBeFalse();
            // Reset the click interval: consecutive drag starts must not be interpreted
            // as background double-clicks by the group-edit recognizer.
            control.InteractionState.LastClickTime = DateTime.MinValue;
            Drag(window, new Point(150, 160), new Point(250, 250), RawInputModifiers.Control);
            first.IsSelected.ShouldBeTrue(); second.IsSelected.ShouldBeTrue();
            control.InteractionState.LastClickTime = DateTime.MinValue;
            Drag(window, new Point(150, 50), new Point(250, 150), RawInputModifiers.Alt);
            first.IsSelected.ShouldBeFalse(); second.IsSelected.ShouldBeTrue();
            vm.CanvasInteraction.CanvasClicked(400, 350);
            second.IsSelected.ShouldBeFalse();
            vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);
            vm.Canvas.CanvasFrozenPaths.Count.ShouldBe(2);
            vm.CommandManager.UndoCount.ShouldBe(0);

            // Clicking an imported piece still selects/deletes just that piece, and a
            // subsequent ordinary marquee must replace that single click selection.
            vm.CanvasInteraction.CanvasClicked(180, 100);
            first.IsSelected.ShouldBeTrue();
            control.InteractionState.LastClickTime = DateTime.MinValue;
            Drag(window, new Point(150, 160), new Point(250, 250));
            first.IsSelected.ShouldBeFalse(); second.IsSelected.ShouldBeTrue();
            vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);
            vm.Canvas.CanvasFrozenPaths.ShouldBe(new[] { first });
        }
        finally { window.Close(); }
    }
    private static void Drag(Window window, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left, modifiers | RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, modifiers | RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }
}


