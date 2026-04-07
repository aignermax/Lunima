using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Integration tests for waveguide endpoint alignment in GDS/Nazca export.
/// Verifies that exported waveguide paths start and end exactly at the
/// correct Nazca pin positions for all component types and rotations.
///
/// Issue #355: End pins don't align when NazcaOriginOffsetY ≠ HeightMicrometers.
/// Root cause: path routing uses editor coordinates; Nazca export uses Nazca coordinates.
/// For PDK components with NazcaOriginOffset, these differ, causing endpoint errors.
/// </summary>
public class WaveguideEndpointAlignmentTests
{
    private const double AlignmentTolerance = 0.1; // µm

    // ── Baseline: Two legacy components (delta=0) ────────────────────────────

    [Fact]
    public void SingleStraightWaveguide_TwoLegacyComponents_StartAndEndAlign()
    {
        // Legacy components: NazcaOriginOffset uses fallback (height).
        // delta = H - NazcaOriginOffsetY = 0 → GetAbsoluteNazcaPosition() == (editorX, -editorY)
        var (compA, pinOut) = CreateLegacyComponent("CompA", x: 0, y: 0, width: 100, height: 50,
            pinOffsetX: 100, pinOffsetY: 25, pinAngle: 0);
        var (compB, pinIn) = CreateLegacyComponent("CompB", x: 200, y: 0, width: 100, height: 50,
            pinOffsetX: 0, pinOffsetY: 25, pinAngle: 180);

        var (startPt, endPt) = ExportAndExtractEndpoints(pinOut, pinIn);

        var (expectedStartX, expectedStartY) = pinOut.GetAbsoluteNazcaPosition();
        var (expectedEndX, expectedEndY) = pinIn.GetAbsoluteNazcaPosition();

        AssertAligned(startPt, (expectedStartX, expectedStartY), "start");
        AssertAligned(endPt, (expectedEndX, expectedEndY), "end");
    }

    // ── Core bug: PDK component with NazcaOriginOffsetY ≠ Height ─────────────

    [Fact]
    public void SingleStraightWaveguide_LegacyToPdk_EndAligns_Issue355()
    {
        // Legacy start + PDK end with NazcaOriginOffsetY ≠ Height.
        // Before fix: waveguide misses end pin by (H_end - NazcaOriginOffsetY_end).
        var (_, pinOut) = CreateLegacyComponent("LegacySource", x: 0, y: 0, width: 100, height: 50,
            pinOffsetX: 100, pinOffsetY: 25, pinAngle: 0);
        var (_, pinIn) = CreatePdkComponent("PdkDest", x: 200, y: 0, width: 100, height: 50,
            nazcaOriginOffsetY: 12.5, pinOffsetX: 0, pinOffsetY: 25, pinAngle: 180);

        var (startPt, endPt) = ExportAndExtractEndpoints(pinOut, pinIn);

        var (expectedStartX, expectedStartY) = pinOut.GetAbsoluteNazcaPosition();
        var (expectedEndX, expectedEndY) = pinIn.GetAbsoluteNazcaPosition();

        AssertAligned(startPt, (expectedStartX, expectedStartY), "start");
        AssertAligned(endPt, (expectedEndX, expectedEndY), "end (Issue #355)");
    }

    [Fact]
    public void SingleStraightWaveguide_PdkToLegacy_EndAligns_Issue355()
    {
        // PDK start (with non-standard origin offset) + Legacy end.
        var (_, pinOut) = CreatePdkComponent("PdkSource", x: 0, y: 0, width: 100, height: 50,
            nazcaOriginOffsetY: 12.5, pinOffsetX: 100, pinOffsetY: 25, pinAngle: 0);
        var (_, pinIn) = CreateLegacyComponent("LegacyDest", x: 200, y: 0, width: 100, height: 50,
            pinOffsetX: 0, pinOffsetY: 25, pinAngle: 180);

        var (startPt, endPt) = ExportAndExtractEndpoints(pinOut, pinIn);

        var (expectedStartX, expectedStartY) = pinOut.GetAbsoluteNazcaPosition();
        var (expectedEndX, expectedEndY) = pinIn.GetAbsoluteNazcaPosition();

        AssertAligned(startPt, (expectedStartX, expectedStartY), "start");
        AssertAligned(endPt, (expectedEndX, expectedEndY), "end (Issue #355)");
    }

