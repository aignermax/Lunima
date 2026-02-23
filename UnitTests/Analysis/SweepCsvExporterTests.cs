using CAP_Core.Analysis;
using Shouldly;
using System.Numerics;

namespace UnitTests.Analysis;

public class SweepCsvExporterTests
{
    [Fact]
    public void GenerateCsvContent_ContainsHeader()
    {
        // Arrange
        var result = CreateSampleResult();

        // Act
        var csv = SweepCsvExporter.GenerateCsvContent(result);

        // Assert
        var firstLine = csv.Split(Environment.NewLine)[0];
        firstLine.ShouldStartWith("Coupling");
        firstLine.ShouldContain("Pin_");
    }

    [Fact]
    public void GenerateCsvContent_ContainsCorrectRowCount()
    {
        // Arrange
        var result = CreateSampleResult();

        // Act
        var csv = SweepCsvExporter.GenerateCsvContent(result);

        // Assert - header + 3 data rows + trailing newline
        var lines = csv.Split(Environment.NewLine);
        var nonEmptyLines = lines.Where(l => !string.IsNullOrEmpty(l)).ToArray();
        nonEmptyLines.Length.ShouldBe(4); // 1 header + 3 data
    }

    [Fact]
    public void GenerateCsvContent_DataRowsContainParameterValues()
    {
        // Arrange
        var result = CreateSampleResult();

        // Act
        var csv = SweepCsvExporter.GenerateCsvContent(result);

        // Assert
        var lines = csv.Split(Environment.NewLine);
        lines[1].ShouldStartWith("0");
        lines[2].ShouldStartWith("0.5");
        lines[3].ShouldStartWith("1");
    }

    [Fact]
    public void GenerateCsvContent_NullResult_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            SweepCsvExporter.GenerateCsvContent(null!));
    }

    [Fact]
    public void ExportToFile_NullResult_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            SweepCsvExporter.ExportToFile(null!, "test.csv"));
    }

    [Fact]
    public void ExportToFile_EmptyPath_ThrowsArgumentException()
    {
        var result = CreateSampleResult();

        Should.Throw<ArgumentException>(() =>
            SweepCsvExporter.ExportToFile(result, ""));
    }

    [Fact]
    public void ExportToFile_WritesFileSuccessfully()
    {
        // Arrange
        var result = CreateSampleResult();
        var tempPath = Path.Combine(Path.GetTempPath(), $"sweep_test_{Guid.NewGuid()}.csv");

        try
        {
            // Act
            SweepCsvExporter.ExportToFile(result, tempPath);

            // Assert
            File.Exists(tempPath).ShouldBeTrue();
            var content = File.ReadAllText(tempPath);
            content.ShouldContain("Coupling");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void GenerateCsvContent_UsesInvariantCulture()
    {
        // Arrange - use values that would differ in other cultures
        var pinId = Guid.NewGuid();
        var dataPoints = new List<SweepDataPoint>
        {
            new(0.123456, new Dictionary<Guid, Complex>
            {
                { pinId, new Complex(0.5, 0) }
            })
        };

        var component = TestComponentHelper.CreateComponentWithSlider();
        var param = new SweepParameter(component, 0, "Phase");
        var config = new SweepConfiguration(param, 0, 1, 2, 635);
        var result = new SweepResult(config, dataPoints, new List<Guid> { pinId });

        // Act
        var csv = SweepCsvExporter.GenerateCsvContent(result);

        // Assert - should use dot as decimal separator
        csv.ShouldContain("0.123456");
        csv.ShouldNotContain("0,123456");
    }

    private static SweepResult CreateSampleResult()
    {
        var pinId = Guid.NewGuid();
        var dataPoints = new List<SweepDataPoint>
        {
            new(0.0, new Dictionary<Guid, Complex> { { pinId, new Complex(0.1, 0) } }),
            new(0.5, new Dictionary<Guid, Complex> { { pinId, new Complex(0.5, 0) } }),
            new(1.0, new Dictionary<Guid, Complex> { { pinId, new Complex(1.0, 0) } }),
        };

        var component = TestComponentHelper.CreateComponentWithSlider();
        var param = new SweepParameter(component, 0, "Coupling");
        var config = new SweepConfiguration(param, 0, 1, 3, 635);

        return new SweepResult(config, dataPoints, new List<Guid> { pinId });
    }
}
