using System.Numerics;
using CAP_Core.Components.FormulaReading;
using CAP_Core.Components.Parametric;
using CAP_Core.LightCalculation.Snapshots;
using Shouldly;
using static UnitTests.Simulation.Snapshots.SnapshotPhysicsOracle;

namespace UnitTests.Simulation.Snapshots;

public sealed class SimulationSnapshotDefinitionContractTests
{
    [Fact]
    public void OpaqueFactoryWithoutDetachedDefinition_RejectsCaptureWithoutInvokingFactory()
    {
        var component = CreateComponent(2, (0, 1, Complex.One));
        var invoked = false;
        component.WaveLengthToSMatrixMap[1550].ParametricRebuild = (_, _) =>
        {
            invoked = true;
            throw new InvalidOperationException("live factory must never run");
        };

        var error = Should.Throw<InvalidDataException>(() => SimulationSnapshotCapture.Capture([component], [],
            [new(component.PhysicalPins[0].LogicalPin.IDInFlow, 1550, Complex.One)], false));
        error.Message.ShouldContain("detached parametric definition");
        invoked.ShouldBeFalse();
    }

    [Theory]
    [InlineData("SLIDER999")]
    [InlineData("1 + (")]
    [InlineData("mag=0.5;phase=90")]
    public async Task InvalidRawFormula_FailsInsteadOfUsingNumericWeightOrLiveDelegate(string expression)
    {
        var component = CreateComponent(2, (0, 1, Complex.One));
        var input = component.PhysicalPins[0].LogicalPin;
        var output = component.PhysicalPins[1].LogicalPin;
        var invoked = false;
        component.WaveLengthToSMatrixMap[1550].NonLinearConnections[(input.IDInFlow, output.IDOutFlow)] =
            new ConnectionFunction(_ => { invoked = true; return Complex.One; }, expression, [], false);
        var snapshot = SimulationSnapshotCapture.Capture([component], [], [new(input.IDInFlow, 1550, Complex.One)], false);

        var error = await Record.ExceptionAsync(() => SimulationSnapshotSolver.RunAsync(snapshot)
            .WaitAsync(TimeSpan.FromSeconds(5)));
        error.ShouldNotBeNull("an unrebuildable definition must not silently fall back to a numeric matrix");
        error.ShouldNotBeOfType<TimeoutException>();
        invoked.ShouldBeFalse();
    }

    [Fact]
    public async Task ParametricDefinitionWithUnknownPort_FailsInsteadOfUsingCapturedNumericMatrix()
    {
        var component = CreateComponent(2, (0, 1, Complex.One));
        var matrix = component.WaveLengthToSMatrixMap[1550];
        matrix.ParametricSnapshot = new ParametricSMatrixSnapshot([],
            [new FormulaConnection("missing-port", "p1", "0.5", "0")]);
        var snapshot = SimulationSnapshotCapture.Capture([component], [],
            [new(component.PhysicalPins[0].LogicalPin.IDInFlow, 1550, Complex.One)], false);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => SimulationSnapshotSolver.RunAsync(snapshot)
            .WaitAsync(TimeSpan.FromSeconds(5)));
        error.Message.ShouldContain("unknown pin 'missing-port'");
    }
}
