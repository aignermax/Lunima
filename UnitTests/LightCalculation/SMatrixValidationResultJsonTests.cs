using System.Text.Json;
using CAP_Core.LightCalculation.Validation;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation;

/// <summary>
/// Tests for JSON serialization of S-Matrix validation results.
/// </summary>
public class SMatrixValidationResultJsonTests
{
    [Fact]
    public void ToJson_EmptyResult_ProducesValidJson()
    {
        var result = new SMatrixValidationResult();

        var json = result.ToJson();

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("isValid").GetBoolean().ShouldBeTrue();
        root.GetProperty("hasWarnings").GetBoolean().ShouldBeFalse();
        root.GetProperty("errorCount").GetInt32().ShouldBe(0);
        root.GetProperty("warningCount").GetInt32().ShouldBe(0);
        root.GetProperty("errors").GetArrayLength().ShouldBe(0);
        root.GetProperty("warnings").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void ToJson_WithErrors_IncludesErrorEntries()
    {
        var result = new SMatrixValidationResult();
        result.AddError("Matrix is not square: 2x3.");
        result.AddError("Element [0,1] contains NaN or Infinity.");

        var json = result.ToJson();

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("isValid").GetBoolean().ShouldBeFalse();
        root.GetProperty("errorCount").GetInt32().ShouldBe(2);
        var errors = root.GetProperty("errors");
        errors.GetArrayLength().ShouldBe(2);
        errors[0].GetProperty("severity").GetString().ShouldBe("Error");
        errors[0].GetProperty("message").GetString().ShouldContain("not square");
    }

    [Fact]
    public void ToJson_WithWarnings_IncludesWarningEntries()
    {
        var result = new SMatrixValidationResult();
        result.AddWarning("Column 0: energy conservation violation.");

        var json = result.ToJson();

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("isValid").GetBoolean().ShouldBeTrue();
        root.GetProperty("hasWarnings").GetBoolean().ShouldBeTrue();
        root.GetProperty("warningCount").GetInt32().ShouldBe(1);
        var warnings = root.GetProperty("warnings");
        warnings.GetArrayLength().ShouldBe(1);
        warnings[0].GetProperty("severity").GetString().ShouldBe("Warning");
        warnings[0].GetProperty("message").GetString()
            .ShouldContain("energy conservation");
    }

    [Fact]
    public void ToJson_WithWavelength_IncludesWavelength()
    {
        var result = new SMatrixValidationResult();

        var json = result.ToJson(wavelengthNm: 1550);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("wavelengthNm").GetInt32().ShouldBe(1550);
    }

    [Fact]
    public void ToJson_WithoutWavelength_WavelengthIsNull()
    {
        var result = new SMatrixValidationResult();

        var json = result.ToJson();

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("wavelengthNm").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void ToJson_IncludesTimestamp()
    {
        var result = new SMatrixValidationResult();

        var json = result.ToJson();

        var doc = JsonDocument.Parse(json);
        var timestamp = doc.RootElement.GetProperty("timestamp").GetString();
        timestamp.ShouldNotBeNullOrWhiteSpace();
        // Should be parseable as ISO 8601 datetime
        DateTime.Parse(timestamp).ShouldBeGreaterThan(DateTime.MinValue);
    }

    [Fact]
    public void ToJson_MixedErrorsAndWarnings_SeparatesCorrectly()
    {
        var result = new SMatrixValidationResult();
        result.AddError("Error 1");
        result.AddWarning("Warning 1");
        result.AddError("Error 2");
        result.AddWarning("Warning 2");

        var json = result.ToJson();

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("errorCount").GetInt32().ShouldBe(2);
        root.GetProperty("warningCount").GetInt32().ShouldBe(2);
        root.GetProperty("errors").GetArrayLength().ShouldBe(2);
        root.GetProperty("warnings").GetArrayLength().ShouldBe(2);
    }
}
