using CAP_Core.Components;
using CAP_Core.Grid;
using Moq;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace UnitTests.Components;

public class LibraryStatisticsExporterTests
{
    private static Component CreateMinimalComponent(int typeNumber, string identifier)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        var sMatrix = new Dictionary<int, CAP_Core.LightCalculation.SMatrix>();
        return new Component(sMatrix, new(), "", "", parts, typeNumber, identifier, DiscreteRotation.R0);
    }

    private static LibraryStatistics CreateStatistics(List<Component> components)
    {
        var mockTileManager = new Mock<ITileManager>();
        mockTileManager.Setup(tm => tm.GetAllComponents()).Returns(components);
        return new LibraryStatistics(mockTileManager.Object);
    }

    [Fact]
    public void ExportToJson_EmptyGrid_ReturnsValidJson()
    {
        var stats = CreateStatistics(new List<Component>());

        var json = LibraryStatisticsExporter.ExportToJson(stats);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalComponents").GetInt32().ShouldBe(0);
        doc.RootElement.GetProperty("distinctTypes").GetInt32().ShouldBe(0);
        doc.RootElement.GetProperty("usageRecords").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void ExportToJson_WithComponents_ContainsCorrectData()
    {
        var components = new List<Component>
        {
            CreateMinimalComponent(1, "Splitter"),
            CreateMinimalComponent(2, "Coupler"),
            CreateMinimalComponent(2, "Coupler")
        };
        var stats = CreateStatistics(components);

        var json = LibraryStatisticsExporter.ExportToJson(stats);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalComponents").GetInt32().ShouldBe(3);
        doc.RootElement.GetProperty("distinctTypes").GetInt32().ShouldBe(2);

        var records = doc.RootElement.GetProperty("usageRecords");
        records.GetArrayLength().ShouldBe(2);

        // First record should be Coupler (count 2, sorted desc)
        var first = records[0];
        first.GetProperty("identifier").GetString().ShouldBe("Coupler");
        first.GetProperty("typeNumber").GetInt32().ShouldBe(2);
        first.GetProperty("count").GetInt32().ShouldBe(2);

        // Second record should be Splitter (count 1)
        var second = records[1];
        second.GetProperty("identifier").GetString().ShouldBe("Splitter");
        second.GetProperty("typeNumber").GetInt32().ShouldBe(1);
        second.GetProperty("count").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void ExportToJson_NullStatistics_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(
            () => LibraryStatisticsExporter.ExportToJson(null!));
    }

    [Fact]
    public void ExportToJson_OutputIsValidJson()
    {
        var components = new List<Component>
        {
            CreateMinimalComponent(1, "Splitter"),
            CreateMinimalComponent(1, "Splitter"),
            CreateMinimalComponent(2, "Coupler")
        };
        var stats = CreateStatistics(components);

        var json = LibraryStatisticsExporter.ExportToJson(stats);

        // Should not throw — validates JSON structure
        var doc = JsonDocument.Parse(json);
        doc.ShouldNotBeNull();
    }
}
