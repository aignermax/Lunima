using System.Text.Json;
using CAP_Core.LightCalculation.Validation;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation;

/// <summary>
/// Tests for ValidationReportExporter file export functionality.
/// </summary>
public class ValidationReportExporterTests
{
    private readonly ValidationReportExporter _exporter = new();

    [Fact]
    public async Task ExportAsync_WritesValidJsonFile()
    {
        var result = new SMatrixValidationResult();
        result.AddError("Test error");
        result.AddWarning("Test warning");

        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"test_{Guid.NewGuid()}.validation.json");

        try
        {
            await _exporter.ExportAsync(result, filePath, wavelengthNm: 1310);

            File.Exists(filePath).ShouldBeTrue();
            var json = await File.ReadAllTextAsync(filePath);
            var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("wavelengthNm").GetInt32().ShouldBe(1310);
            doc.RootElement.GetProperty("errorCount").GetInt32().ShouldBe(1);
            doc.RootElement.GetProperty("warningCount").GetInt32().ShouldBe(1);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void GetAutoExportPath_GeneratesCorrectPath()
    {
        var designPath = Path.Combine("C:", "designs", "mydesign.cappro");

        var result = ValidationReportExporter.GetAutoExportPath(designPath);

        result.ShouldEndWith("mydesign.validation.json");
        Path.GetDirectoryName(result).ShouldBe(Path.Combine("C:", "designs"));
    }

    [Fact]
    public void GetAutoExportPath_HandlesPathWithoutDirectory()
    {
        var result = ValidationReportExporter.GetAutoExportPath("design.cappro");

        result.ShouldEndWith("design.validation.json");
    }
}
