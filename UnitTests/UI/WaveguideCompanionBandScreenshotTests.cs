using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.WaveguideCompanions;
using Shouldly;
using PathSegment = CAP_Core.Routing.PathSegment;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Canvas rendering of process companion bands: an "imported" cell whose GDS carries a core
/// (1/0) and an 8 µm trench (2/0) feeds two Lunima-routed waveguides of a process declaring
/// the same trench — the routed bands continue the imported band in the same layer color,
/// merge where the wires run close, and disappear when layer 2/0 is hidden in the layer panel.
/// Writes PNGs to <c>docs/pr-media/waveguide-companion-layers</c> with <c>CAP_UPDATE_PR_MEDIA=1</c>.
/// </summary>
public class WaveguideCompanionBandScreenshotTests
{
    private const int WidthPx = 1200;
    private const double TrenchOffset = 8.0;
    private static readonly Rect World = new(-20, 0, 340, 190);

    [AvaloniaFact]
    public void RoutedWaveguides_DrawTrenchBandsLikeTheImportedCell()
    {
        var canvas = BuildScene(new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("TRENCH", 2, 0, WaveguideCompanionRole.Trench, TrenchOffset, null),
        }));
        var renderer = new WaveguideCompanionBandRenderer();

        Capture(canvas, renderer, visibility: null, "01-trench-bands.png");
        renderer.LastBandBatchCount.ShouldBe(1, "both routes share one (layer, width) batch — merged like the mask");

        var hidden = new GdsLayerVisibilityState();
        hidden.Set(2, 0, isVisible: false, opacity: 1.0);
        Capture(canvas, renderer, hidden, "02-trench-layer-hidden.png");
        renderer.LastBandBatchCount.ShouldBe(0, "a hidden layer draws no band");
    }

    [AvaloniaFact]
    public void KeepOutBand_UsesItsOwnLayerColor()
    {
        var canvas = BuildScene(new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("TRENCH", 2, 0, WaveguideCompanionRole.Trench, TrenchOffset, null),
            new WaveguideCompanionLayer("KEEPOUT", 40, 0, WaveguideCompanionRole.KeepOut, 3.0, null),
        }));
        var renderer = new WaveguideCompanionBandRenderer();

        Capture(canvas, renderer, visibility: null, "03-trench-and-keepout.png");
        renderer.LastBandBatchCount.ShouldBe(2);
    }

    [AvaloniaFact]
    public void WithoutProvider_NothingIsDrawn()
    {
        var canvas = BuildScene(spec: null);
        var renderer = new WaveguideCompanionBandRenderer();

        Capture(canvas, renderer, visibility: null, fileName: null);
        renderer.LastBandBatchCount.ShouldBe(0);
    }

    private static DesignCanvasViewModel BuildScene(WaveguideCompanionSpec? spec)
    {
        var canvas = new DesignCanvasViewModel();
        if (spec != null)
            canvas.WaveguideCompanionSpecProvider = (_, _) => spec;

        // The imported cell draws its own core (1/0) and trench (2/0) polygons, in local coordinates.
        var imported = Box("imported_cell", 0, 60, 100, 60, ("o1", 100, 24), ("o2", 100, 36));
        imported.OutlinePolygons = new[]
        {
            Rect(2, 0, 0, 24 - 0.225 - TrenchOffset, 100, 36 + 0.225 + TrenchOffset),
            Rect(1, 0, 0, 24 - 0.225, 100, 24 + 0.225),
            Rect(1, 0, 0, 36 - 0.225, 100, 36 + 0.225),
        };
        var top = Box("top", 280, 10, 20, 20, ("i", 0, 10));
        var bottom = Box("bottom", 280, 150, 20, 20, ("i", 0, 10));
        foreach (var component in new[] { imported, top, bottom })
            canvas.Components.Add(new ComponentViewModel(component));

        // The routed wires leave the cell 12 µm apart (bands merge), then fan out north / south.
        AddRoute(canvas, imported.PhysicalPins[0], top.PhysicalPins[0],
            new StraightSegment(100, 84, 140, 84, 0),
            new BendSegment(140, 64, 20, 0, -90),
            new StraightSegment(160, 64, 160, 40, -90),
            new BendSegment(180, 40, 20, -90, 90),
            new StraightSegment(180, 20, 280, 20, 0));
        AddRoute(canvas, imported.PhysicalPins[1], bottom.PhysicalPins[0],
            new StraightSegment(100, 96, 140, 96, 0),
            new BendSegment(140, 116, 20, 0, 90),
            new StraightSegment(160, 116, 160, 140, 90),
            new BendSegment(180, 140, 20, 90, -90),
            new StraightSegment(180, 160, 280, 160, 0));
        return canvas;
    }

    private static void AddRoute(DesignCanvasViewModel canvas, PhysicalPin start, PhysicalPin end, params PathSegment[] segments)
    {
        var connection = new WaveguideConnection { StartPin = start, EndPin = end, WidthMicrometers = 0.45 };
        var path = new RoutedPath();
        foreach (var segment in segments)
            path.Segments.Add(segment);
        connection.RestoreCachedPath(path);
        canvas.Connections.Add(new WaveguideConnectionViewModel(connection));
    }

    private static Component Box(string id, double x, double y, double w, double h, params (string Name, double X, double Y)[] pins)
    {
        var component = TestComponentFactory.CreateBasicComponent();
        component.Identifier = id;
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = w;
        component.HeightMicrometers = h;
        foreach (var (name, px, py) in pins)
        {
            component.PhysicalPins.Add(new PhysicalPin
            {
                Name = name, ParentComponent = component, OffsetXMicrometers = px, OffsetYMicrometers = py,
            });
        }
        return component;
    }

    private static OutlinePolygon Rect(int layer, int dataType, double x0, double y0, double x1, double y1) => new()
    {
        Layer = layer,
        DataType = dataType,
        Points = new[] { new OutlinePoint(x0, y0), new OutlinePoint(x1, y0), new OutlinePoint(x1, y1), new OutlinePoint(x0, y1) },
    };

    private static void Capture(
        DesignCanvasViewModel canvas, WaveguideCompanionBandRenderer renderer,
        GdsLayerVisibilityState? visibility, string? fileName)
    {
        int height = (int)Math.Ceiling(WidthPx * World.Height / World.Width);
        var scene = new BandScene(canvas, renderer, visibility) { Width = WidthPx, Height = height };
        var window = new Window { Width = WidthPx, Height = height, Content = scene, Background = Brushes.Black };
        window.Show();
        using var bitmap = HeadlessFrameCapture.Capture(window);
        window.Close();
        if (fileName == null)
            return;
        var directory = ScreenshotArtifacts.ResolvePrMediaDirectory("waveguide-companion-layers");
        Directory.CreateDirectory(directory);
        ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(directory, fileName)).Length.ShouldBeGreaterThan(0);
    }

    /// <summary>Same draw order as <c>DesignCanvas.Render</c>: bands, cores, components.</summary>
    private sealed class BandScene(
        DesignCanvasViewModel canvas, WaveguideCompanionBandRenderer bands, GdsLayerVisibilityState? visibility) : Control
    {
        private readonly WaveguideConnectionRenderer _connections = new();
        private readonly ComponentRenderer _components = new();

        public override void Render(DrawingContext context)
        {
            double scale = Bounds.Width / World.Width;
            canvas.PanX = -World.X * scale;
            canvas.PanY = -World.Y * scale;
            context.FillRectangle(CanvasPalette.BackgroundBrush, new Rect(Bounds.Size));
            var rc = new CanvasRenderContext
            {
                ViewModel = canvas,
                InteractionState = new CanvasInteractionState(),
                Zoom = scale,
                Bounds = new Rect(Bounds.Size),
                LayerVisibility = visibility,
            };
            using var _ = context.PushTransform(Matrix.CreateTranslation(-World.X, -World.Y) * Matrix.CreateScale(scale, scale));
            bands.Render(context, rc);
            _connections.Render(context, rc);
            _components.Render(context, rc);
            rc.Labels.Flush(context, scale);
        }
    }
}
