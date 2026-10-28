using System.Numerics;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using Shouldly;

namespace UnitTests.LightCalculation.TimeDomainSimulation;

public sealed class TransientCancellationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void CancellationBetweenWavelengths_PreventsFurtherMatrixEvaluation(int cancelAfter)
    {
        using var stop = new CancellationTokenSource();
        var builder = new CancellingBuilder(stop, cancelAfter);
        if (cancelAfter == 0) stop.Cancel();
        var simulator = new TimeDomainSimulator(builder);
        var time = TimeSignalDefinition.FromWavelengthSweep(1550, 100, 64);
        Should.Throw<OperationCanceledException>(() => simulator.Run(
            new() { [builder.Input] = Enumerable.Repeat(1.0, 64).ToArray() }, time,
            1550, 100, 64, cancellationToken: stop.Token));
        builder.Calls.ShouldBe(cancelAfter);
    }

    private sealed class CancellingBuilder(CancellationTokenSource stop, int cancelAfter) : ISystemMatrixBuilder
    {
        internal Guid Input { get; } = Guid.NewGuid();
        private readonly Guid _output = Guid.NewGuid();
        internal int Calls { get; private set; }
        public SMatrix GetSystemSMatrix(int wavelengthNm)
        {
            Calls++;
            var matrix = new SMatrix([Input, _output], new());
            matrix.SetValues(new() { [(Input, _output)] = Complex.One });
            if (Calls == cancelAfter) stop.Cancel();
            return matrix;
        }
        public SMatrix GetSystemSMatrix(double wavelengthNm) => GetSystemSMatrix((int)wavelengthNm);
    }
}
