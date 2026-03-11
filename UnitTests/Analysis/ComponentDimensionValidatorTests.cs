using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Unit tests for ComponentDimensionValidator.
/// Validates that component dimensions correctly encompass all physical pins.
/// </summary>
public class ComponentDimensionValidatorTests
{
    private readonly ComponentDimensionValidator _validator = new();

    [Fact]
    public void Validate_ComponentWithNoPins_ReturnsValid()
    {
        // Arrange
        var component = CreateComponent(width: 100, height: 50, pins: Array.Empty<PhysicalPin>());

        // Act
        var result = _validator.Validate(component);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.ComponentName.ShouldBe("TestComponent");
    }

    [Fact]
    public void Validate_ComponentWithPinsWithinBounds_ReturnsValid()
    {
        // Arrange
        var pins = new[]
        {
            CreatePin("in", 0, 25),
            CreatePin("out", 100, 25)
        };
        var component = CreateComponent(width: 120, height: 50, pins: pins);

        // Act
        var result = _validator.Validate(component);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issue.ShouldBeNull();
    }

    [Fact]
    public void Validate_ComponentTooNarrow_ReturnsInvalid()
    {
        // Arrange - pins extend to x=120, but component width is only 100
        var pins = new[]
        {
            CreatePin("in", 0, 25),
            CreatePin("out", 120, 25)
        };
        var component = CreateComponent(width: 100, height: 50, pins: pins);

        // Act
        var result = _validator.Validate(component);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issue.ShouldContain("Width");
        result.Issue.ShouldContain("too small");
        result.RecommendedWidth.ShouldBeGreaterThan(120);
    }

    [Fact]
    public void Validate_ComponentTooShort_ReturnsInvalid()
    {
        // Arrange - pins extend to y=50, but component height is only 30
        var pins = new[]
        {
            CreatePin("a0", 10, 0),
            CreatePin("a1", 10, 50)
        };
        var component = CreateComponent(width: 100, height: 30, pins: pins);

        // Act
        var result = _validator.Validate(component);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issue.ShouldContain("Height");
        result.Issue.ShouldContain("too small");
        result.RecommendedHeight.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void Validate_MmiLikeComponent_DetectsIssue()
    {
        // Arrange - simulates the MMI 2x2 issue from demo-pdk.json
        // Component claims 120x50, but for proper GDS should be larger to encompass pins
        var pins = new[]
        {
            CreatePin("a0", 0, 12.5),
            CreatePin("a1", 0, 37.5),
            CreatePin("b0", 120, 12.5),
            CreatePin("b1", 120, 37.5)
        };
        var component = CreateComponent(width: 120, height: 50, pins: pins);

        // Act
        var result = _validator.Validate(component);

        // Assert - With 5µm margins, this should require 130×48 (pins span 0-120 in X, 12.5-37.5=25 in Y)
        result.ComponentName.ShouldBe("TestComponent");
        result.CurrentWidth.ShouldBe(120);
        result.CurrentHeight.ShouldBe(50);
        result.RecommendedWidth.ShouldBeGreaterThanOrEqualTo(130); // 120 + 2*5
        result.IsValid.ShouldBeFalse(); // Width is too small
    }

    [Fact]
    public void CalculatePinBoundingBox_ReturnsCorrectBounds()
    {
        // Arrange
        var pins = new List<PhysicalPin>
        {
            CreatePin("a", 10, 20),
            CreatePin("b", 100, 80),
            CreatePin("c", 50, 50)
        };

        // Act
        var bbox = _validator.CalculatePinBoundingBox(pins);

        // Assert
        bbox.MinX.ShouldBe(10);
        bbox.MaxX.ShouldBe(100);
        bbox.MinY.ShouldBe(20);
        bbox.MaxY.ShouldBe(80);
    }

    [Fact]
    public void CalculatePinBoundingBox_EmptyList_ReturnsZeroBounds()
    {
        // Arrange
        var pins = new List<PhysicalPin>();

        // Act
        var bbox = _validator.CalculatePinBoundingBox(pins);

        // Assert
        bbox.MinX.ShouldBe(0);
        bbox.MaxX.ShouldBe(0);
        bbox.MinY.ShouldBe(0);
        bbox.MaxY.ShouldBe(0);
    }

    [Fact]
    public void ValidateAll_FiltersOnlyInvalidComponents()
    {
        // Arrange
        var components = new[]
        {
            CreateComponent(width: 120, height: 50, pins: new[]
            {
                CreatePin("in", 0, 25),
                CreatePin("out", 100, 25)
            }),
            CreateComponent(width: 100, height: 50, pins: new[]
            {
                CreatePin("in", 0, 25),
                CreatePin("out", 120, 25) // Too wide!
            }),
            CreateComponent(width: 120, height: 50, pins: Array.Empty<PhysicalPin>())
        };

        // Act
        var results = _validator.ValidateAll(components);

        // Assert
        results.Count.ShouldBe(1);
        results[0].IsValid.ShouldBeFalse();
    }

    private static Component CreateComponent(double width, double height, PhysicalPin[] pins)
    {
        var parts = new Part[1, 1];
        var logicalPins = new List<Pin>();
        parts[0, 0] = new Part(logicalPins);

        var sMatrix = new SMatrix(new List<Guid>(), new List<(Guid, double)>());
        var wavelengthMap = new Dictionary<int, SMatrix> { { 1550, sMatrix } };

        var component = new Component(
            wavelengthMap,
            new List<Slider>(),
            "test_component",
            "",
            parts,
            0,
            "TestComponent",
            DiscreteRotation.R0,
            pins.ToList());

        component.WidthMicrometers = width;
        component.HeightMicrometers = height;

        return component;
    }

    private static PhysicalPin CreatePin(string name, double x, double y)
    {
        return new PhysicalPin
        {
            Name = name,
            OffsetXMicrometers = x,
            OffsetYMicrometers = y,
            AngleDegrees = 0
        };
    }
}
