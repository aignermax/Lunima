using CAP_Core.Analysis;
using CAP_Core.Components;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

public class PathLossEntryTests
{
    [Fact]
    public void Constructor_SumsLossFromConnections()
    {
        // Arrange
        var connections = new List<WaveguideConnection>
        {
            CreateConnectionWithLoss(2.0),
            CreateConnectionWithLoss(3.5),
        };

        // Act
        var entry = new PathLossEntry(connections, "A -> B");

        // Assert
        entry.TotalLossDb.ShouldBe(5.5);
    }

    [Fact]
    public void Constructor_ClassifiesSeverityCorrectly()
    {
        // Arrange - total 1.0 dB => Low
        var lowEntry = new PathLossEntry(
            new List<WaveguideConnection> { CreateConnectionWithLoss(1.0) },
            "low");

        // Arrange - total 5.0 dB => Medium
        var mediumEntry = new PathLossEntry(
            new List<WaveguideConnection> { CreateConnectionWithLoss(5.0) },
            "medium");

        // Arrange - total 12.0 dB => High
        var highEntry = new PathLossEntry(
            new List<WaveguideConnection> { CreateConnectionWithLoss(12.0) },
            "high");

        // Assert
        lowEntry.Severity.ShouldBe(LossSeverity.Low);
        mediumEntry.Severity.ShouldBe(LossSeverity.Medium);
        highEntry.Severity.ShouldBe(LossSeverity.High);
    }

    [Fact]
    public void ToString_ContainsLabelAndLoss()
    {
        var entry = new PathLossEntry(
            new List<WaveguideConnection> { CreateConnectionWithLoss(4.5) },
            "in -> out");

        var result = entry.ToString();

        result.ShouldContain("in -> out");
        result.ShouldContain("4.50");
    }

    [Fact]
    public void Constructor_SingleConnection_UsesItsLoss()
    {
        var connection = CreateConnectionWithLoss(7.0);

        var entry = new PathLossEntry(
            new List<WaveguideConnection> { connection }, "test");

        entry.TotalLossDb.ShouldBe(7.0);
        entry.Connections.Count.ShouldBe(1);
        entry.Connections[0].ShouldBeSameAs(connection);
    }

    private static WaveguideConnection CreateConnectionWithLoss(double lossDb)
    {
        var connection = new WaveguideConnection();
        SetTotalLossDb(connection, lossDb);
        return connection;
    }

    /// <summary>
    /// Sets the TotalLossDb via reflection since it has a private setter.
    /// </summary>
    internal static void SetTotalLossDb(WaveguideConnection connection, double lossDb)
    {
        var prop = typeof(WaveguideConnection).GetProperty(nameof(WaveguideConnection.TotalLossDb));
        prop!.SetValue(connection, lossDb);
    }
}
