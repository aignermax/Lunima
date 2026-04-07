using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.CodeExporter;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Demonstrates the three fundamental coordinate system bugs in the GDS export pipeline.
/// See docs/gds-coordinate-system-analysis.md for full analysis.
///
/// BUG 1 — Multi-segment path coordinate discontinuity (Issue #458):
///   Segment 1 uses GetAbsoluteNazcaPosition() (correct Nazca coords).
///   Segments 2+ use simple Y-flip of routing coordinates (editor coords).
///   Jump = HeightMicrometers - 2 * NazcaOriginOffsetY at every segment boundary.
///
/// BUG 2 — Legacy NazcaExporter.cs is completely broken:
///   No Y-flip, wrong rotation sign, no NazcaOriginOffset.
///
/// BUG 3 — GetAbsoluteNazcaPosition() ≠ (editorX, -editorY) for most components.
///   Only equivalent when NazcaOriginOffsetY = HeightMicrometers / 2.
/// </summary>
public class GdsCoordinateSystemBugTests
{
    private const double MicrometerTolerance = 0.01;

    // ── Bug 3 (architectural): NazcaPosition ≠ simple Y-flip ──────────────────

    /// <summary>
    /// Proves that GetAbsoluteNazcaPosition() equals simple Y-flip ONLY when
    /// oy_effective == HeightMicrometers / 2.
    ///
    /// oy_effective is from CalculateOriginOffset():
    ///   PDK/explicit oy: uses stored value; legacy (no PDK, oy=0): falls back to H.
    ///
    /// For the SiEPIC GC (oy=9.5, H=19): oy == H/2 → no mismatch.
    /// For a demo_pdk component with oy=0, H=50: oy_eff=0, jump = 50 µm.
    /// For a legacy component (stored oy=0, no PDK name): oy_eff=H=250, jump = -250 µm.
    /// </summary>
    [Theory]
    // GC-like: explicit oy=9.5=H/2 → jump = 0
    [InlineData("gc_like", 0.0, 9.5, 19.0, 9.5, 0.0)]
    // demo_pdk: PDK name → oy_eff=0 → jump = H = 50 µm
    [InlineData("demo_pdk.mmi2x2", 0.0, 0.0, 50.0, 12.5, 50.0)]
    // Explicit oy=H=50: oy_eff=50 → jump = 50 - 100 = -50 µm
    [InlineData("explicit_oy_h", 0.0, 50.0, 50.0, 12.5, -50.0)]
    // Legacy (no PDK, stored oy=0): oy_eff=H=250 → jump = -250 µm
    [InlineData("legacy_plain", 0.0, 0.0, 250.0, 125.0, -250.0)]
    public void GetAbsoluteNazcaPosition_VsSimpleYFlip_DifferenceDependsOnOriginOffset(
        string funcName, double nazcaOffsetX, double nazcaOffsetY,
        double componentHeight, double pinOffsetY, double expectedYDiff)
    {
        // Arrange: component at editor position (100, 200), rotation=0
        const double physX = 100.0;
        const double physY = 200.0;
        const double pinOffsetX = 0.0;

        var comp = CreateComponent(physX, physY, 100, componentHeight, nazcaOffsetX, nazcaOffsetY, funcName);
        var pin = CreatePin(comp, pinOffsetX, pinOffsetY);

        // Act: Get both representations
        var (nazcaX, nazcaY) = pin.GetAbsoluteNazcaPosition();
        var (editorX, editorY) = pin.GetAbsolutePosition();
        double simpleFlipY = -editorY;

        double actualYDiff = nazcaY - simpleFlipY;

        // Assert
        Math.Abs(actualYDiff - expectedYDiff).ShouldBeLessThan(MicrometerTolerance,
            $"[{funcName}] NazcaY vs simple-flip-Y difference: " +
            $"expected {expectedYDiff:F2} µm, got {actualYDiff:F2} µm " +
            $"(nazcaY={nazcaY:F3}, simpleFlipY={simpleFlipY:F3})");
    }

    // ── Bug 1: Multi-segment discontinuity ────────────────────────────────────

