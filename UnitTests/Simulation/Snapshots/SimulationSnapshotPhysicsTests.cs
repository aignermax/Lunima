using System.Numerics;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Parametric;
using CAP_Core.Grid.FormulaReading;
using Shouldly;
using static UnitTests.Simulation.Snapshots.SnapshotPhysicsOracle;

namespace UnitTests.Simulation.Snapshots;

public sealed class SimulationSnapshotPhysicsTests
{
    [Theory]
    [InlineData("flat")]
    [InlineData("nested")]
    [InlineData("die")]
    public async Task HierarchyAndFrozenTransfers_MatchGridCalculator(string hierarchy)
    {
        var first = CreateComponent(2, (0, 1, new Complex(0.8, 0.1)));
        var second = CreateComponent(2, (0, 1, new Complex(0.6, -0.2)));
        var wire = Wire(first, 1, second, 0, 37);
        Component[] roots = [first, second];
        WaveguideConnection[] wires = [wire];
        if (hierarchy != "flat")
        {
            var inner = new ComponentGroup("inner");
            inner.AddChild(first);
            inner.AddChild(second);
            inner.AddInternalPath(new FrozenWaveguidePath
                { StartPin = wire.StartPin, EndPin = wire.EndPin, Path = Path(37) });
            var outer = new ComponentGroup("outer");
            outer.AddChild(inner);
            if (hierarchy == "die") outer.Die = new DieDefinition(500, 500);
            roots = [outer];
            wires = [];
        }
        var result = await CompareAsync(roots, wires, first.PhysicalPins[0].LogicalPin, coherent: true);
        result.Fields[second.PhysicalPins[1].LogicalPin.IDOutFlow].Magnitude.ShouldBeGreaterThan(0.4);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(180.0, 0.0)]
    [InlineData(90.0, 0.7071067811865476)]
    public async Task CoherentMachZehnder_ParametricPhaseAndRoute_MatchGridCalculator(double degrees, double magnitude)
    {
        var split = CreateComponent(3, (0, 1, Math.Sqrt(0.5)), (0, 2, Math.Sqrt(0.5)));
        var arm = CreateComponent(2);
        var pins = arm.PhysicalPins.Select(pin => pin.LogicalPin).ToList();
        var slider = new Slider(Guid.NewGuid(), 0, degrees, 360, 0);
        arm.AddSlider(0, slider);
        arm.WaveLengthToSMatrixMap[1550] = ParametricSMatrixFactory.Build(pins, [slider], new(
            [new ParameterDefinition("phase", 0, 0, 360, sliderNumber: 0)],
            [new FormulaConnection("p0", "p1", "1", "phase")]));
        var combine = CreateComponent(3, (1, 0, Math.Sqrt(0.5)), (2, 0, Math.Sqrt(0.5)));
        // Both arms have equal routed length; only the captured slider controls interference.
        var wires = new[] { Wire(split, 1, arm, 0, 21), Wire(arm, 1, combine, 1), Wire(split, 2, combine, 2, 21) };
        foreach (var wire in wires)
        {
            wire.PropagationLossDbPerCm = 0;
            if (wire.RoutedPath != null) wire.RestoreCachedPath(wire.RoutedPath);
        }
        var result = await CompareAsync([split, arm, combine], wires, split.PhysicalPins[0].LogicalPin, coherent: true);
        result.Fields[combine.PhysicalPins[0].LogicalPin.IDOutFlow].Magnitude.ShouldBe(magnitude, 1e-8);
    }

    [Theory]
    [InlineData("ToComplex(SLIDER0, 0)", 0.5)]
    [InlineData("Mul(PIN0, SLIDER0)", 0.25)]
    public async Task RawFormulaAndFieldDependentNonlinearity_MatchGridCalculator(string expression, double expected)
    {
        var component = CreateComponent(2);
        var pins = component.GetAllPins();
        var slider = new Slider(Guid.NewGuid(), 0, 0.5, 1, 0);
        component.AddSlider(0, slider);
        // Legacy raw formulas require the explicit complex functions: a bare slider returns
        // Double, and NCalc's '*' does not multiply a Complex pin field by a Double slider.
        component.WaveLengthToSMatrixMap[1550].NonLinearConnections[(pins[0].IDInFlow, pins[1].IDOutFlow)] =
            MathExpressionReader.ConvertToDelegate(expression, pins, [slider])!.Value;
        var result = await CompareAsync([component], [], pins[0], field: new Complex(0.5, 0));
        result.Fields[pins[1].IDOutFlow].Real.ShouldBe(expected * 0.5, 1e-9);
    }

    [Fact]
    public async Task ElectricalRoute_DoesNotAcquireOpticalPropagationPhase()
    {
        var first = CreateComponent(2, (0, 1, Complex.One));
        var second = CreateComponent(2, (0, 1, Complex.One));
        foreach (var pin in first.GetAllPins().Concat(second.GetAllPins())) pin.MatterType = MatterType.Electricity;
        var wire = Wire(first, 1, second, 0, 51);
        wire.IsElectrical.ShouldBeTrue();
        var result = await CompareAsync([first, second], [wire], first.PhysicalPins[0].LogicalPin, coherent: true);
        result.Fields[second.PhysicalPins[1].LogicalPin.IDOutFlow].Imaginary.ShouldBe(0);
    }

    [Fact]
    public async Task CrossDieFacetGapOffsetAndModeMismatch_MatchGridCalculator()
    {
        var first = CreateComponent(2, (0, 1, Complex.One));
        var second = CreateComponent(2, (0, 1, Complex.One));
        first.TemplateName = second.TemplateName = "Edge Coupler";
        first.ModeFieldDiameterMicrometers = 3;
        second.ModeFieldDiameterMicrometers = 4;
        first.PhysicalPins[1].AngleDegrees = 0;
        second.PhysicalPins[0].AngleDegrees = 180;
        var firstDie = new ComponentGroup("A") { Die = new DieDefinition(500, 500) };
        var secondDie = new ComponentGroup("B") { Die = new DieDefinition(500, 500) };
        firstDie.AddChild(first);
        secondDie.AddChild(second);
        secondDie.MoveGroup(5, 1);
        var wire = Wire(first, 1, second, 0, 50);
        wire.FacetGapMediumIndex = 1.5;
        wire.IsCrossChipletFacetLink.ShouldBeTrue();
        var coupling = ChipletEdgeCouplerCoupling.FieldFactor(wire, 1550);
        coupling.ShouldBeInRange(0.01, 0.99);
        var result = await CompareAsync([firstDie, secondDie], [wire], first.PhysicalPins[0].LogicalPin, coherent: true);
        result.Fields[second.PhysicalPins[1].LogicalPin.IDOutFlow].Real
            .ShouldBe(wire.TransmissionCoefficient.Real * coupling, 1e-9);
        result.Fields[second.PhysicalPins[1].LogicalPin.IDOutFlow].Imaginary.ShouldBe(0);
    }
}
