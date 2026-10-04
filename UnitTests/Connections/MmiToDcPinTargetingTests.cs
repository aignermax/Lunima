using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.UI.Flows;
using Xunit;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.Connections;

/// <summary>
/// Headless reproduction of the #1161 Connect-mode journey: a 2x2 MMI placed at x≈600 and a
/// Broadband DC TE 1550 at x≈1100 (same y), connecting the MMI's right upper port to the DC's
/// left upper port by aiming at the rendered pin markers. The field finding was a connection
/// counted as 1 with a "5µm, 0.00dB" label sitting on the DC but no waveguide drawn anywhere —
/// apparently joining two of the DC's own left ports (4.7µm apart, ≈ the reported 5µm).
/// #1185 fixed the marker <em>rendering</em> (<c>PinPitchSizer</c>); these tests pin the
/// <em>targeting</em> half through the real Connect-mode paths (pin hit-test →
/// <c>CreateConnectionCommand</c>), and the never-invisible-route invariant: the connection
/// must join the two chosen pins of the two different components, its path length must match
/// the ~250µm geometric distance, and it must carry drawable geometry.
/// </summary>
[Collection("LocalizationSingleton")]
public class MmiToDcPinTargetingTests
{
    // #1161 placement: "2x2 MMI at x≈600, Broadband DC at x≈1100" (top-left origins, same y).
    private const double MmiX = 600;
    private const double DcX = 1100;
    private const double ComponentsY = 400;

    // The demo 2x2 MMI's right ports are named out1 (upper) / out2 (lower); the SiEPIC
    // Broadband DC's left ports are "port 1" (upper) / "port 2" (lower), 4.7µm apart.
    private const string MmiRightUpperPinName = "out1";
    private const string DcLeftUpperPinName = "port 1";
    private const string DcLeftLowerPinName = "port 2";

    /// <summary>
    /// Clicking the center of a rendered pin marker must resolve to exactly the pin the marker
    /// belongs to — at the default zoom and at the working zoom of the #1158 repro. The marker
    /// is drawn centered at the pin's absolute position (<c>PinRenderer</c>), so aiming at the
    /// marker means hit-testing at exactly that point.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(3.0)]
    public void HitTestPin_AtRenderedMarkerPosition_PicksExactlyThatPin(double zoom)
    {
        var scene = BuildScene();

        DesignCanvasHitTesting
            .HitTestPin(ToPoint(scene.MmiOut), scene.Canvas, zoom)
            .ShouldBeSameAs(scene.MmiOut, "the MMI's right upper marker must target its own pin");
        DesignCanvasHitTesting
            .HitTestPin(ToPoint(scene.DcUpper), scene.Canvas, zoom)
            .ShouldBeSameAs(scene.DcUpper, "the DC's left upper marker must target its own pin");
    }

    /// <summary>
    /// The DC's two left ports sit 4.7µm apart — the pair the #1161 self-connection apparently
    /// joined. Both markers must stay individually targetable.
    /// </summary>
    [Fact]
    public void HitTestPin_DcLeftMarkers_StayIndividuallyTargetable()
    {
        var scene = BuildScene();
        var lower = Pin(scene.Dc, DcLeftLowerPinName);

        DesignCanvasHitTesting.HitTestPin(ToPoint(scene.DcUpper), scene.Canvas, 1.0)
            .ShouldBeSameAs(scene.DcUpper);
        DesignCanvasHitTesting.HitTestPin(ToPoint(lower), scene.Canvas, 1.0)
            .ShouldBeSameAs(lower);
    }

    /// <summary>
    /// Click-to-connect (hover → click on each marker), the <c>CanvasInteractionViewModel</c>
    /// path a pin click takes in Connect mode.
    /// </summary>
    [Fact]
    public async Task ClickToConnect_MmiToDc_CreatesVisibleRouteBetweenTheChosenPins()
    {
        var scene = BuildScene();
        var interaction = new CanvasInteractionViewModel(scene.Canvas, new CommandManager());
        interaction.CurrentMode = InteractionMode.Connect;

        interaction.CanvasMouseMove(scene.MmiOutPos.X, scene.MmiOutPos.Y);
        interaction.CanvasClicked(scene.MmiOutPos.X, scene.MmiOutPos.Y);
        interaction.CanvasMouseMove(scene.DcUpperPos.X, scene.DcUpperPos.Y);
        interaction.CanvasClicked(scene.DcUpperPos.X, scene.DcUpperPos.Y);

        var vm = await AssertConnectionAndRouteAsync(scene);
        vm.Connection.StartPin.ShouldBeSameAs(scene.MmiOut);
        vm.Connection.EndPin.ShouldBeSameAs(scene.DcUpper);
    }

