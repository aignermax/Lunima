using CAP_Core.Components;
using CAP_Core.Grid;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

public class LibraryStatisticsTests
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
    public void GetUsageStatistics_EmptyGrid_ReturnsEmptyList()
    {
        var stats = CreateStatistics(new List<Component>());

        var result = stats.GetUsageStatistics();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void GetUsageStatistics_SingleComponent_ReturnsSingleRecord()
    {
        var components = new List<Component>
        {
            CreateMinimalComponent(1, "Splitter")
        };
        var stats = CreateStatistics(components);

        var result = stats.GetUsageStatistics();

        result.Count.ShouldBe(1);
        result[0].Identifier.ShouldBe("Splitter");
        result[0].TypeNumber.ShouldBe(1);
        result[0].Count.ShouldBe(1);
    }

    [Fact]
    public void GetUsageStatistics_MultipleOfSameType_CountsCorrectly()
    {
        var components = new List<Component>
        {
            CreateMinimalComponent(1, "Splitter"),
            CreateMinimalComponent(1, "Splitter"),
            CreateMinimalComponent(1, "Splitter")
        };
        var stats = CreateStatistics(components);

        var result = stats.GetUsageStatistics();

        result.Count.ShouldBe(1);
        result[0].Count.ShouldBe(3);
    }

    [Fact]
    public void GetUsageStatistics_DifferentTypes_SortedByCountDescending()
    {
        var components = new List<Component>
        {
            CreateMinimalComponent(1, "Splitter"),
            CreateMinimalComponent(2, "Coupler"),
            CreateMinimalComponent(2, "Coupler"),
            CreateMinimalComponent(2, "Coupler"),
            CreateMinimalComponent(3, "PhaseShifter"),
            CreateMinimalComponent(3, "PhaseShifter")
        };
        var stats = CreateStatistics(components);

        var result = stats.GetUsageStatistics();

        result.Count.ShouldBe(3);
        result[0].Identifier.ShouldBe("Coupler");
        result[0].Count.ShouldBe(3);
        result[1].Identifier.ShouldBe("PhaseShifter");
        result[1].Count.ShouldBe(2);
        result[2].Identifier.ShouldBe("Splitter");
        result[2].Count.ShouldBe(1);
    }

    [Fact]
    public void GetUsageStatistics_EqualCounts_SortedByIdentifier()
    {
        var components = new List<Component>
        {
            CreateMinimalComponent(2, "Coupler"),
            CreateMinimalComponent(1, "Amplifier")
        };
        var stats = CreateStatistics(components);

        var result = stats.GetUsageStatistics();

        result.Count.ShouldBe(2);
        result[0].Identifier.ShouldBe("Amplifier");
        result[1].Identifier.ShouldBe("Coupler");
    }

    [Fact]
    public void GetTotalComponentCount_ReturnsCorrectTotal()
    {
        var components = new List<Component>
        {
            CreateMinimalComponent(1, "Splitter"),
            CreateMinimalComponent(2, "Coupler"),
            CreateMinimalComponent(2, "Coupler")
        };
        var stats = CreateStatistics(components);

        stats.GetTotalComponentCount().ShouldBe(3);
    }

    [Fact]
    public void GetTotalComponentCount_EmptyGrid_ReturnsZero()
    {
        var stats = CreateStatistics(new List<Component>());

        stats.GetTotalComponentCount().ShouldBe(0);
    }

    [Fact]
    public void GetDistinctTypeCount_ReturnsCorrectCount()
    {
        var components = new List<Component>
        {
            CreateMinimalComponent(1, "Splitter"),
            CreateMinimalComponent(2, "Coupler"),
            CreateMinimalComponent(2, "Coupler"),
            CreateMinimalComponent(3, "PhaseShifter")
        };
        var stats = CreateStatistics(components);

        stats.GetDistinctTypeCount().ShouldBe(3);
    }

    [Fact]
    public void GetDistinctTypeCount_EmptyGrid_ReturnsZero()
    {
        var stats = CreateStatistics(new List<Component>());

        stats.GetDistinctTypeCount().ShouldBe(0);
    }

    [Fact]
    public void Constructor_NullTileManager_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new LibraryStatistics(null!));
    }
}
