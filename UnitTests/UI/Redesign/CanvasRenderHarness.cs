using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Export;
using UnitTests.Integration;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Loads shipped examples and renders the production <see cref="DesignCanvas"/> headless into a
/// bitmap — every layer the app draws (grid, chip frame, waveguides, components, deferred labels,
/// overlays), not a replica scene. Shared by the canvas screenshot and render-timing tests.
/// </summary>
internal static class CanvasRenderHarness
{
    /// <summary>Opens a shipped example the way the Home screen does and waits for post-load routing.</summary>
    public static async Task<DesignCanvasViewModel> LoadExampleAsync(string exampleFileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var canvas = new DesignCanvasViewModel();
        var fileOps = new FileOperationsViewModel(new CAP_DataAccess.Persistence.NativeDesign.NativeDesignFileStore(),
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new ErrorConsoleService());
        if (!await fileOps.OpenDesignAsCopyAsync(path))
            throw new InvalidOperationException($"Example '{exampleFileName}' failed to open");
        await fileOps.PostLoadRouting;
        return canvas;
    }

    /// <summary>Zoom and pan that fit every component into a viewport, like the app's Zoom-to-fit.</summary>
    public static double FitToView(DesignCanvasViewModel canvas, double width, double height)
    {
        var bounds = BoundingBoxCalculator.Calculate(canvas.Components)
            ?? throw new InvalidOperationException("Empty design");
        var padded = BoundingBoxCalculator.WithPadding(bounds, BoundingBoxCalculator.DefaultPaddingFraction);
        var (zoom, panX, panY) = BoundingBoxCalculator.CalculateZoomToFit(padded, width, height);
        canvas.PanX = panX;
        canvas.PanY = panY;
        return zoom;
    }

    /// <summary>Centers the world point (<paramref name="centerX"/>, <paramref name="centerY"/>) at <paramref name="zoom"/>.</summary>
    public static void CenterOn(DesignCanvasViewModel canvas, double centerX, double centerY,
        double zoom, double width, double height)
    {
        canvas.PanX = width / 2 - centerX * zoom;
        canvas.PanY = height / 2 - centerY * zoom;
    }

    /// <summary>Builds a laid-out <see cref="DesignCanvas"/> of the given pixel size.</summary>
    public static DesignCanvas CreateCanvas(DesignCanvasViewModel viewModel, double zoom, int width, int height)
    {
        var control = new DesignCanvas { ViewModel = viewModel, Zoom = zoom };
        control.Motion.IsEnabled = false;
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        return control;
    }

    /// <summary>Renders one full canvas frame into a new bitmap.</summary>
    public static RenderTargetBitmap Render(DesignCanvas control, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        using (var context = bitmap.CreateDrawingContext())
            control.Render(context);
        return bitmap;
    }

    /// <summary>Renders one frame into an existing bitmap (timing loops reuse the target).</summary>
    public static void RenderInto(DesignCanvas control, RenderTargetBitmap bitmap)
    {
        using var context = bitmap.CreateDrawingContext();
        context.FillRectangle(Brushes.Transparent, new Rect(bitmap.Size));
        control.Render(context);
    }
}
