using CAP_Core.Analysis;
using Shouldly;

namespace UnitTests.Analysis;

public class StochasticResultCsvExporterTests
{
    [Fact]
    public void ExportToCsv_WithResults_ContainsHeader()
    {
        var result = CreateSimpleResult();

        string csv = StochasticResultCsvExporter.ExportToCsv(result);

        csv.ShouldContain("PinId,MeanPower,StdDeviation");
    }

    [Fact]
    public void ExportToCsv_WithResults_ContainsPinData()
    {
        var pinId = Guid.NewGuid();
        var samples = new List<double> { 0.5, 0.6, 0.7 };
        var stats = new OutputPowerStatistics(pinId, samples);
        var result = new StochasticResult(
            3,
            new List<ParameterVariation>(),
            new List<OutputPowerStatistics> { stats });

        string csv = StochasticResultCsvExporter.ExportToCsv(result);

        csv.ShouldContain(pinId.ToString());
        csv.ShouldContain("Iteration");
    }

    [Fact]
    public void ExportToCsv_WithResults_ContainsSampleRows()
    {
        var result = CreateSimpleResult();

        string csv = StochasticResultCsvExporter.ExportToCsv(result);

        // Should have iteration rows (0, 1, 2 for 3 samples)
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        // Header line + pin stats + empty line + sample header + 3 sample rows
        lines.Length.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void ExportToCsv_EmptyResults_ProducesHeaderOnly()
    {
        var result = new StochasticResult(
            0,
            new List<ParameterVariation>(),
            new List<OutputPowerStatistics>());

        string csv = StochasticResultCsvExporter.ExportToCsv(result);

        csv.ShouldContain("PinId,MeanPower,StdDeviation");
    }

    [Fact]
    public void ExportToCsv_UsesInvariantCulture()
    {
        var pinId = Guid.NewGuid();
        var samples = new List<double> { 0.123456789 };
        var stats = new OutputPowerStatistics(pinId, samples);
        var result = new StochasticResult(
            1,
            new List<ParameterVariation>(),
            new List<OutputPowerStatistics> { stats });

        string csv = StochasticResultCsvExporter.ExportToCsv(result);

        // Should use '.' as decimal separator, not ','
        csv.ShouldContain("0.123456789");
    }

    [Fact]
    public void ExportToFile_WritesFile()
    {
        var result = CreateSimpleResult();
        string tempFile = Path.GetTempFileName();

        try
        {
            StochasticResultCsvExporter.ExportToFile(result, tempFile);

            File.Exists(tempFile).ShouldBeTrue();
            string content = File.ReadAllText(tempFile);
            content.ShouldContain("PinId,MeanPower,StdDeviation");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static StochasticResult CreateSimpleResult()
    {
        var pinId = Guid.NewGuid();
        var samples = new List<double> { 0.5, 0.6, 0.7 };
        var stats = new OutputPowerStatistics(pinId, samples);
        return new StochasticResult(
            3,
            new List<ParameterVariation> { new(ParameterType.Coupling, 0.05) },
            new List<OutputPowerStatistics> { stats });
    }
}
