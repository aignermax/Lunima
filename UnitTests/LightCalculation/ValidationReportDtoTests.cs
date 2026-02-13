using CAP_Core.LightCalculation.Validation;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation;

/// <summary>
/// Tests for the ToReportDto conversion on SMatrixValidationResult.
/// </summary>
public class ValidationReportDtoTests
{
    [Fact]
    public void ToReportDto_EmptyResult_MapsCorrectly()
    {
        var result = new SMatrixValidationResult();

        var dto = result.ToReportDto();

        dto.IsValid.ShouldBeTrue();
        dto.HasWarnings.ShouldBeFalse();
        dto.ErrorCount.ShouldBe(0);
        dto.WarningCount.ShouldBe(0);
        dto.Errors.ShouldBeEmpty();
        dto.Warnings.ShouldBeEmpty();
        dto.WavelengthNm.ShouldBeNull();
        dto.Timestamp.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ToReportDto_WithWavelength_IncludesWavelength()
    {
        var result = new SMatrixValidationResult();

        var dto = result.ToReportDto(wavelengthNm: 1550);

        dto.WavelengthNm.ShouldBe(1550);
    }

    [Fact]
    public void ToReportDto_WithErrors_MapsErrorEntries()
    {
        var result = new SMatrixValidationResult();
        result.AddError("Error A");
        result.AddError("Error B");

        var dto = result.ToReportDto();

        dto.IsValid.ShouldBeFalse();
        dto.ErrorCount.ShouldBe(2);
        dto.Errors.Count.ShouldBe(2);
        dto.Errors[0].Severity.ShouldBe("Error");
        dto.Errors[0].Message.ShouldBe("Error A");
        dto.Errors[1].Message.ShouldBe("Error B");
    }

    [Fact]
    public void ToReportDto_WithWarnings_MapsWarningEntries()
    {
        var result = new SMatrixValidationResult();
        result.AddWarning("Warning X");

        var dto = result.ToReportDto();

        dto.IsValid.ShouldBeTrue();
        dto.HasWarnings.ShouldBeTrue();
        dto.WarningCount.ShouldBe(1);
        dto.Warnings.Count.ShouldBe(1);
        dto.Warnings[0].Severity.ShouldBe("Warning");
        dto.Warnings[0].Message.ShouldBe("Warning X");
    }

    [Fact]
    public void ToReportDto_Timestamp_IsValidIso8601()
    {
        var result = new SMatrixValidationResult();

        var dto = result.ToReportDto();

        // Parse as round-trip format ("o")
        DateTimeOffset.TryParse(dto.Timestamp, out var parsed).ShouldBeTrue();
        parsed.ShouldBeGreaterThan(DateTimeOffset.MinValue);
    }
}