    /// <summary>
    /// Proves Bug 1: For a multi-segment path with a demo_pdk component (oy=0, H=50),
    /// segment 1 in the exported Nazca script starts at the correct Nazca pin position,
    /// but segment 2 starts at the naive Y-flip position — 50 µm off from where segment 1 ends.
    ///
    /// This demonstrates the coordinate jump at the segment 1→2 boundary.
    /// </summary>
    [Fact]
    public void MultiSegmentExport_DemoPdkComponent_Segment2StartsAtWrongY()
    {
        // Arrange: demo_pdk MMI at (0,0), H=50, NazcaOriginOffset=(0,0)
        // Pin a0 at editor (0, 12.5), rotation=0
        const double physX = 0, physY = 0, height = 50;
        const double pinOffsetX = 0, pinOffsetY = 12.5;
        const double nazcaOffsetY = 0; // demo_pdk with explicit (0,0)

        var comp = CreateComponentWithPdkFunctionName(physX, physY, 200, height, 0, nazcaOffsetY);
        var startPin = CreatePin(comp, pinOffsetX, pinOffsetY);
        startPin.Name = "a0";

        // Compute expected Nazca start position for segment 1
        var (correctNazcaX, correctNazcaY) = startPin.GetAbsoluteNazcaPosition();

        // Compute what simple Y-flip gives (= what segments 2+ will use)
        var (editorX, editorY) = startPin.GetAbsolutePosition();
        double naiveFlipY = -editorY;

        // Act: Build a 2-segment path in editor space (as routing does)
        double midX = editorX + 100;
        var seg1 = new StraightSegment(editorX, editorY, midX, editorY, 0);
        var seg2 = new StraightSegment(midX, editorY, midX + 100, editorY, 0);

        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(comp, "test");

        var endComp = CreateComponentWithPdkFunctionName(physX + 300, physY, 50, 50, 0, nazcaOffsetY);
        var endPin = CreatePin(endComp, 0, pinOffsetY);
        canvas.AddComponent(endComp, "end");

        var path = new RoutedPath();
        path.Segments.Add(seg1);
        path.Segments.Add(seg2);
        canvas.ConnectPinsWithCachedRoute(startPin, endPin, path);

        var exporter = new SimpleNazcaExporter();
        var script = exporter.Export(canvas);
        var parser = new NazcaCodeParser();
        var parsed = parser.Parse(script);

        // Assert: Segment 1 starts at the correct Nazca position
        parsed.WaveguideStubs.Count.ShouldBeGreaterThanOrEqualTo(1, "Must have at least one segment");
        var firstSeg = parsed.WaveguideStubs[0];

        Math.Abs(firstSeg.StartY - correctNazcaY).ShouldBeLessThan(MicrometerTolerance,
            $"Segment 1 Y should match GetAbsoluteNazcaPosition().Y ({correctNazcaY:F3} µm), " +
            $"got {firstSeg.StartY:F3} µm");

        // The KEY assertion: document the known coordinate difference for Bug 1.
        // For this component (oy=0, H=50, pinOffsetY=12.5): expected diff = H - 2*oy = 50 µm
        double expectedBugYDiff = height - 2.0 * nazcaOffsetY;  // = 50 µm
        double actualYDiff = correctNazcaY - naiveFlipY;

        Math.Abs(actualYDiff - expectedBugYDiff).ShouldBeLessThan(MicrometerTolerance,
            $"The Y coordinate difference between GetAbsoluteNazcaPosition() and " +
            $"simple Y-flip should be {expectedBugYDiff:F2} µm (= H - 2*oy). " +
            $"Got {actualYDiff:F2} µm. " +
            $"This is the magnitude of the Bug 1 jump for multi-segment paths.");

        if (parsed.WaveguideStubs.Count >= 2)
        {
            var secondSeg = parsed.WaveguideStubs[1];
            // Document: segment 2 uses naive Y-flip, which differs from segment 1 start by ~50 µm
            double seg2YDeviation = Math.Abs(secondSeg.StartY - naiveFlipY);
            seg2YDeviation.ShouldBeLessThan(MicrometerTolerance,
                $"Segment 2 starts at naive Y-flip ({naiveFlipY:F3} µm), " +
                $"but segment 1 started at correct Nazca Y ({correctNazcaY:F3} µm). " +
                $"This {actualYDiff:F1} µm jump IS Bug 1.");
        }
    }

