using CAP.Avalonia.Services;
using CAP_Core.CodeExporter;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Ground-truth tests for GDS export coordinate correctness (Issue #332).
///
/// These tests establish a deterministic baseline by comparing
/// <c>SimpleNazcaExporter</c> output against the explicit coordinates
/// defined in <c>NazcaReferenceGenerator</c> and mirrored in
/// <c>scripts/generate_reference_nazca.py</c>.
///
/// Test strategy:
///   1. Create reference design via <c>GdsTestDesigns</c>.
///   2. Export to Nazca Python via <c>SimpleNazcaExporter</c>.
///   3. Parse the script with <c>NazcaCodeParser</c>.
///   4. Assert that component positions and waveguide coordinates match
///      the constants in <c>NazcaReferenceGenerator</c> exactly.
///
/// If a test fails, the deviation reported is the exact offset to investigate
/// (see Issue #329 — waveguides off by micrometers).
/// </summary>
public class GdsGroundTruthTests
{
    private const double PositionTolerance = 0.01; // µm

    private readonly SimpleNazcaExporter _exporter = new();
    private readonly NazcaCodeParser     _parser   = new();
    private readonly NazcaReferenceGenerator _reference = new();

    // ── Component position tests ───────────────────────────────────────────

    /// <summary>
    /// Verifies that component 1 is placed at the expected Nazca coordinates.
    /// </summary>
    [Fact]
    public void ReferenceDesign_Component1_ExportedAtExpectedNazcaPosition()
    {
        var (canvas, _) = GdsTestDesigns.CreateReferenceDesign();

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        parsed.Components.Count.ShouldBeGreaterThanOrEqualTo(2,
            "Reference design must contain at least 2 components");

        var comp1 = parsed.Components[0];
        comp1.X.ShouldBe(NazcaReferenceGenerator.Component1X,
            PositionTolerance,
            $"Component 1 Nazca X mismatch (expected {NazcaReferenceGenerator.Component1X}, got {comp1.X})");

        comp1.Y.ShouldBe(NazcaReferenceGenerator.NazcaComp1Y,
            PositionTolerance,
            $"Component 1 Nazca Y mismatch (expected {NazcaReferenceGenerator.NazcaComp1Y}, got {comp1.Y})");
    }

    /// <summary>
    /// Verifies that component 2 is placed at the expected Nazca coordinates.
    /// </summary>
    [Fact]
    public void ReferenceDesign_Component2_ExportedAtExpectedNazcaPosition()
    {
        var (canvas, _) = GdsTestDesigns.CreateReferenceDesign();

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        parsed.Components.Count.ShouldBeGreaterThanOrEqualTo(2);

        var comp2 = parsed.Components[1];
        comp2.X.ShouldBe(NazcaReferenceGenerator.Component2X,
            PositionTolerance,
            $"Component 2 Nazca X mismatch (expected {NazcaReferenceGenerator.Component2X}, got {comp2.X})");

        comp2.Y.ShouldBe(NazcaReferenceGenerator.NazcaComp2Y,
            PositionTolerance,
            $"Component 2 Nazca Y mismatch (expected {NazcaReferenceGenerator.NazcaComp2Y}, got {comp2.Y})");
    }

    // ── Waveguide position tests ───────────────────────────────────────────

    /// <summary>
    /// Verifies that the waveguide starts at the correct Nazca coordinates.
    /// This is the key test for Issue #329 — waveguide-to-pin alignment.
    /// </summary>
    [Fact]
    public void ReferenceDesign_Waveguide_StartPointMatchesPinPosition()
    {
        var (canvas, _) = GdsTestDesigns.CreateReferenceDesign();

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        parsed.WaveguideStubs.Count.ShouldBeGreaterThanOrEqualTo(1,
            "Reference design must contain at least one waveguide segment");

        var wg = parsed.WaveguideStubs[0];
        wg.StartX.ShouldBe(NazcaReferenceGenerator.NazcaWgStartX,
            PositionTolerance,
            $"Waveguide start X mismatch — offset = {wg.StartX - NazcaReferenceGenerator.NazcaWgStartX:F3} µm");

        wg.StartY.ShouldBe(NazcaReferenceGenerator.NazcaWgStartY,
            PositionTolerance,
            $"Waveguide start Y mismatch — offset = {wg.StartY - NazcaReferenceGenerator.NazcaWgStartY:F3} µm");
    }

    /// <summary>
    /// Verifies that the waveguide length is exactly 200 µm.
    /// </summary>
    [Fact]
    public void ReferenceDesign_Waveguide_LengthIsCorrect()
    {
        var (canvas, _) = GdsTestDesigns.CreateReferenceDesign();

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        parsed.WaveguideStubs.Count.ShouldBeGreaterThanOrEqualTo(1);

        var wg = parsed.WaveguideStubs[0];
        wg.Length.ShouldBe(NazcaReferenceGenerator.WaveguideLength,
            PositionTolerance,
            $"Waveguide length mismatch (expected {NazcaReferenceGenerator.WaveguideLength}, got {wg.Length})");
    }

    // ── Script content tests ───────────────────────────────────────────────

    /// <summary>
    /// Verifies the generated Python script contains the correct design cell name.
    /// </summary>
    [Fact]
    public void ReferenceDesign_ExportedScript_ContainsDesignCell()
    {
        var (canvas, _) = GdsTestDesigns.CreateReferenceDesign();
        var script = _exporter.Export(canvas);

        script.ShouldContain("ConnectAPIC_Design");
        script.ShouldContain("nd.export_gds");
    }

    /// <summary>
    /// Verifies that the exported script contains the waveguide length (200 µm).
    /// This confirms the waveguide connection was created and exported.
    /// </summary>
    [Fact]
    public void ReferenceDesign_ExportedScript_ContainsWaveguideLength()
    {
        var (canvas, _) = GdsTestDesigns.CreateReferenceDesign();
        var script = _exporter.Export(canvas);

        // The 200 µm straight waveguide must appear
        script.ShouldContain("length=200.00");
    }

    // ── NazcaReferenceGenerator unit tests ────────────────────────────────

    /// <summary>
    /// Verifies that NazcaReferenceGenerator returns the correct expected physical coordinates.
    /// </summary>
    [Fact]
    public void NazcaReferenceGenerator_GetExpectedCoordinates_ReturnsCorrectValues()
    {
        var coords = _reference.GetExpectedCoordinates();

        coords["comp1_position"].X.ShouldBe(0.0,   PositionTolerance);
        coords["comp1_position"].Y.ShouldBe(0.0,   PositionTolerance);
        coords["comp1_pin_out"].X.ShouldBe(100.0,  PositionTolerance);
        coords["comp1_pin_out"].Y.ShouldBe(25.0,   PositionTolerance);
        coords["comp1_pin_in"].X.ShouldBe(0.0,     PositionTolerance);
        coords["comp2_position"].X.ShouldBe(300.0, PositionTolerance);
        coords["waveguide_start"].X.ShouldBe(100.0, PositionTolerance);
        coords["waveguide_start"].Y.ShouldBe(25.0,  PositionTolerance);
        coords["waveguide_end"].X.ShouldBe(300.0,   PositionTolerance);
        coords["waveguide_end"].Y.ShouldBe(25.0,    PositionTolerance);
    }

    /// <summary>
    /// Verifies that NazcaReferenceGenerator Nazca coordinates use the Y-flip convention
    /// and account for NazcaOriginOffset.
    /// </summary>
    [Fact]
    public void NazcaReferenceGenerator_GetExpectedNazcaCoordinates_YFlipped()
    {
        var coords = _reference.GetExpectedNazcaCoordinates();

        coords["comp1_nazca"].X.ShouldBe(0.0,    PositionTolerance);
        coords["comp1_nazca"].Y.ShouldBe(-50.0,  PositionTolerance);
        coords["comp2_nazca"].X.ShouldBe(300.0,  PositionTolerance);
        coords["comp2_nazca"].Y.ShouldBe(-50.0,  PositionTolerance);
        coords["wg_start_nazca"].X.ShouldBe(100.0,  PositionTolerance);
        coords["wg_start_nazca"].Y.ShouldBe(-75.0,  PositionTolerance); // -(0 + 50 + 25) with NazcaOriginOffset
    }

    /// <summary>
    /// Verifies that the generated Python script content contains correct Nazca placements.
    /// </summary>
    [Fact]
    public void NazcaReferenceGenerator_GeneratePythonScript_ContainsExpectedPlacements()
    {
        var script = _reference.GeneratePythonScript("/tmp/ref.gds", "/tmp/ref.json");

        // Component placements at Nazca coordinates
        script.ShouldContain("0.00, -50.00, 0");   // comp1
        script.ShouldContain("300.00, -50.00, 0"); // comp2

        // Waveguide start (with NazcaOriginOffset accounted for)
        script.ShouldContain("100.00, -75.00, 0"); // wg start

        // Waveguide length
        script.ShouldContain("200.00"); // wg length
    }

    /// <summary>
    /// Verifies that NazcaReferenceGenerator WaveguideLength constant is correct.
    /// </summary>
    [Fact]
    public void NazcaReferenceGenerator_WaveguideLength_IsGapBetweenComponents()
    {
        // Gap = Component2X - ComponentWidth = 300 - 100 = 200
        NazcaReferenceGenerator.WaveguideLength
            .ShouldBe(
                NazcaReferenceGenerator.Component2X - NazcaReferenceGenerator.ComponentWidth,
                PositionTolerance);

        NazcaReferenceGenerator.WaveguideLength.ShouldBe(200.0, PositionTolerance);
    }
}
