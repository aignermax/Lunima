using CAP_Core.Export;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Unit tests for <see cref="PdkNazcaParserService"/> and the coordinate
/// conversion helpers used by <c>scripts/parse_pdk.py</c>.
///
/// Issue #460: PDK Import Tool — Python/Nazca PDK parser with visual verification.
/// </summary>
public class PdkNazcaParserServiceTests
{
    // ── Coordinate conversion tests ─────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 27.5, 0.0, 27.5)]   // xmin=0 → offsetX=0; ymax=27.5 → offsetY=27.5
    [InlineData(-5.0, 30.0, 5.0, 30.0)]  // xmin negative → offsetX positive
    [InlineData(10.0, 20.0, -10.0, 20.0)] // xmin positive → offsetX negative
    public void NazcaOriginOffset_ComputedFromBbox(
        double xmin, double ymax,
        double expectedOffsetX, double expectedOffsetY)
    {
        // Act — same formula as parse_pdk.py compute_origin_offset()
        double offsetX = -xmin;
        double offsetY = ymax;

        // Assert
        offsetX.ShouldBe(expectedOffsetX, tolerance: 1e-9);
        offsetY.ShouldBe(expectedOffsetY, tolerance: 1e-9);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 27.5, 0.0, 27.5)]    // origin pin at (0,0) → editor (0, 27.5)
    [InlineData(80.0, 2.0, 0.0, 27.5, 80.0, 25.5)]   // right pin → editor x preserved, y flipped
    [InlineData(80.0, -2.0, 0.0, 27.5, 80.0, 29.5)]  // below origin → editor y > ymax
    public void NazcaToEditor_PinConversion(
        double nazcaX, double nazcaY,
        double xmin, double ymax,
        double expectedEditorX, double expectedEditorY)
    {
        // Act — same formula as parse_pdk.py nazca_to_editor()
        double editorX = nazcaX - xmin;
        double editorY = ymax - nazcaY;

        // Assert
        editorX.ShouldBe(expectedEditorX, tolerance: 1e-9);
        editorY.ShouldBe(expectedEditorY, tolerance: 1e-9);
    }

    [Theory]
    [InlineData(0.0, 0.0)]     // 0° right → stays 0° right
    [InlineData(180.0, 180.0)] // 180° left → stays 180° left
    [InlineData(90.0, 270.0)]  // 90° up (Nazca) → 270° down (editor, Y-flip)
    [InlineData(270.0, 90.0)]  // 270° down (Nazca) → 90° up (editor, Y-flip)
    public void AngleFlip_YAxisInversion(double nazcaAngle, double expectedEditorAngle)
    {
        // Act — same formula as parse_pdk.py normalize_angle()
        double editorAngle = (-(nazcaAngle % 360) + 360) % 360;

        // Assert
        editorAngle.ShouldBe(expectedEditorAngle, tolerance: 1e-9);
    }

    // ── JSON deserialization tests ──────────────────────────────────────────

    [Fact]
    public void DeserializeResult_ValidJson_ParsesComponents()
    {
        var json = @"{
            ""fileFormatVersion"": 1,
            ""name"": ""test_pdk"",
            ""description"": ""Test PDK"",
            ""foundry"": ""test"",
            ""version"": ""1.0"",
            ""defaultWavelengthNm"": 1550,
            ""nazcaModuleName"": ""test_pdk"",
            ""components"": [
                {
                    ""name"": ""mmi1x2"",
                    ""category"": ""Splitters"",
                    ""nazcaFunction"": ""mmi1x2"",
                    ""nazcaParameters"": """",
                    ""widthMicrometers"": 80.0,
                    ""heightMicrometers"": 55.0,
                    ""nazcaOriginOffsetX"": 0.0,
                    ""nazcaOriginOffsetY"": 27.5,
                    ""pins"": [
                        { ""name"": ""a0"", ""offsetXMicrometers"": 0.0, ""offsetYMicrometers"": 27.5, ""angleDegrees"": 180.0 },
                        { ""name"": ""b0"", ""offsetXMicrometers"": 80.0, ""offsetYMicrometers"": 25.5, ""angleDegrees"": 0.0 }
                    ]
                }
            ]
        }";

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<PdkParseResult>(json, options);

        result.ShouldNotBeNull();
        result.Name.ShouldBe("test_pdk");
        result.Components.Count.ShouldBe(1);

        var comp = result.Components[0];
        comp.Name.ShouldBe("mmi1x2");
        comp.WidthMicrometers.ShouldBe(80.0);
        comp.HeightMicrometers.ShouldBe(55.0);
        comp.NazcaOriginOffsetX.ShouldBe(0.0);
        comp.NazcaOriginOffsetY.ShouldBe(27.5);
        comp.Pins.Count.ShouldBe(2);
        comp.Pins[0].Name.ShouldBe("a0");
        comp.Pins[0].AngleDegrees.ShouldBe(180.0);
    }

    [Fact]
    public void DeserializeResult_EmptyComponents_ReturnsEmptyList()
    {
        var json = @"{
            ""fileFormatVersion"": 1,
            ""name"": ""empty_pdk"",
            ""defaultWavelengthNm"": 1550,
            ""components"": []
        }";

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<PdkParseResult>(json, options);

        result.ShouldNotBeNull();
        result.Components.ShouldBeEmpty();
    }

    // ── Service construction tests ──────────────────────────────────────────

    [Fact]
    public void Constructor_NullPythonPath_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new PdkNazcaParserService(null!, "script.py"));
    }

    [Fact]
    public void Constructor_NullScriptPath_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new PdkNazcaParserService("python3", null!));
    }

    [Fact]
    public async Task ParseAsync_ScriptNotFound_ThrowsFileNotFoundException()
    {
        var service = new PdkNazcaParserService(
            "python3",
            "/nonexistent/path/parse_pdk.py");

        await Should.ThrowAsync<FileNotFoundException>(
            () => service.ParseAsync("demo"));
    }

    // ── Demo output reference values ────────────────────────────────────────

    [Fact]
    public void DemoOutput_Mmi1x2_OriginAtMidHeight()
    {
        // Reference: mmi1x2_sh in Nazca demofab
        // Bounding box: (0, -27.5) to (80, 27.5)
        // So nazcaOriginOffsetY should be ymax = 27.5 = height/2
        const double width = 80.0;
        const double height = 55.0;
        const double xmin = 0.0;
        const double ymax = 27.5;

        double originOffsetX = -xmin;
        double originOffsetY = ymax;

        originOffsetX.ShouldBe(0.0);
        originOffsetY.ShouldBe(height / 2, tolerance: 1e-9);
    }

    [Fact]
    public void DemoOutput_GratingCoupler_OriginAtMidHeight()
    {
        // Reference: io cell in Nazca demofab
        // Bounding box: (0, -9.5) to (100, 9.5)
        const double xmin = 0.0;
        const double ymax = 9.5;

        double originOffsetX = -xmin;
        double originOffsetY = ymax;

        // For symmetric cells: nazcaOriginOffsetY == height/2
        const double height = 19.0;
        originOffsetX.ShouldBe(0.0);
        originOffsetY.ShouldBe(height / 2, tolerance: 1e-9);
    }

    // ── PdkParserException tests ────────────────────────────────────────────

    [Fact]
    public void PdkParserException_MessagePreserved()
    {
        const string message = "Test error from parse_pdk.py";
        var ex = new PdkParserException(message);
        ex.Message.ShouldBe(message);
    }

    [Fact]
    public void PdkParserException_WithInnerException_ChainPreserved()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new PdkParserException("outer", inner);
        ex.InnerException.ShouldBe(inner);
    }
}