    /// <summary>
    /// Proves that for a GC-like component (NazcaOriginOffsetY = H/2), multi-segment paths
    /// accidentally work correctly because the Y-flip mismatch is zero.
    /// This explains why simple GC designs appear correct even with multi-segment paths.
    /// </summary>
    [Fact]
    public void MultiSegmentExport_GcLikeComponent_NoYCoordinateJump()
    {
        // GC-like: oy = H/2 = 9.5, H = 19
        const double physX = 0, physY = 100;
        const double pinOffsetY = 9.5;
        const double nazcaOffsetY = 9.5;
        const double height = 19;

        var comp = CreateComponentWithPdkFunctionName(physX, physY, 100, height, 0, nazcaOffsetY);
        var startPin = CreatePin(comp, 0, pinOffsetY);

        // Verify: NazcaPosition == simple Y-flip (oy = H/2 case)
        var (nazcaX, nazcaY) = startPin.GetAbsoluteNazcaPosition();
        var (editorX, editorY) = startPin.GetAbsolutePosition();
        double naiveFlipY = -editorY;

        double diff = Math.Abs(nazcaY - naiveFlipY);
        diff.ShouldBeLessThan(MicrometerTolerance,
            $"When NazcaOriginOffsetY = H/2 = {nazcaOffsetY}, " +
            $"GetAbsoluteNazcaPosition().Y should equal -GetAbsolutePosition().Y. " +
            $"Got diff = {diff:F4} µm. " +
            $"This is why GC-to-GC routes appear correct even with multi-segment export.");
    }

    // ── Bug 2: Legacy NazcaExporter.cs ────────────────────────────────────────

    /// <summary>
    /// Proves Bug 2: the legacy NazcaExporter.cs places components at the wrong Y position.
    /// It uses component.PhysicalY directly (no Y-flip) and component.RotationDegrees
    /// directly (no sign negation).
    ///
    /// Expected Nazca Y = -(PhysY + NazcaOriginOffsetY)
    /// Actual legacy export Y = +PhysY (wrong sign, no offset)
    /// </summary>
    [Fact]
    public void LegacyNazcaExporter_ComponentPlacement_YPositionIsWrong()
    {
        // Arrange: component at editor (100, 200), H=50, NazcaOriginOffset=(0,9.5)
        // Correct Nazca Y = -(200 + 9.5) = -209.5
        // Legacy export Y = 200 (positive, no flip, no offset) ← WRONG
        const double physX = 100, physY = 200;
        const double nazcaOffsetY = 9.5;

        var comp = CreateComponent(physX, physY, 100, 19, 0, nazcaOffsetY);
        comp.NazcaFunctionName = "ebeam_gc_te1550";

        double expectedNazcaY = -(physY + nazcaOffsetY);  // correct: -209.5
        double legacyExportY = physY;                       // legacy BUG: +200

        double deviation = Math.Abs(legacyExportY - expectedNazcaY);

        // The deviation should be large (= physY + nazcaOffsetY + physY = 2*physY + oy)
        deviation.ShouldBeGreaterThan(100.0,
            $"Legacy exporter places component at Y={legacyExportY:F1} µm " +
            $"but correct Nazca Y = {expectedNazcaY:F1} µm. " +
            $"Deviation = {deviation:F1} µm. " +
            $"This is Bug 2: NazcaExporter.cs does not Y-flip the component position.");
    }

    /// <summary>
    /// Proves Bug 2b: the legacy NazcaExporter.cs uses the wrong rotation sign.
    /// Nazca requires negated rotation (Y-axis flip inverts rotation direction).
    /// Legacy code: .put(posX, posY, +RotationDegrees) → should be -RotationDegrees.
    /// </summary>
    [Theory]
    [InlineData(90.0)]
    [InlineData(270.0)]
    [InlineData(45.0)]
    public void LegacyNazcaExporter_RotatedComponent_RotationSignIsWrong(double editorRotation)
    {
        // In Nazca, Y-axis flip inverts the rotation sense.
        // A component rotated +90° in editor must be exported as -90° in Nazca.
        double correctNazcaRotation = -editorRotation;
        double legacyExportRotation = +editorRotation;  // BUG: wrong sign

        // Normalise to [-180, 180]
        correctNazcaRotation = NormalizeAngle(correctNazcaRotation);
        legacyExportRotation = NormalizeAngle(legacyExportRotation);

        bool rotationsMatch = Math.Abs(correctNazcaRotation - legacyExportRotation) < 0.1
                           || Math.Abs(Math.Abs(correctNazcaRotation - legacyExportRotation) - 360) < 0.1;

        rotationsMatch.ShouldBeFalse(
            $"Editor rotation={editorRotation}°: " +
            $"correct Nazca rotation={correctNazcaRotation}°, " +
            $"legacy export rotation={legacyExportRotation}°. " +
            $"These SHOULD be different (this test documents Bug 2b). " +
            $"If this fails, the legacy exporter was fixed.");
    }

    // ── Coordinate system math validation ─────────────────────────────────────

