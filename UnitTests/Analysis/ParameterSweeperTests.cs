using CAP_Core.Analysis;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.LightCalculation;
using Moq;
using Shouldly;
using System.Numerics;

namespace UnitTests.Analysis;

public class ParameterSweeperTests
{
    [Fact]
    public async Task RunSweepAsync_ReturnsCorrectNumberOfDataPoints()
    {
        // Arrange
        var component = TestComponentHelper.CreateComponentWithSlider(0, 1, 0.5);
        var param = new SweepParameter(component, 0, "Coupling");
        var config = new SweepConfiguration(param, 0, 1, 5, StandardWaveLengths.RedNM);

        var mockCalc = CreateMockCalculator();
        var sweeper = new ParameterSweeper(mockCalc.Object);

        // Act
        var result = await sweeper.RunSweepAsync(config);

        // Assert
        result.DataPoints.Count.ShouldBe(5);
    }

    [Fact]
    public async Task RunSweepAsync_RestoresOriginalSliderValue()
    {
        // Arrange
        var component = TestComponentHelper.CreateComponentWithSlider(0, 1, 0.5);
        var param = new SweepParameter(component, 0, "Coupling");
        var config = new SweepConfiguration(param, 0, 1, 3, StandardWaveLengths.RedNM);

        var mockCalc = CreateMockCalculator();
        var sweeper = new ParameterSweeper(mockCalc.Object);

        double originalValue = component.GetSlider(0)!.Value;

        // Act
        await sweeper.RunSweepAsync(config);

        // Assert
        component.GetSlider(0)!.Value.ShouldBe(originalValue);
    }

    [Fact]
    public async Task RunSweepAsync_SetsSliderValueForEachStep()
    {
        // Arrange
        var component = TestComponentHelper.CreateComponentWithSlider(0, 1, 0.5);
        var param = new SweepParameter(component, 0, "Coupling");
        var config = new SweepConfiguration(param, 0.2, 0.8, 3, StandardWaveLengths.RedNM);

        var capturedValues = new List<double>();
        var mockCalc = new Mock<ILightCalculator>();
        mockCalc.Setup(c => c.CalculateFieldPropagationAsync(
                It.IsAny<CancellationTokenSource>(), It.IsAny<int>()))
            .Returns(() =>
            {
                capturedValues.Add(component.GetSlider(0)!.Value);
                return Task.FromResult(CreateSampleFieldResult());
            });

        var sweeper = new ParameterSweeper(mockCalc.Object);

        // Act
        await sweeper.RunSweepAsync(config);

        // Assert
        capturedValues.Count.ShouldBe(3);
        capturedValues[0].ShouldBe(0.2, 1e-10);
        capturedValues[1].ShouldBe(0.5, 1e-10);
        capturedValues[2].ShouldBe(0.8, 1e-10);
    }

    [Fact]
    public async Task RunSweepAsync_CancellationToken_StopsEarly()
    {
        // Arrange
        var component = TestComponentHelper.CreateComponentWithSlider(0, 1, 0.5);
        var param = new SweepParameter(component, 0, "Coupling");
        var config = new SweepConfiguration(param, 0, 1, 100, StandardWaveLengths.RedNM);

        var cts = new CancellationTokenSource();
        int callCount = 0;
        var mockCalc = new Mock<ILightCalculator>();
        mockCalc.Setup(c => c.CalculateFieldPropagationAsync(
                It.IsAny<CancellationTokenSource>(), It.IsAny<int>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount >= 3) cts.Cancel();
                return Task.FromResult(CreateSampleFieldResult());
            });

        var sweeper = new ParameterSweeper(mockCalc.Object);

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => sweeper.RunSweepAsync(config, cts.Token));
    }

    [Fact]
    public async Task RunSweepAsync_RestoresValueEvenOnException()
    {
        // Arrange
        var component = TestComponentHelper.CreateComponentWithSlider(0, 1, 0.5);
        var param = new SweepParameter(component, 0, "Coupling");
        var config = new SweepConfiguration(param, 0, 1, 5, StandardWaveLengths.RedNM);

        double originalValue = component.GetSlider(0)!.Value;

        int callCount = 0;
        var mockCalc = new Mock<ILightCalculator>();
        mockCalc.Setup(c => c.CalculateFieldPropagationAsync(
                It.IsAny<CancellationTokenSource>(), It.IsAny<int>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount >= 3) throw new InvalidOperationException("Sim failed");
                return Task.FromResult(CreateSampleFieldResult());
            });

        var sweeper = new ParameterSweeper(mockCalc.Object);

        // Act
        await Should.ThrowAsync<InvalidOperationException>(
            () => sweeper.RunSweepAsync(config));

        // Assert - value should still be restored
        component.GetSlider(0)!.Value.ShouldBe(originalValue);
    }

    [Fact]
    public void Constructor_NullCalculator_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new ParameterSweeper(null!));
    }

    [Fact]
    public async Task RunSweepAsync_NullConfiguration_ThrowsArgumentNullException()
    {
        var mockCalc = CreateMockCalculator();
        var sweeper = new ParameterSweeper(mockCalc.Object);

        await Should.ThrowAsync<ArgumentNullException>(
            () => sweeper.RunSweepAsync(null!));
    }

    private static Mock<ILightCalculator> CreateMockCalculator()
    {
        var mock = new Mock<ILightCalculator>();
        mock.Setup(c => c.CalculateFieldPropagationAsync(
                It.IsAny<CancellationTokenSource>(), It.IsAny<int>()))
            .ReturnsAsync(CreateSampleFieldResult());
        return mock;
    }

    private static Dictionary<Guid, Complex> CreateSampleFieldResult()
    {
        return new Dictionary<Guid, Complex>
        {
            { Guid.NewGuid(), new Complex(0.7, 0.1) },
            { Guid.NewGuid(), new Complex(0.3, -0.2) }
        };
    }
}
