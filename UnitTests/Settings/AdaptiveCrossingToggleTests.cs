using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Settings;
using Shouldly;

namespace UnitTests.Settings;

/// <summary>Enabling adaptive crossings in settings applies to the open design immediately.</summary>
public class AdaptiveCrossingToggleTests
{
    [AvaloniaFact]
    public async Task EnableFromSettings_RoutesExistingConnectionsWithoutAnotherEdit()
    {
        var canvas = CreateCanvas();
        var binder = new CrossingInsertionCanvasBinder(canvas, () => null, action => action());
        var settings = new RoutingSettingsViewModel(canvas, binder);
        int starts = 0;
        bool wasRouting = false;
        canvas.Routing.StateChanged += () =>
        {
            if (canvas.IsRouting && !wasRouting) starts++;
            wasRouting = canvas.IsRouting;
        };

        settings.CrossingInsertionEnabled = true;
        settings.CrossingInsertionEnabled = true;

        try
        {
            canvas.IsRouting.ShouldBeTrue("the toggle must start its own asynchronous pass");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (canvas.IsRouting && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(5);
            }
            canvas.IsRouting.ShouldBeFalse();
            starts.ShouldBe(1);
            canvas.ConnectionManager.Connections.Single().RoutedPath.ShouldNotBeNull();
            binder.IsEnabled.ShouldBeTrue();
            settings.CrossingInsertionEnabled = false;
            starts.ShouldBe(1, "disabling insertion does not need a new search");
        }
        finally
        {
            canvas.Routing.CancelRouting();
        }
    }

    [AvaloniaFact]
    public void RestorePreferenceAndOpenSettings_DoNotStartRouting()
    {
        var canvas = CreateCanvas();
        var binder = new CrossingInsertionCanvasBinder(canvas, () => null, action => action());
        int updates = 0;
        canvas.Routing.StateChanged += () => updates++;

        binder.IsEnabled = true;
        var settings = new RoutingSettingsViewModel(canvas, binder);

        settings.CrossingInsertionEnabled.ShouldBeTrue();
        updates.ShouldBe(0);
        canvas.ConnectionManager.Connections.Single().RoutedPath.ShouldBeNull();
    }

    [AvaloniaFact]
    public void EnableOnEmptyCanvas_DoesNotStartRouting()
    {
        var canvas = new DesignCanvasViewModel();
        var binder = new CrossingInsertionCanvasBinder(canvas, () => null, action => action());
        var settings = new RoutingSettingsViewModel(canvas, binder);
        int updates = 0;
        canvas.Routing.StateChanged += () => updates++;

        settings.CrossingInsertionEnabled = true;

        binder.IsEnabled.ShouldBeTrue();
        updates.ShouldBe(0);
    }

    private static DesignCanvasViewModel CreateCanvas()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(-100, -100, 600, 600);
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        end.PhysicalX = 400;
        canvas.ConnectionManager.AddConnectionDeferred(
            start.PhysicalPins.First(p => p.Name == "out"),
            end.PhysicalPins.First(p => p.Name == "in"));
        return canvas;
    }
}