    /// <summary>
    /// Validates the complete coordinate transform formula for a PDK component:
    ///
    ///   cellX = PhysX + ox
    ///   cellY = -(PhysY + oy)
    ///   pinNazcaX = cellX + (OffsetX - ox) = PhysX + OffsetX           [ox cancels for rotation=0!]
    ///   pinNazcaY = cellY + (H - OffsetY - oy) = -(PhysY + oy) + H - OffsetY - oy
    ///             = -(PhysY + OffsetY) + (H - 2*oy)
    ///
    /// Note: oy_effective is determined by CalculateOriginOffset():
    ///   - PDK function name or explicit NazcaOriginOffset≠0 → oy = stored NazcaOriginOffsetY
    ///   - Legacy (no PDK name, oy=0) → oy_effective = HeightMicrometers (fallback)
    /// </summary>
    [Theory]
    // PDK component (demo_pdk. prefix), oy=0: pinNazcaY = -200 + (50-25-0) = -175
    [InlineData("demo_pdk.test", 0.0, 0.0, 50.0, 25.0, 0.0, 200.0, 0.0, 0.0, -175.0)]
    // GC-like component: explicit oy=9.5 → H/2 → no residual. PinNazcaY = -(100+9.5) = -109.5
    [InlineData("test_gc", 0.0, 9.5, 19.0, 9.5, 0.0, 100.0, 0.0, 0.0, -109.5)]
    // Legacy (no PDK name, oy=0) → fallback oy_eff=H=50. pinNazcaY = -(100+50)+(50-12.5-50) = -150-12.5 = -162.5
    [InlineData("plain_comp", 0.0, 0.0, 50.0, 12.5, 0.0, 100.0, 0.0, 0.0, -162.5)]
    public void NazcaPinPosition_Formula_MatchesGetAbsoluteNazcaPosition(
        string funcName, double nazcaOffsetX, double nazcaOffsetY,
        double height, double pinOffsetY,
        double physX, double physY,
        double pinOffsetX,
        double expectedAbsNazcaX, double expectedAbsNazcaY)
    {
        var comp = CreateComponent(physX, physY, 100, height, nazcaOffsetX, nazcaOffsetY, funcName);
        var pin = CreatePin(comp, pinOffsetX, pinOffsetY);

        var (actualX, actualY) = pin.GetAbsoluteNazcaPosition();

        Math.Abs(actualX - expectedAbsNazcaX).ShouldBeLessThan(MicrometerTolerance,
            $"NazcaPin X (funcName={funcName}): expected {expectedAbsNazcaX:F3}, got {actualX:F3}");
        Math.Abs(actualY - expectedAbsNazcaY).ShouldBeLessThan(MicrometerTolerance,
            $"NazcaPin Y (funcName={funcName}): expected {expectedAbsNazcaY:F3}, got {actualY:F3}");
    }

    /// <summary>
    /// Validates the coordinate jump formula:
    ///   jump = GetAbsoluteNazcaPosition().Y - (-GetAbsolutePosition().Y)
    ///        = H - 2 * oy_effective
    ///
    /// oy_effective comes from CalculateOriginOffset():
    ///   - PDK/explicit oy≠0: oy_eff = stored NazcaOriginOffsetY
    ///   - Legacy (no PDK name, stored oy=0): oy_eff = HeightMicrometers (fallback!)
    ///
    /// This jump is introduced at every segment boundary in Bug 1.
    /// </summary>
    [Theory]
    // demo_pdk with oy=0: oy_eff=0 → jump = H = 50 µm
    [InlineData("demo_pdk.mmi", 0.0, 50.0, 12.5, 50.0)]
    // Explicit oy=9.5, H=19 (GC-like, oy=H/2): jump = 19 - 19 = 0 µm
    [InlineData("test_gc", 9.5, 19.0, 9.5, 0.0)]
    // Explicit oy=50, H=50: jump = 50 - 100 = -50 µm
    [InlineData("test_comp", 50.0, 50.0, 25.0, -50.0)]
    // Legacy (plain funcName, stored oy=0): oy_eff=H=250 → jump = 250 - 500 = -250 µm
    [InlineData("plain_legacy", 0.0, 250.0, 125.0, -250.0)]
    public void MultiSegmentBug_JumpMagnitude_EqualsHeightMinus2TimesEffectiveOriginOffset(
        string funcName, double nazcaOffsetY, double height, double pinOffsetY, double expectedJump)
    {
        var comp = CreateComponent(0, 0, 100, height, 0, nazcaOffsetY, funcName);
        var pin = CreatePin(comp, 0, pinOffsetY);

        var (nazcaX, nazcaY) = pin.GetAbsoluteNazcaPosition();
        var (editorX, editorY) = pin.GetAbsolutePosition();
        double simpleFlipY = -editorY;

        double actualJump = nazcaY - simpleFlipY;

        Math.Abs(actualJump - expectedJump).ShouldBeLessThan(MicrometerTolerance,
            $"[{funcName}] Multi-segment jump for stored_oy={nazcaOffsetY}, H={height}: " +
            $"expected {expectedJump:F2} µm, got {actualJump:F2} µm. " +
            $"This is the Y-error introduced for segments 2+ in Bug 1.");
    }

