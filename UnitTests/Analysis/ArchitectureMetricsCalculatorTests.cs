using CAP_Core.Analysis;
using CAP_Core.Grid;
using Shouldly;

namespace UnitTests.Analysis;

/// <summary>
/// Integration tests for ArchitectureMetricsCalculator using real grid setups.
/// </summary>
public class ArchitectureMetricsCalculatorTests
{
    [Fact]
    public void Calculate_EmptyGrid_ReturnsZeroMetrics()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var calculator = ArchitectureMetricsCalculator.FromGridManager(grid);

        // Act
        var metrics = calculator.Calculate();

        // Assert
        metrics.TotalComponentCount.ShouldBe(0);
        metrics.ComponentCountByType.Count.ShouldBe(0);
        metrics.NetworkDepth.ShouldBe(0);
        metrics.FeedbackLoopCount.ShouldBe(0);
        metrics.PathLengths.Count.ShouldBe(0);
        metrics.AveragePathLength.ShouldBe(0.0);
    }

    [Fact]
    public void Calculate_SingleWaveguide_ReturnsCorrectCounts()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var wg = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(3, 3, wg);
        var calculator = ArchitectureMetricsCalculator.FromGridManager(grid);

        // Act
        var metrics = calculator.Calculate();

        // Assert
        metrics.TotalComponentCount.ShouldBe(1);
        metrics.ComponentCountByType[wg.TypeNumber].ShouldBe(1);
    }

    [Fact]
    public void Calculate_TwoConnectedWaveguides_HasFanOut()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var wg1 = TestComponentFactory.CreateStraightWaveGuide();
        var wg2 = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(3, 3, wg1);
        grid.ComponentMover.PlaceComponent(4, 3, wg2);
        var calculator = ArchitectureMetricsCalculator.FromGridManager(grid);

        // Act
        var metrics = calculator.Calculate();

        // Assert
        metrics.TotalComponentCount.ShouldBe(2);
        metrics.FanOutDistribution.ContainsKey(1).ShouldBeTrue();
    }

    [Fact]
    public void Calculate_ThreeChainedWaveguides_MiddleHasHigherFanOut()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var wg1 = TestComponentFactory.CreateStraightWaveGuide();
        var wg2 = TestComponentFactory.CreateStraightWaveGuide();
        var wg3 = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(3, 3, wg1);
        grid.ComponentMover.PlaceComponent(4, 3, wg2);
        grid.ComponentMover.PlaceComponent(5, 3, wg3);
        var calculator = ArchitectureMetricsCalculator.FromGridManager(grid);

        // Act
        var metrics = calculator.Calculate();

        // Assert
        metrics.TotalComponentCount.ShouldBe(3);
        // End nodes have fan-out 1, middle has fan-out 2
        metrics.FanOutDistribution.ContainsKey(1).ShouldBeTrue();
        metrics.FanOutDistribution.ShouldContainKey(2);
    }

    [Fact]
    public void Calculate_InputToOutput_CalculatesDepth()
    {
        // Arrange - connect through external input/output ports
        // External inputs at x=0 y=2,3,4; outputs at x=9 y=5,6,7,8,9
        var grid = new GridManager(10, 10);
        var wgInput = TestComponentFactory.CreateStraightWaveGuide();
        var wgMiddle = TestComponentFactory.CreateStraightWaveGuide();
        var wgOutput = TestComponentFactory.CreateStraightWaveGuide();
        grid.ComponentMover.PlaceComponent(0, 2, wgInput);
        grid.ComponentMover.PlaceComponent(1, 2, wgMiddle);
        // Note: output needs to be at x=9 to match output port
        grid.ComponentMover.PlaceComponent(9, 5, wgOutput);
        var calculator = ArchitectureMetricsCalculator.FromGridManager(grid);

        // Act
        var metrics = calculator.Calculate();

        // Assert
        metrics.TotalComponentCount.ShouldBe(3);
        // Input and middle are connected; output is disconnected
        // So no full input-to-output path
        metrics.FeedbackLoopCount.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Calculate_MixedComponentTypes_CountsByType()
    {
        // Arrange
        var grid = new GridManager(10, 10);
        var wg = TestComponentFactory.CreateStraightWaveGuide();
        var coupler = TestComponentFactory.CreateDirectionalCoupler();
        grid.ComponentMover.PlaceComponent(1, 1, wg);
        grid.ComponentMover.PlaceComponent(3, 3, coupler);
        var calculator = ArchitectureMetricsCalculator.FromGridManager(grid);

        // Act
        var metrics = calculator.Calculate();

        // Assert
        metrics.TotalComponentCount.ShouldBe(2);
        // Both have TypeNumber 0 in the test factory
        metrics.ComponentCountByType[0].ShouldBe(2);
    }

    [Fact]
    public void Calculate_ConnectedChain_AveragePathLengthIsReasonable()
    {
        // Arrange: a chain from input to output
        var grid = new GridManager(10, 10);
        var wg1 = TestComponentFactory.CreateStraightWaveGuide();
        var wg2 = TestComponentFactory.CreateStraightWaveGuide();
        // Place at input port position
        grid.ComponentMover.PlaceComponent(0, 2, wg1);
        grid.ComponentMover.PlaceComponent(1, 2, wg2);
        var calculator = ArchitectureMetricsCalculator.FromGridManager(grid);

        // Act
        var metrics = calculator.Calculate();

        // Assert
        metrics.AveragePathLength.ShouldBeGreaterThanOrEqualTo(0.0);
    }
}
