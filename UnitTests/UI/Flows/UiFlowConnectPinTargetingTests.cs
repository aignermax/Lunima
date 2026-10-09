using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// Issue #1217 / UX finding #1161: in Connect mode, dragging from a 2x2 MMI's right port to a
/// Broadband DC's left port produced a connection counted as 1 with a "5µm, 0.00dB" label
/// sitting on the DC — but no waveguide between the components, apparently joining two of the
/// DC's own closely spaced ports. These journeys drive the REAL input pipeline (headless
/// MainWindow → <see cref="DesignCanvas"/> → ConnectionGestureRecognizer →
/// CreateConnectionCommand) with the two PDK parts from the finding and assert the created
/// connection (a) ends exactly at the two targeted pins of the two DIFFERENT components,
/// (b) routes at roughly the geometric distance — never a ~5 µm phantom — and (c) has
/// drawable geometry, so it can never be invisible.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default
// runs (CI covers it, the local runners exclude Category=Slow).
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowConnectPinTargetingTests
{
    private const string SiepicPdkFile = "siepic-ebeam-pdk.json";

    // Placement from the #1161 report: MMI at x≈600, Broadband DC at x≈1100, same y.
    private const double MmiX = 600;
    private const double DcX = 1100;
    private const double ComponentsY = 200;

    // Any route honestly connecting the two components must span most of their ~466 µm
    // pin-to-pin distance; the #1161 phantom route was ~5 µm (the DC's own port pitch).
    private const double MinHonestRouteLengthMicrometers = 100;

    [AvaloniaFact]
    public async Task ConnectDrag_MmiRightPortToDcLeftPort_CreatesVisibleCrossComponentRoute()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var scene = BuildMmiDcScene(vm);
        vm.CanvasInteraction.SetConnectModeCommand.Execute(null);
        UiInput.RunJobs();
        var canvasControl = UiInput.Descendants<DesignCanvas>(win).First();

        // The journey from the finding: press the MMI's right upper port marker, drag,
        // release on the DC's left upper port marker — all at the rendered marker positions.
        UiInput.DragMouse(win,
            CanvasPoint(win, canvasControl, scene.MmiRightUpperAbs),
            CanvasPoint(win, canvasControl, scene.DcLeftUpperAbs));
        await vm.Canvas.RecalculateRoutesAsync();

        var conn = vm.Canvas.Connections.ShouldHaveSingleItem(
            $"the drag must create exactly one connection (status: {vm.StatusText})").Connection;
        conn.StartPin.ShouldBeSameAs(scene.MmiRightUpper,
            "the drag started on the MMI's right upper port marker");
        conn.EndPin.ShouldBeSameAs(scene.DcLeftUpper,
            "the drag was released on the DC's left upper port marker");
        AssertHonestVisibleRoute(conn);
    }

    [AvaloniaFact]
    public async Task ConnectDrag_PressWithoutPriorHover_StaleHighlightCannotHijackDragStart()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        var scene = BuildMmiDcScene(vm);
        vm.CanvasInteraction.SetConnectModeCommand.Execute(null);
        UiInput.RunJobs();
        var canvasControl = UiInput.Descendants<DesignCanvas>(win).First();

        // A previous interaction leaves the hover highlight on the DC's left upper port…
        win.MouseMove(CanvasPoint(win, canvasControl, scene.DcLeftUpperAbs));
        UiInput.RunJobs();
        vm.Canvas.HighlightedPin?.Pin.ShouldBeSameAs(scene.DcLeftUpper,
            "sanity: hovering the DC port highlights it");

        // …then the user presses the MMI's right upper port marker with NO intervening
        // pointer move — the touch / long-press situation from #1161, where no hover
        // tracking refreshes the highlight between interactions.
        win.MouseDown(CanvasPoint(win, canvasControl, scene.MmiRightUpperAbs),
            MouseButton.Left, RawInputModifiers.LeftMouseButton);
        UiInput.RunJobs();
        canvasControl.InteractionState.ConnectionDragStartPin.ShouldBeSameAs(scene.MmiRightUpper,
            "the press position is authoritative — a stale hover highlight on another "
            + "component must not hijack the drag start (the #1161 mechanism)");

        // Drag onto the DC's left LOWER port and release there.
        win.MouseMove(CanvasPoint(win, canvasControl, scene.DcLeftLowerAbs),
            RawInputModifiers.LeftMouseButton);
        win.MouseUp(CanvasPoint(win, canvasControl, scene.DcLeftLowerAbs), MouseButton.Left);
        UiInput.RunJobs();
        await vm.Canvas.RecalculateRoutesAsync();

        var conn = vm.Canvas.Connections.ShouldHaveSingleItem(
            $"the drag must create exactly one connection (status: {vm.StatusText})").Connection;
        conn.StartPin.ShouldBeSameAs(scene.MmiRightUpper);
        conn.EndPin.ShouldBeSameAs(scene.DcLeftLower);
        AssertHonestVisibleRoute(conn);
    }

    /// <summary>
    /// The #1161 invariants: endpoints on two DIFFERENT components, a routed length in the
    /// ballpark of the pin-to-pin distance (never the ~5 µm same-component phantom), and
    /// drawable geometry anchored at both pins so the route can never render as nothing.
    /// </summary>
    private static void AssertHonestVisibleRoute(
        CAP_Core.Components.Connections.WaveguideConnection conn)
    {
        conn.StartPin.ParentComponent.ShouldNotBeSameAs(conn.EndPin.ParentComponent,
            "#1161: the connection must join the MMI to the DC — not two ports of the DC itself");
        conn.IsPathValid.ShouldBeTrue("the open-field route between the components must route");
        conn.IsBlockedFallback.ShouldBeFalse("nothing blocks the straight shot — no fallback");
        conn.GetPathSegments().ShouldNotBeEmpty(
            "the connection must have drawable geometry — never an invisible route");

        var startAbs = conn.StartPin.GetAbsolutePosition();
        var endAbs = conn.EndPin.GetAbsolutePosition();
        double geometric = Distance(startAbs, endAbs);
        conn.RoutedPath!.TotalLengthMicrometers.ShouldBeGreaterThan(
            MinHonestRouteLengthMicrometers,
            $"#1161 phantom was ~5 µm; an honest MMI→DC route spans ≈{geometric:F0} µm");
        conn.RoutedPath.TotalLengthMicrometers.ShouldBeGreaterThan(geometric * 0.9,
            "the routed length must match the pin-to-pin distance, not a collapsed stub");

        var first = conn.RoutedPath.Segments[0];
        var last = conn.RoutedPath.Segments[^1];
        Distance((first.StartPoint.X, first.StartPoint.Y), startAbs)
            .ShouldBeLessThan(1.0, "the route starts exactly at the start pin");
        Distance((last.EndPoint.X, last.EndPoint.Y), endAbs)
            .ShouldBeLessThan(1.0, "the route ends exactly at the end pin");
    }

    /// <summary>Places the two real SiEPIC parts from the finding on the canvas.</summary>
    private static MmiDcScene BuildMmiDcScene(CAP.Avalonia.ViewModels.MainViewModel vm)
    {
        var templates = TestPdkLoader.LoadFromPdk(SiepicPdkFile);
        var mmiTemplate = templates.Single(t => t.Name == "MMI 2x2 50/50 TE 1310");
        var dcTemplate = templates.Single(t => t.Name == "Broadband DC TE 1550");

        var mmi = ComponentTemplates.CreateFromTemplate(mmiTemplate, MmiX, ComponentsY);
        var dc = ComponentTemplates.CreateFromTemplate(dcTemplate, DcX, ComponentsY);
        vm.Canvas.AddComponent(mmi, mmiTemplate.Name);
        vm.Canvas.AddComponent(dc, dcTemplate.Name);
        UiInput.RunJobs();

        // "Upper" is the smaller Y offset (screen Y grows downward); right ports face 0°,
        // left ports face 180°.
        var mmiRightUpper = mmi.PhysicalPins
            .Where(p => p.AngleDegrees == 0)
            .OrderBy(p => p.OffsetYMicrometers)
            .First();
        var dcLeftPins = dc.PhysicalPins
            .Where(p => p.AngleDegrees == 180)
            .OrderBy(p => p.OffsetYMicrometers)
            .ToList();
        return new MmiDcScene(
            mmiRightUpper,
            dcLeftPins[0],
            dcLeftPins[1]);
    }

    private static Point CanvasPoint(
        global::Avalonia.Controls.Window win, DesignCanvas canvasControl, (double X, double Y) world)
    {
        var vm = (CAP.Avalonia.ViewModels.MainViewModel)win.DataContext!;
        return canvasControl.TranslatePoint(
            new Point(world.X * canvasControl.Zoom + vm.Canvas.PanX,
                      world.Y * canvasControl.Zoom + vm.Canvas.PanY),
            win)!.Value;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The two targeted pins and their absolute (rendered marker) positions.</summary>
    private sealed record MmiDcScene(
        PhysicalPin MmiRightUpper,
        PhysicalPin DcLeftUpper,
        PhysicalPin DcLeftLower)
    {
        public (double X, double Y) MmiRightUpperAbs { get; } = MmiRightUpper.GetAbsolutePosition();
        public (double X, double Y) DcLeftUpperAbs { get; } = DcLeftUpper.GetAbsolutePosition();
        public (double X, double Y) DcLeftLowerAbs { get; } = DcLeftLower.GetAbsolutePosition();
    }
}
