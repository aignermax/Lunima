using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using UnitTests.Helpers;

namespace UnitTests.Gestures;

public class PlacementPreviewLifecycleTests
{
    [AvaloniaTheory]
    [InlineData("leave")]
    [InlineData("mode")]
    [InlineData("template")]
    [InlineData("disarm")]
    public void RealPointerPreview_ClearsWhenPlacementContextEnds(string change)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var canvas = new DesignCanvas { ViewModel = vm.Canvas, MainViewModel = vm, Margin = new Thickness(40) };
        var window = new Window { Width = 600, Height = 400, Content = canvas };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            vm.CanvasInteraction.SelectedTemplate = new ComponentTemplate { Name = "First", WidthMicrometers = 30, HeightMicrometers = 20 };
            vm.CanvasInteraction.CurrentMode = InteractionMode.PlaceComponent;
            window.MouseMove(new Point(200, 200));
            Dispatcher.UIThread.RunJobs();
            Assert.True(canvas.InteractionState.ShowPlacementPreview);
            Assert.Same(vm.CanvasInteraction.SelectedTemplate, canvas.InteractionState.PlacementPreviewTemplate);

            switch (change)
            {
                case "leave": window.MouseMove(new Point(10, 10)); break;
                case "mode": vm.CanvasInteraction.CurrentMode = InteractionMode.Connect; break;
                case "template": vm.CanvasInteraction.SelectedTemplate = new ComponentTemplate { Name = "Second" }; break;
                case "disarm": vm.CanvasInteraction.SelectedTemplate = null; break;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(canvas.InteractionState.ShowPlacementPreview);
            Assert.Null(canvas.InteractionState.PlacementPreviewTemplate);

            if (change is "leave" or "template")
            {
                window.MouseMove(new Point(220, 200));
                Dispatcher.UIThread.RunJobs();
                Assert.True(canvas.InteractionState.ShowPlacementPreview);
                Assert.Same(vm.CanvasInteraction.SelectedTemplate, canvas.InteractionState.PlacementPreviewTemplate);
            }
        }
        finally { window.Close(); }
    }
}
