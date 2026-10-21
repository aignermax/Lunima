using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.RerouteImported;

public partial class RerouteImportedRoutesViewModelTests
{
    [AvaloniaFact]
    public void PreservedRoute_OffersActionOnCanvasWithoutOpeningPropertiesOrContextMenu()
    {
        var canvas = new DesignCanvasViewModel();
        var (start, end) = AddComponentPair(canvas, 0);
        var route = ConnectFrozenImported(canvas, start, end);
        route.Connection.AttachAsDrawnGeometry(new AsDrawnGeometry(new[]
        {
            new OutlinePolygon { Points = new[]
            {
                new OutlinePoint(0, 0), new OutlinePoint(100, 0),
                new OutlinePoint(100, 2), new OutlinePoint(0, 2)
            } }
        }));
        var main = UnitTests.Helpers.MainViewModelTestHelper.CreateMainViewModel(
            commandManager: new CommandManager(), canvas: canvas);
        var panel = new CAP.Avalonia.Views.Panels.ImportedSelectionActions { DataContext = main };
        var window = new Window { Content = panel, Width = 420, Height = 200 };
        window.Show();
        try
        {
            var action = panel.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, main.BottomPanel.RerouteImported.RerouteSelectedCommand));
            action.IsEffectivelyVisible.ShouldBeFalse();
            main.BottomPanel.RerouteImported.SelectedConnection = route;
            Dispatcher.UIThread.RunJobs();
            action.IsEffectivelyVisible.ShouldBeTrue();
            action.IsEffectivelyEnabled.ShouldBeTrue();
            if (Environment.GetEnvironmentVariable("UI_SHOT_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(420, 200));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, "imported-route-canvas-action.png"));
            }
            main.BottomPanel.RerouteImported.SelectedConnection = null;
            Dispatcher.UIThread.RunJobs();
            action.IsEffectivelyVisible.ShouldBeFalse();
        }
        finally { window.Close(); }
    }
}