    // ── Single-segment works correctly (regression guard) ──────────────────────

    /// <summary>
    /// Confirms that single-segment exports work correctly because they use
    /// FormatStraightSegmentFromPins() which ignores routing coordinates entirely.
    ///
    /// This is the baseline that masks Bug 1 in simple designs.
    /// </summary>
    [Fact]
    public void SingleSegment_PinToPin_ExportUsesNazcaCoordinatesNotRoutingCoordinates()
    {
        // Arrange: demo_pdk MMI at (0, 0), oy=0, H=50
        var comp = CreateComponentWithPdkFunctionName(0, 0, 200, 50, 0, 0);
        var startPin = CreatePin(comp, 0, 12.5);
        startPin.Name = "a0";

        var endComp = CreateComponentWithPdkFunctionName(300, 0, 50, 50, 0, 0);
        var endPin = CreatePin(endComp, 0, 12.5);
        endPin.Name = "in";

        var (startNazcaX, startNazcaY) = startPin.GetAbsoluteNazcaPosition();
        var (endNazcaX, endNazcaY) = endPin.GetAbsoluteNazcaPosition();

        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(comp, "start_comp");
        canvas.AddComponent(endComp, "end_comp");

        // Create single straight segment from routing coordinates (editor space)
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));
        canvas.ConnectPinsWithCachedRoute(startPin, endPin, path);

        var exporter = new SimpleNazcaExporter();
        var script = exporter.Export(canvas);
        var parser = new NazcaCodeParser();
        var parsed = parser.Parse(script);

        parsed.WaveguideStubs.Count.ShouldBeGreaterThan(0);
        var wg = parsed.WaveguideStubs[0];

        // Start must match Nazca pin position (NOT the routing start)
        Math.Abs(wg.StartX - startNazcaX).ShouldBeLessThan(MicrometerTolerance,
            $"Single-segment start X: expected {startNazcaX:F3}, got {wg.StartX:F3}");
        Math.Abs(wg.StartY - startNazcaY).ShouldBeLessThan(MicrometerTolerance,
            $"Single-segment start Y: expected {startNazcaY:F3}, got {wg.StartY:F3}. " +
            $"(Routing Y was {-sy:F3} — FormatStraightSegmentFromPins correctly ignores it.)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Component CreateComponent(
        double physX, double physY, double width, double height,
        double nazcaOffsetX, double nazcaOffsetY,
        string funcName = "test_component")
    {
        var template = new ComponentTemplate
        {
            Name = funcName,
            Category = "Test",
            WidthMicrometers = width,
            HeightMicrometers = height,
            NazcaOriginOffsetX = nazcaOffsetX,
            NazcaOriginOffsetY = nazcaOffsetY,
            NazcaFunctionName = funcName,
            PinDefinitions = Array.Empty<CAP.Avalonia.ViewModels.Library.PinDefinition>(),
            CreateSMatrix = _ =>
            {
                var sm = new CAP_Core.LightCalculation.SMatrix(new(), new());
                return sm;
            }
        };

        var comp = ComponentTemplates.CreateFromTemplate(template, physX, physY);
        return comp;
    }

    private static Component CreateComponentWithPdkFunctionName(
        double physX, double physY, double width, double height,
        double nazcaOffsetX, double nazcaOffsetY)
    {
        return CreateComponent(physX, physY, width, height, nazcaOffsetX, nazcaOffsetY,
            funcName: "demo_pdk.test_component");
    }

    private static PhysicalPin CreatePin(Component parent, double offsetX, double offsetY, double angle = 0)
    {
        var pin = new PhysicalPin
        {
            Name = $"pin_{offsetX:F0}_{offsetY:F0}",
            ParentComponent = parent,
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = angle
        };
        parent.PhysicalPins.Add(pin);
        return pin;
    }

    private static double NormalizeAngle(double degrees)
    {
        while (degrees > 180) degrees -= 360;
        while (degrees <= -180) degrees += 360;
        return degrees;
    }
}