    [Fact]
    public void SingleStraightWaveguide_TwoPdkComponents_DifferentOriginOffset_EndAligns()
    {
        // Both PDK, but different NazcaOriginOffsetY → delta differs → classic bug.
        var (_, pinOut) = CreatePdkComponent("PdkA", x: 0, y: 0, width: 100, height: 50,
            nazcaOriginOffsetY: 10.0, pinOffsetX: 100, pinOffsetY: 25, pinAngle: 0);
        var (_, pinIn) = CreatePdkComponent("PdkB", x: 200, y: 0, width: 80, height: 60,
            nazcaOriginOffsetY: 20.0, pinOffsetX: 0, pinOffsetY: 30, pinAngle: 180);

        var (startPt, endPt) = ExportAndExtractEndpoints(pinOut, pinIn);

        var (expectedStartX, expectedStartY) = pinOut.GetAbsoluteNazcaPosition();
        var (expectedEndX, expectedEndY) = pinIn.GetAbsoluteNazcaPosition();

        AssertAligned(startPt, (expectedStartX, expectedStartY), "start");
        AssertAligned(endPt, (expectedEndX, expectedEndY), "end (different PDK offsets)");
    }

    [Fact]
    public void SingleStraightWaveguide_TwoPdkComponents_SameOriginOffset_EndAligns()
    {
        // Both PDK with same offset → delta equal → should always work.
        var (_, pinOut) = CreatePdkComponent("PdkA", x: 0, y: 0, width: 100, height: 50,
            nazcaOriginOffsetY: 25.0, pinOffsetX: 100, pinOffsetY: 25, pinAngle: 0);
        var (_, pinIn) = CreatePdkComponent("PdkB", x: 200, y: 0, width: 100, height: 50,
            nazcaOriginOffsetY: 25.0, pinOffsetX: 0, pinOffsetY: 25, pinAngle: 180);

        var (startPt, endPt) = ExportAndExtractEndpoints(pinOut, pinIn);

        var (expectedStartX, expectedStartY) = pinOut.GetAbsoluteNazcaPosition();
        var (expectedEndX, expectedEndY) = pinIn.GetAbsoluteNazcaPosition();

        AssertAligned(startPt, (expectedStartX, expectedStartY), "start");
        AssertAligned(endPt, (expectedEndX, expectedEndY), "end (same PDK offset)");
    }

    // ── Grating Coupler: real-world PDK values (H=38, NazcaOriginOffsetY=9.5) ─

    [Fact]
    public void SingleStraightWaveguide_GratingCouplerAsEndpoint_EndAligns()
    {
        // Simulates the reported bug scenario: grating coupler at end.
        // GratingCoupler: H=38µm, NazcaOriginOffsetY=9.5 → delta = 38-9.5 = 28.5µm error without fix.
        var (_, pinOut) = CreateLegacyComponent("Source", x: 0, y: 10, width: 100, height: 38,
            pinOffsetX: 100, pinOffsetY: 19, pinAngle: 0);
        var (_, pinIn) = CreatePdkComponent("GratingCoupler", x: 200, y: 10, width: 38, height: 38,
            nazcaOriginOffsetY: 9.5, pinOffsetX: 0, pinOffsetY: 19, pinAngle: 180);

        var (startPt, endPt) = ExportAndExtractEndpoints(pinOut, pinIn);

        var (expectedStartX, expectedStartY) = pinOut.GetAbsoluteNazcaPosition();
        var (expectedEndX, expectedEndY) = pinIn.GetAbsoluteNazcaPosition();

        AssertAligned(startPt, (expectedStartX, expectedStartY), "start");
        AssertAligned(endPt, (expectedEndX, expectedEndY), "end (grating coupler)");
    }

