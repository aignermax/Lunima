using CAP_Core.Analysis;
using CAP_Core.Components;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

public class LossBudgetResultTests
{
    [Fact]
    public void Constructor_EmptyPaths_AllStatisticsAreZero()
    {
        var result = new LossBudgetResult(
            Array.Empty<PathLossEntry>(),
            Array.Empty<WaveguideConnection>());

        result.MinLossDb.ShouldBe(0);
        result.MaxLossDb.ShouldBe(0);
        result.AverageLossDb.ShouldBe(0);
        result.HighestLossPath.ShouldBeNull();
        result.Paths.Count.ShouldBe(0);
    }

    [Fact]
    public void Constructor_MultiplePaths_ComputesMinMaxAverage()
    {
        var paths = new List<PathLossEntry>
        {
            CreateEntry(2.0),
            CreateEntry(6.0),
            CreateEntry(10.0),
        };

        var result = new LossBudgetResult(paths, Array.Empty<WaveguideConnection>());

        result.MinLossDb.ShouldBe(2.0);
        result.MaxLossDb.ShouldBe(10.0);
        result.AverageLossDb.ShouldBe(6.0);
    }

    [Fact]
    public void HighestLossPath_IsPathWithMaxLoss()
    {
        var lowPath = CreateEntry(1.0);
        var highPath = CreateEntry(15.0);
        var midPath = CreateEntry(5.0);

        var result = new LossBudgetResult(
            new List<PathLossEntry> { lowPath, highPath, midPath },
            Array.Empty<WaveguideConnection>());

        result.HighestLossPath.ShouldBeSameAs(highPath);
    }

    [Fact]
    public void GetPathsBySeverity_FiltersCorrectly()
    {
        var paths = new List<PathLossEntry>
        {
            CreateEntry(1.0),   // Low
            CreateEntry(5.0),   // Medium
            CreateEntry(12.0),  // High
            CreateEntry(2.0),   // Low
        };

        var result = new LossBudgetResult(paths, Array.Empty<WaveguideConnection>());

        result.GetPathsBySeverity(LossSeverity.Low).Count().ShouldBe(2);
        result.GetPathsBySeverity(LossSeverity.Medium).Count().ShouldBe(1);
        result.GetPathsBySeverity(LossSeverity.High).Count().ShouldBe(1);
    }

    [Fact]
    public void CriticalConnections_AreStored()
    {
        var conn = new WaveguideConnection();
        var result = new LossBudgetResult(
            Array.Empty<PathLossEntry>(),
            new List<WaveguideConnection> { conn });

        result.CriticalConnections.Count.ShouldBe(1);
        result.CriticalConnections[0].ShouldBeSameAs(conn);
    }

    private static PathLossEntry CreateEntry(double lossDb)
    {
        var connection = new WaveguideConnection();
        PathLossEntryTests.SetTotalLossDb(connection, lossDb);
        return new PathLossEntry(
            new List<WaveguideConnection> { connection },
            $"test ({lossDb} dB)");
    }
}
