using CAP_Core.Components.Core;
using CAP_Core.Coordinates;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;

namespace UnitTests.Coordinates;

/// <summary>
/// Tests for <see cref="CoordinateTranslationService"/> covering all coordinate transformations.
/// </summary>
public class CoordinateTranslationServiceTests
{
    private readonly CoordinateTranslationService _sut = new();

    // ===== Helpers =====

    private static Component CreateComponent(
        double physicalX = 0, double physicalY = 0,
        double widthMicrons = 10, double heightMicrons = 5,
        double rotationDegrees = 0,
        double nazcaOriginOffsetX = 0, double nazcaOriginOffsetY = 0,
        string nazcaFunctionName = "demo_component",
        string nazcaFunctionParameters = "")
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        var comp = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            nazcaFunctionName,
            nazcaFunctionParameters,
            parts,
            typeNumber: 0,
            identifier: "TestComp",
            rotationCounterClock: DiscreteRotation.R0);

        comp.PhysicalX = physicalX;
        comp.PhysicalY = physicalY;
        comp.WidthMicrometers = widthMicrons;
        comp.HeightMicrometers = heightMicrons;
        comp.RotationDegrees = rotationDegrees;
        comp.NazcaOriginOffsetX = nazcaOriginOffsetX;
        comp.NazcaOriginOffsetY = nazcaOriginOffsetY;
        return comp;
    }

    private static PhysicalPin CreatePin(Component parent, double offsetX, double offsetY)
    {
        var pin = new PhysicalPin
        {
            Name = "test_pin",
            ParentComponent = parent,
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = 0
        };
        parent.PhysicalPins.Add(pin);
        return pin;
    }

    // ===== RotatePoint =====

    [Fact]
    public void RotatePoint_ZeroDegrees_ReturnsOriginalPoint()
    {
        var (x, y) = _sut.RotatePoint(3.0, 4.0, 0);

        x.ShouldBe(3.0, tolerance: 1e-10);
        y.ShouldBe(4.0, tolerance: 1e-10);
    }

    [Fact]
    public void RotatePoint_90Degrees_RotatesCounterClockwise()
    {
        var (x, y) = _sut.RotatePoint(1.0, 0.0, 90);

        x.ShouldBe(0.0, tolerance: 1e-10);
        y.ShouldBe(1.0, tolerance: 1e-10);
    }

    [Fact]
    public void RotatePoint_180Degrees_NegatesBothAxes()
    {
        var (x, y) = _sut.RotatePoint(3.0, 4.0, 180);

        x.ShouldBe(-3.0, tolerance: 1e-10);
        y.ShouldBe(-4.0, tolerance: 1e-10);
    }

    [Fact]
    public void RotatePoint_360Degrees_ReturnsOriginalPoint()
    {
        var (x, y) = _sut.RotatePoint(5.0, 2.0, 360);

        x.ShouldBe(5.0, tolerance: 1e-10);
        y.ShouldBe(2.0, tolerance: 1e-10);
    }

    // ===== IsPdkFunction =====

    [Theory]
    [InlineData("ebeam_y_1550")]
    [InlineData("ebeam_dc_te1550")]
    [InlineData("GC_te1550")]
    [InlineData("ANT_connector")]
    [InlineData("crossing_wg")]
    [InlineData("taper_linear")]
    [InlineData("siepic.gc_te1550")]
    public void IsPdkFunction_KnownPdkNames_ReturnsTrue(string funcName)
    {
        CoordinateTranslationService.IsPdkFunction(funcName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("demo_pdk.mmi1x2")]
    [InlineData("grating_coupler")]
    [InlineData("phase_shifter")]
    [InlineData("splitter_1x2")]
    public void IsPdkFunction_NonPdkNames_ReturnsFalse(string funcName)
    {
        CoordinateTranslationService.IsPdkFunction(funcName).ShouldBeFalse();
    }

    // ===== IsParametricStraight =====

    [Fact]
    public void IsParametricStraight_WithLengthAndStraightKeyword_ReturnsTrue()
    {
        CoordinateTranslationService.IsParametricStraight("wg_straight", "length=100").ShouldBeTrue();
    }

    [Fact]
    public void IsParametricStraight_WithLengthAndStrtKeyword_ReturnsTrue()
    {
        CoordinateTranslationService.IsParametricStraight("wg_strt", "length=50").ShouldBeTrue();
    }

    [Fact]
    public void IsParametricStraight_WithoutLength_ReturnsFalse()
    {
        CoordinateTranslationService.IsParametricStraight("wg_straight", "width=0.45").ShouldBeFalse();
    }

    [Fact]
    public void IsParametricStraight_NullParameters_ReturnsFalse()
    {
        CoordinateTranslationService.IsParametricStraight("wg_straight", null).ShouldBeFalse();
    }

    // ===== CalculateNazcaOriginOffset =====

    [Fact]
    public void CalculateNazcaOriginOffset_LegacyComponent_ReturnsHeightBasedOffset()
    {
        var comp = CreateComponent(heightMicrons: 7.5);

        var (offsetX, offsetY) = _sut.CalculateNazcaOriginOffset(comp);

        offsetX.ShouldBe(0.0);
        offsetY.ShouldBe(7.5);
    }

    [Fact]
    public void CalculateNazcaOriginOffset_WithExplicitOriginOffset_ReturnsThatOffset()
    {
        var comp = CreateComponent(nazcaOriginOffsetX: 3.0, nazcaOriginOffsetY: 4.0);

        var (offsetX, offsetY) = _sut.CalculateNazcaOriginOffset(comp);

        offsetX.ShouldBe(3.0, tolerance: 1e-10);
        offsetY.ShouldBe(4.0, tolerance: 1e-10);
    }

    [Fact]
    public void CalculateNazcaOriginOffset_PdkFunctionNoExplicitOffset_ReturnsZeroOffset()
    {
        var comp = CreateComponent(nazcaFunctionName: "ebeam_y_1550");

        var (offsetX, offsetY) = _sut.CalculateNazcaOriginOffset(comp);

        offsetX.ShouldBe(0.0);
        offsetY.ShouldBe(0.0);
    }

    [Fact]
    public void CalculateNazcaOriginOffset_PdkFunctionWithRotation_RotatesOffset()
    {
        var comp = CreateComponent(
            nazcaFunctionName: "ebeam_y_1550",
            nazcaOriginOffsetX: 5.0,
            nazcaOriginOffsetY: 0.0,
            rotationDegrees: 90);

        var (offsetX, offsetY) = _sut.CalculateNazcaOriginOffset(comp);

        // Rotating (5,0) by 90° → (0, 5)
        offsetX.ShouldBe(0.0, tolerance: 1e-10);
        offsetY.ShouldBe(5.0, tolerance: 1e-10);
    }

    // ===== ComponentToNazca =====

    [Fact]
    public void ComponentToNazca_BasicLegacyComponent_FlipsYAxis()
    {
        var comp = CreateComponent(physicalX: 10, physicalY: 20, heightMicrons: 0);

        var (nazcaX, nazcaY, _) = _sut.ComponentToNazca(comp);

        nazcaX.ShouldBe(10.0);
        nazcaY.ShouldBe(-20.0);
    }

    [Fact]
    public void ComponentToNazca_RotationIsNegated()
    {
        var comp = CreateComponent(rotationDegrees: 45);

        var (_, _, nazcaRot) = _sut.ComponentToNazca(comp);

        nazcaRot.ShouldBe(-45.0);
    }

    [Fact]
    public void ComponentToNazca_WithExplicitOriginOffset_IncludesOffsetInPosition()
    {
        var comp = CreateComponent(physicalX: 0, physicalY: 0, nazcaOriginOffsetX: 2.0, nazcaOriginOffsetY: 3.0);

        var (nazcaX, nazcaY, _) = _sut.ComponentToNazca(comp);

        nazcaX.ShouldBe(2.0, tolerance: 1e-10);
        nazcaY.ShouldBe(-3.0, tolerance: 1e-10);
    }

    // ===== GetPinNazcaPosition =====

    [Fact]
    public void GetPinNazcaPosition_NoRotationNoOffset_BasicTransform()
    {
        // Component at (10,20), height=5, pin at offset (0, 0)
        // Expected: nazcaCompX = 10+0=10, nazcaCompY = -(20+5)=-25
        // localPinX = 0-0=0, localPinY = (5-0)-5=0
        // Result = (10, -25)
        var comp = CreateComponent(physicalX: 10, physicalY: 20, heightMicrons: 5);
        var pin = CreatePin(comp, offsetX: 0, offsetY: 0);

        var (x, y) = _sut.GetPinNazcaPosition(pin);

        x.ShouldBe(10.0, tolerance: 1e-10);
        y.ShouldBe(-25.0, tolerance: 1e-10);
    }

    [Fact]
    public void GetPinNazcaPosition_PinAtBottomCenter_CorrectNazcaCoords()
    {
        // Component at (0,0), height=10, pin at offset (5, 10) (bottom center)
        // With legacy offset (0, 10): nazcaCompX=0, nazcaCompY=-(0+10)=-10
        // localPinX = 5-0=5, localPinY = (10-10)-10=-10
        // No rotation
        // Result = (5, -20)
        var comp = CreateComponent(physicalX: 0, physicalY: 0, heightMicrons: 10);
        var pin = CreatePin(comp, offsetX: 5, offsetY: 10);

        var (x, y) = _sut.GetPinNazcaPosition(pin);

        x.ShouldBe(5.0, tolerance: 1e-10);
        y.ShouldBe(-20.0, tolerance: 1e-10);
    }

    [Fact]
    public void GetPinNazcaPosition_PinAtTopCenter_CorrectNazcaCoords()
    {
        // Component at (0,0), height=10, pin at offset (5, 0) (top center in editor)
        // With legacy offset (0, 10): nazcaCompX=0, nazcaCompY=-10
        // localPinX = 5, localPinY = (10-0)-10=0
        // Result = (5, -10)
        var comp = CreateComponent(physicalX: 0, physicalY: 0, heightMicrons: 10);
        var pin = CreatePin(comp, offsetX: 5, offsetY: 0);

        var (x, y) = _sut.GetPinNazcaPosition(pin);

        x.ShouldBe(5.0, tolerance: 1e-10);
        y.ShouldBe(-10.0, tolerance: 1e-10);
    }

    [Fact]
    public void GetPinNazcaPosition_WithPdkOriginOffset_UsesExplicitOffset()
    {
        // Component at (10,20), height=5, ebeam PDK, NazcaOriginOffset=(2,3)
        // offset = (2,3), nazcaCompX = 10+2=12, nazcaCompY = -(20+3)=-23
        // localPinX = 0-2=-2, localPinY = (5-0)-3=2
        // No rotation → result = (12-2, -23+2) = (10, -21)
        var comp = CreateComponent(
            physicalX: 10, physicalY: 20, heightMicrons: 5,
            nazcaFunctionName: "ebeam_y_1550",
            nazcaOriginOffsetX: 2.0, nazcaOriginOffsetY: 3.0);
        var pin = CreatePin(comp, offsetX: 0, offsetY: 0);

        var (x, y) = _sut.GetPinNazcaPosition(pin);

        x.ShouldBe(10.0, tolerance: 1e-10);
        y.ShouldBe(-21.0, tolerance: 1e-10);
    }

    [Fact]
    public void GetPinNazcaPosition_With90DegreeRotation_AppliesNegatedRotation()
    {
        // Component at (0,0), height=5, NazcaOriginOffset=(0,0) (legacy: offset=(0,5))
        // nazcaCompX=0, nazcaCompY=-5
        // pin at (0,0): localPinX=0, localPinY=(5-0)-5=0
        // Rotation=90 → negated=-90: rotate (0,0) → (0,0)
        // Result = (0, -5)
        var comp = CreateComponent(physicalX: 0, physicalY: 0, heightMicrons: 5, rotationDegrees: 90);
        var pin = CreatePin(comp, offsetX: 0, offsetY: 0);

        var (x, y) = _sut.GetPinNazcaPosition(pin);

        x.ShouldBe(0.0, tolerance: 1e-10);
        y.ShouldBe(-5.0, tolerance: 1e-10);
    }
}