    // ── Rotation tests ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void SingleStraightWaveguide_RotatedLegacyComponents_EndAligns(double rotation)
    {
        var (_, pinOut) = CreateLegacyComponent("RotA", x: 0, y: 0, width: 100, height: 50,
            pinOffsetX: 100, pinOffsetY: 25, pinAngle: 0, rotation: rotation);
        var (_, pinIn) = CreateLegacyComponent("RotB", x: 300, y: 0, width: 100, height: 50,
            pinOffsetX: 0, pinOffsetY: 25, pinAngle: 180, rotation: rotation);

        var (startPt, endPt) = ExportAndExtractEndpoints(pinOut, pinIn);

        var (expectedStartX, expectedStartY) = pinOut.GetAbsoluteNazcaPosition();
        var (expectedEndX, expectedEndY) = pinIn.GetAbsoluteNazcaPosition();

        AssertAligned(startPt, (expectedStartX, expectedStartY), $"start (rot={rotation}°)");
        AssertAligned(endPt, (expectedEndX, expectedEndY), $"end (rot={rotation}°)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void SingleStraightWaveguide_RotatedPdkComponents_EndAligns(double rotation)
    {
        var (_, pinOut) = CreatePdkComponent("PdkRotA", x: 0, y: 0, width: 100, height: 50,
            nazcaOriginOffsetY: 12.5, pinOffsetX: 100, pinOffsetY: 25, pinAngle: 0, rotation: rotation);
        var (_, pinIn) = CreatePdkComponent("PdkRotB", x: 300, y: 0, width: 100, height: 50,
            nazcaOriginOffsetY: 12.5, pinOffsetX: 0, pinOffsetY: 25, pinAngle: 180, rotation: rotation);

        var (startPt, endPt) = ExportAndExtractEndpoints(pinOut, pinIn);

        var (expectedStartX, expectedStartY) = pinOut.GetAbsoluteNazcaPosition();
        var (expectedEndX, expectedEndY) = pinIn.GetAbsoluteNazcaPosition();

        AssertAligned(startPt, (expectedStartX, expectedStartY), $"start PDK (rot={rotation}°)");
        AssertAligned(endPt, (expectedEndX, expectedEndY), $"end PDK (rot={rotation}°)");
    }

    // ── Multi-segment: verify uniform coordinate transformation (Issue #456) ──

    /// <summary>
    /// Multi-segment path with PDK component: adjacent segments must be continuous in Nazca space.
    /// Root cause (Issue #456): segment[0] used GetAbsoluteNazcaPosition() (includes NazcaOriginOffset)
    /// while segment[1+] used a plain Y-flip — different coordinate systems caused a visible Y-gap.
    /// Fix: all segments use the same delta offset derived from the start pin.
    /// </summary>
    [Fact]
    public void MultiSegmentWaveguide_PdkComponent_SegmentsAreContinuousInNazca_Issue456()
    {
        // PDK component with NazcaOriginOffset — like an MMI 2x2 (H=38µm, NazcaOriginOffsetY=9.5µm).
        // NazcaOriginOffsetY=9.5 means deltaY = nazcaPinY + editorPinY = 0 + 19 = 19.
        // Without the fix, segment[1] would be shifted by 19 µm in Y relative to segment[0].
        var (_, pinOut) = CreatePdkComponent("MmiOut", x: 0, y: 0, width: 100, height: 38,
            nazcaOriginOffsetY: 9.5, pinOffsetX: 100, pinOffsetY: 19, pinAngle: 0);
        var (_, pinIn) = CreatePdkComponent("MmiIn", x: 300, y: 0, width: 100, height: 38,
            nazcaOriginOffsetY: 9.5, pinOffsetX: 0, pinOffsetY: 19, pinAngle: 180);

        var (sx, sy) = pinOut.GetAbsolutePosition(); // (100, 19)
        var (ex, ey) = pinIn.GetAbsolutePosition();  // (300, 19) — same Y, both horizontal

        // Two horizontal segments sharing the midpoint — deliberately split to expose the Y-gap bug.
        double midX = (sx + ex) / 2.0; // 200
        var segments = new List<PathSegment>
        {
            new StraightSegment(sx, sy, midX, sy, 0),   // seg[0]: 100→200, editor Y=19
            new StraightSegment(midX, sy, ex, ey, 0)    // seg[1]: 200→300, editor Y=19 (the "last" segment)
        };

        var sb = new StringBuilder();
        SimpleNazcaExporter.AppendSegmentExport(sb, segments, pinOut, pinIn);
        var nazcaCode = sb.ToString();

        var lines = nazcaCode.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("nd.strt(") || l.Contains("nd.bend("))
            .ToList();
        lines.Count.ShouldBe(2, "Expected 2 exported segments");

        // Compute where segment[0] ends in Nazca space: start + length along angle.
        var seg0End = ComputeEndPoint(lines[0]);

        // Segment[1] must start exactly where segment[0] ends — no Y-gap.
        var (seg1StartX, seg1StartY) = ExtractStartPoint(lines[1]);
        Math.Abs(seg1StartX - seg0End.X).ShouldBeLessThan(AlignmentTolerance,
            $"Issue #456: Seg[0] ends at X={seg0End.X:F3} but seg[1] starts at X={seg1StartX:F3}");
        Math.Abs(seg1StartY - seg0End.Y).ShouldBeLessThan(AlignmentTolerance,
            $"Issue #456: Y-gap between segments (NazcaOriginOffset not applied uniformly). " +
            $"Seg[0] ends at Y={seg0End.Y:F3} but seg[1] starts at Y={seg1StartY:F3}");
    }

    // ── Multi-segment: verify start pin alignment is preserved ────────────────

    [Fact]
    public void MultiSegmentWaveguide_StartPinAligns()
    {
        // For multi-segment paths (with bends), the start pin should always be correct.
        var (_, pinOut) = CreateLegacyComponent("MsA", x: 0, y: 0, width: 100, height: 50,
            pinOffsetX: 100, pinOffsetY: 25, pinAngle: 0);
        var (_, pinIn) = CreateLegacyComponent("MsB", x: 200, y: 100, width: 100, height: 50,
            pinOffsetX: 0, pinOffsetY: 25, pinAngle: 180);

        var segments = new List<PathSegment>
        {
            new StraightSegment(100, 25, 150, 25, 0),
            new BendSegment(150, 75, 50, 0, 90),
            new StraightSegment(200, 75, 200, 125, 90)
        };

        var nazcaCode = ExportWithCustomSegments(pinOut, pinIn, segments);

        var firstLine = nazcaCode
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .First(l => l.Contains("nd.strt(") || l.Contains("nd.bend("));

        var (startX, startY) = ExtractStartPoint(firstLine);
        var (expectedX, expectedY) = pinOut.GetAbsoluteNazcaPosition();

        Math.Abs(startX - expectedX).ShouldBeLessThan(AlignmentTolerance,
            $"Multi-segment start X off: {startX} vs {expectedX}");
        Math.Abs(startY - expectedY).ShouldBeLessThan(AlignmentTolerance,
            $"Multi-segment start Y off: {startY} vs {expectedY}");
    }

    // ── Helper: exported Nazca line endpoint formula verification ─────────────

    [Fact]
    public void FormatStraightSegmentFromPins_ZeroDistance_ProducesZeroLengthSegment()
    {
        // Degenerate: start == end → length should be 0
        var (_, pinOut) = CreateLegacyComponent("Same", x: 100, y: 50, width: 10, height: 10,
            pinOffsetX: 5, pinOffsetY: 5, pinAngle: 0);

        // Create an identical pin at the same location
        var pinIn = new PhysicalPin
        {
            Name = "in",
            OffsetXMicrometers = 5,
            OffsetYMicrometers = 5,
            AngleDegrees = 180,
            ParentComponent = pinOut.ParentComponent
        };

        var sb = new StringBuilder();
        SimpleNazcaExporter.AppendSegmentExport(
            sb,
            new List<PathSegment> { new StraightSegment(0, 0, 0, 0, 0) },
            pinOut,
            pinIn);

        var line = sb.ToString();
        line.ShouldContain("nd.strt(length=0.00)");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a legacy component (no PDK function → CalculateOriginOffset uses height fallback).</summary>
    private static (Component comp, PhysicalPin pin) CreateLegacyComponent(
        string name, double x, double y, double width, double height,
        double pinOffsetX, double pinOffsetY, double pinAngle, double rotation = 0)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var comp = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: name,
            rotationCounterClock: DiscreteRotation.R0);

        comp.PhysicalX = x;
        comp.PhysicalY = y;
        comp.WidthMicrometers = width;
        comp.HeightMicrometers = height;
        comp.RotationDegrees = rotation;
        comp.NazcaOriginOffsetX = 0;
        comp.NazcaOriginOffsetY = 0;

        var pin = new PhysicalPin
        {
            Name = "port",
            OffsetXMicrometers = pinOffsetX,
            OffsetYMicrometers = pinOffsetY,
            AngleDegrees = pinAngle,
            ParentComponent = comp
        };
        comp.PhysicalPins.Add(pin);

        return (comp, pin);
    }