    /// <summary>
    /// Press–drag–release from the MMI marker to the DC marker through the real input pipeline
    /// of a hosted <see cref="DesignCanvas"/> — the gesture the #1161 report describes
    /// ("long-press MMI right port, then DC left port").
    /// </summary>
    [AvaloniaFact]
    public async Task DragGesture_MmiToDc_CreatesVisibleRouteBetweenTheChosenPins()
    {
        var scene = BuildScene();
        var mainVm = MainViewModelTestHelper.CreateMainViewModel(canvas: scene.Canvas);
        mainVm.CanvasInteraction.CurrentMode = InteractionMode.Connect;

        var designCanvas = new DesignCanvas
        {
            ViewModel = scene.Canvas,
            MainViewModel = mainVm,
            Zoom = 1.0,
        };
        var window = new Window { Width = 1300, Height = 800, Content = designCanvas };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            UiInput.DragMouse(window,
                WindowPoint(window, designCanvas, scene.MmiOutPos),
                WindowPoint(window, designCanvas, scene.DcUpperPos));

            var vm = await AssertConnectionAndRouteAsync(scene);
            vm.Connection.StartPin.ShouldBeSameAs(scene.MmiOut);
            vm.Connection.EndPin.ShouldBeSameAs(scene.DcUpper);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Asserts the single created connection joins the two chosen pins of the two different
    /// components and carries drawable geometry spanning the gap between them — never an
    /// invisible 5µm self-connection on the DC.
    /// </summary>
    private static async Task<WaveguideConnectionViewModel> AssertConnectionAndRouteAsync(Scene scene)
    {
        var vm = scene.Canvas.Connections.ShouldHaveSingleItem(
            "the journey creates exactly one connection");
        var conn = vm.Connection;

        conn.StartPin.ParentComponent.ShouldBeSameAs(scene.Mmi);
        conn.EndPin.ParentComponent.ShouldBeSameAs(scene.Dc);

        await scene.Canvas.RecalculateRoutesAsync();

        conn.IsPathValid.ShouldBeTrue("the route across the open gap must succeed");
        conn.IsBlockedFallback.ShouldBeFalse("nothing blocks the gap — no blocked fallback");
        conn.RoutedPath.ShouldNotBeNull();
        conn.RoutedPath.Segments.ShouldNotBeEmpty(
            "the route must carry drawable geometry — a connection may never be invisible");
        conn.RoutedPath.IsInvalidGeometry.ShouldBeFalse();

        double distance = Distance(scene.MmiOutPos, scene.DcUpperPos);
        distance.ShouldBeGreaterThan(200, "scene sanity: the two pins are ~250µm apart");
        conn.PathLengthMicrometers.ShouldBeGreaterThan(distance * 0.9,
            "the label must reflect the MMI→DC span — a ~5µm length means the endpoints "
            + "collapsed onto the DC's own two ports (the #1161 defect)");
        conn.PathLengthMicrometers.ShouldBeLessThan(distance * 1.5,
            "the open straight-line gap needs no detour, so path length ≈ geometric distance");

        var first = conn.RoutedPath.Segments[0];
        var last = conn.RoutedPath.Segments[^1];
        Distance(first.StartPoint.X, first.StartPoint.Y, scene.MmiOutPos.X, scene.MmiOutPos.Y)
            .ShouldBeLessThan(1.0, "the route starts exactly at the MMI pin");
        Distance(last.EndPoint.X, last.EndPoint.Y, scene.DcUpperPos.X, scene.DcUpperPos.Y)
            .ShouldBeLessThan(1.0, "the route ends exactly at the DC pin");

        // The drawn geometry must be hittable where it is actually drawn (visibility proxy).
        var longest = conn.RoutedPath.Segments
            .OrderByDescending(s => Distance(
                s.StartPoint.X, s.StartPoint.Y, s.EndPoint.X, s.EndPoint.Y))
            .First();
        var mid = new Point(
            (longest.StartPoint.X + longest.EndPoint.X) / 2,
            (longest.StartPoint.Y + longest.EndPoint.Y) / 2);
        DesignCanvasHitTesting.HitTestConnection(mid, scene.Canvas).ShouldBeSameAs(vm,
            "the routed waveguide must be hittable on its drawn path");

        new DesignValidator().Validate(scene.Canvas.ConnectionManager.Connections)
            .ShouldBeEmpty("the created design must pass DRC-lite without warnings");
        return vm;
    }

    /// <summary>World → window coordinates (zoom 1, no pan: canvas-local == world).</summary>
    private static Point WindowPoint(Window window, Visual control, Point world)
        => control.TranslatePoint(world, window)
            ?? throw new InvalidOperationException("canvas is not attached to the window");

    private static Point ToPoint(PhysicalPin pin)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return new Point(x, y);
    }

    private static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.Single(p => p.Name == name);

    private static double Distance(Point a, Point b) => Distance(a.X, a.Y, b.X, b.Y);

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The real bundled-PDK components of the #1161 journey on one canvas.</summary>
    private static Scene BuildScene()
    {
        var mmiTemplate = TestPdkLoader.LoadFromPdk("demo-pdk.json")
            .Single(t => t.Name == "2x2 MMI Coupler");
        var dcTemplate = TestPdkLoader.LoadFromPdk("siepic-ebeam-pdk.json")
            .Single(t => t.Name == "Broadband DC TE 1550");

        var canvas = new DesignCanvasViewModel();
        var mmi = ComponentTemplates.CreateFromTemplate(mmiTemplate, MmiX, ComponentsY);
        var dc = ComponentTemplates.CreateFromTemplate(dcTemplate, DcX, ComponentsY);
        canvas.AddComponent(mmi, mmiTemplate.Name, mmiTemplate.PdkSource);
        canvas.AddComponent(dc, dcTemplate.Name, dcTemplate.PdkSource);

        var mmiOut = Pin(mmi, MmiRightUpperPinName);
        var dcUpper = Pin(dc, DcLeftUpperPinName);
        return new Scene(canvas, mmi, dc, mmiOut, dcUpper);
    }

    private sealed record Scene(
        DesignCanvasViewModel Canvas,
        Component Mmi,
        Component Dc,
        PhysicalPin MmiOut,
        PhysicalPin DcUpper)
    {
        public Point MmiOutPos => ToPoint(MmiOut);
        public Point DcUpperPos => ToPoint(DcUpper);
    }
}