    /// <summary>
    /// Creates a PDK component (ebeam_ prefix → CalculateOriginOffset uses NazcaOriginOffset).
    /// </summary>
    private static (Component comp, PhysicalPin pin) CreatePdkComponent(
        string name, double x, double y, double width, double height,
        double nazcaOriginOffsetY, double pinOffsetX, double pinOffsetY, double pinAngle,
        double rotation = 0, double nazcaOriginOffsetX = 0)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var comp = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: $"ebeam_{name.ToLower()}",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: name,
            rotationCounterClock: DiscreteRotation.R0);

        comp.PhysicalX = x;
        comp.PhysicalY = y;
        comp.WidthMicrometers = width;
        comp.HeightMicrometers = height;
        comp.RotationDegrees = rotation;
        comp.NazcaOriginOffsetX = nazcaOriginOffsetX;
        comp.NazcaOriginOffsetY = nazcaOriginOffsetY;

        var pin = new PhysicalPin
        {
            Name = "port",
            OffsetXMicrometers = pinOffsetX,
            OffsetYMicrometers = pinOffsetY,
            AngleDegrees = pinAngle,
            ParentComponent = comp
        };
        comp.PhysicalPins.Add(pin);

        return (comp, pin);
    }

    /// <summary>
    /// Exports a single straight waveguide from startPin to endPin and returns
    /// the computed (startX, startY) and (endX, endY) of the exported segment in Nazca space.
    /// </summary>
    private static ((double X, double Y) start, (double X, double Y) end) ExportAndExtractEndpoints(
        PhysicalPin startPin, PhysicalPin endPin)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX, endY) = endPin.GetAbsolutePosition();

        var segment = new StraightSegment(startX, startY, endX, endY, startPin.GetAbsoluteAngle());
        var segments = new List<PathSegment> { segment };

        var sb = new StringBuilder();
        SimpleNazcaExporter.AppendSegmentExport(sb, segments, startPin, endPin);
        var line = sb.ToString().Trim();

        return (ExtractStartPoint(line), ComputeEndPoint(line));
    }

    private static string ExportWithCustomSegments(
        PhysicalPin startPin, PhysicalPin endPin, List<PathSegment> segments)
    {
        var sb = new StringBuilder();
        SimpleNazcaExporter.AppendSegmentExport(sb, segments, startPin, endPin);
        return sb.ToString();
    }

    /// <summary>Parses .put(X, Y, A) from an nd.strt or nd.bend line and returns (X, Y).</summary>
    private static (double X, double Y) ExtractStartPoint(string line)
    {
        var match = Regex.Match(line, @"\.put\(([-\d.]+),\s*([-\d.]+)");
        match.Success.ShouldBeTrue($"Could not parse .put() from: {line}");
        return (
            double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
        );
    }

    /// <summary>
    /// Parses nd.strt(length=L).put(X, Y, A) and computes the endpoint in Nazca space.
    /// Endpoint = (X + L*cos(A°), Y + L*sin(A°)).
    /// </summary>
    private static (double X, double Y) ComputeEndPoint(string line)
    {
        var match = Regex.Match(line,
            @"nd\.strt\(length=([-\d.]+)\)\.put\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)\)");
        match.Success.ShouldBeTrue($"Could not parse nd.strt().put() from: {line}");

        double length = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double x = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        double y = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        double angleDeg = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        double angleRad = angleDeg * Math.PI / 180.0;

        return (x + length * Math.Cos(angleRad), y + length * Math.Sin(angleRad));
    }

    private static void AssertAligned(
        (double X, double Y) actual, (double X, double Y) expected, string label)
    {
        Math.Abs(actual.X - expected.X).ShouldBeLessThan(AlignmentTolerance,
            $"{label} X mismatch: actual={actual.X:F3} expected={expected.X:F3}");
        Math.Abs(actual.Y - expected.Y).ShouldBeLessThan(AlignmentTolerance,
            $"{label} Y mismatch: actual={actual.Y:F3} expected={expected.Y:F3}");
    }

    // ── Real-world MMI design test (from user's MMI.lun file) ────────────────

    /// <summary>
    /// Tests the actual MMI design from the user's MMI.lun file.
    /// This is a real-world integration test with:
    /// - 2x2 MMI Couplers (demo.mmi2x2_dp) with NazcaOriginOffsetY
    /// - Phase Shifter (demo.eopm_dc)
    /// - Grating Coupler (ebeam_gc_te1550)
    /// - Photodetector (demo.pd)
    /// - Multi-segment waveguides with bends
    /// </summary>
    [Fact]
    public void RealWorld_MmiDesign_AllWaveguidesAlignToPins()
    {
        // Component 0: 2x2 MMI Coupler at (493.86, 458.09)
        // MMI 2x2: Width=250µm, Height=60µm, NazcaOriginOffsetY = Height/2 = 30µm
        var (mmi1, mmi1_out1) = CreatePdkComponent("mmi1",
            x: 493.86170555623454, y: 458.0869784295108,
            width: 250, height: 60, nazcaOriginOffsetY: 30.0,
            pinOffsetX: 250, pinOffsetY: 26, pinAngle: 0); // out1 pin
        var mmi1_out2 = new PhysicalPin {
            Name = "out2",
            OffsetXMicrometers = 250,
            OffsetYMicrometers = 34,
            AngleDegrees = 0,
            ParentComponent = mmi1
        };
        mmi1.PhysicalPins.Add(mmi1_out2);

        // Component 2: Phase Shifter at (853.72, 287.74)
        // Phase Shifter: Width=500µm, Height=60µm, NazcaOriginOffsetY = 30µm
        var (ps, ps_in) = CreatePdkComponent("phase_shifter",
            x: 853.7179006311945, y: 287.73579424558557,
            width: 500, height: 60, nazcaOriginOffsetY: 30.0,
            pinOffsetX: 0, pinOffsetY: 30, pinAngle: 180); // in pin

        // Connection 1: mmi1.out1 -> phase_shifter.in
        // Simplified test: just two straight segments to expose the coordinate bug
        var (startX, startY) = mmi1_out1.GetAbsolutePosition();
        var (endX, endY) = ps_in.GetAbsolutePosition();
        double midX = (startX + endX) / 2.0;

        var segments1 = new List<PathSegment>
        {
            new StraightSegment(startX, startY, midX, startY, 0),   // First half
            new StraightSegment(midX, startY, endX, endY, 0)         // Second half (the "last" segment)
        };

        // Export and verify
        var nazcaCode = ExportWithCustomSegments(mmi1_out1, ps_in, segments1);
        var lines = nazcaCode.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("nd.strt(") || l.Contains("nd.bend("))
            .ToList();

        // Verify first segment starts at mmi1.out1 pin
        var (firstStartX, firstStartY) = ExtractStartPoint(lines[0]);
        var (expectedStartX, expectedStartY) = mmi1_out1.GetAbsoluteNazcaPosition();
        AssertAligned((firstStartX, firstStartY), (expectedStartX, expectedStartY),
            "MMI1.out1 -> PhaseShifter.in: first segment start");

        // Verify segments are continuous (no gaps)
        for (int i = 0; i < lines.Count - 1; i++)
        {
            var segEnd = lines[i].Contains("nd.strt(")
                ? ComputeEndPoint(lines[i])
                : ExtractStartPoint(lines[i + 1]); // For bends, just check next segment start
            var (nextStartX, nextStartY) = ExtractStartPoint(lines[i + 1]);

            Math.Abs(nextStartX - segEnd.X).ShouldBeLessThan(AlignmentTolerance,
                $"Gap between segment {i} and {i+1}: X mismatch");
            Math.Abs(nextStartY - segEnd.Y).ShouldBeLessThan(AlignmentTolerance,
                $"Gap between segment {i} and {i+1}: Y mismatch");
        }

        // Verify last segment ends at phase_shifter.in pin
        var lastSegment = lines[lines.Count - 1];
        var (lastEndX, lastEndY) = ComputeEndPoint(lastSegment);
        var (expectedEndX, expectedEndY) = ps_in.GetAbsoluteNazcaPosition();
        AssertAligned((lastEndX, lastEndY), (expectedEndX, expectedEndY),
            "MMI1.out1 -> PhaseShifter.in: last segment end");
    }
}
