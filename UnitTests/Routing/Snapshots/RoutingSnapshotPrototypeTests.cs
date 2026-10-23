using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.Components.Parametric;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.LightCalculation.Snapshots;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;

namespace UnitTests.Routing.Snapshots;

public sealed class RoutingSnapshotPrototypeTests
{
    [Fact]
    public async Task ParametricPrototype_KeepsEditablePrivateFormulasAndFullWavelengthDispersion()
    {
        Component? prototype = null;
        Component Factory()
        {
            var component = CrossingTestCircuit.CreateCrossingComponent();
            var pins = component.GetAllPins();
            var slider = new Slider(Guid.NewGuid(), 0, 0.4, 1, 0);
            component.AddSlider(0, slider);
            var definition = new ParametricSMatrixSnapshot(
                [new ParameterDefinition("gain", 0.4, 0, 1, sliderNumber: 0)],
                [new FormulaConnection("port 1", "port 2", "gain", "0")]);
            component.WaveLengthToSMatrixMap[1550] = ParametricSMatrixFactory.Build(pins, [slider], definition);
            component.ParameterDefinitions = definition.Parameters;
            component.WaveguideDispersion = new PolynomialDispersion(1550, 2.5, n1: 0.001, loss0: 1, lossSlope: 0.01);
            prototype = component;
            return component;
        }
        var fixture = CrossingTestCircuit.Build(0.0001, Factory);
        var components = new[] { fixture.ALeft.Component, fixture.ARight.Component, fixture.BTop.Component, fixture.BBottom.Component }
            .Concat(fixture.AddedCrossings).ToArray();
        var snapshot = RoutingInputSnapshot.Capture(components, fixture.Router, fixture.Manager);
        prototype.ShouldNotBeNull();
        var sourceMatrix = prototype.WaveLengthToSMatrixMap[1550];
        sourceMatrix.ParametricRebuild = (_, _) => throw new InvalidOperationException("Live parametric factory.");
        foreach (var key in sourceMatrix.NonLinearConnections.Keys.ToArray())
            sourceMatrix.NonLinearConnections[key] = sourceMatrix.NonLinearConnections[key] with
                { CalcConnectionWeightAsync = _ => throw new InvalidOperationException("Live formula.") };

        var factory = snapshot.CreatePrivateGraph().Manager.CrossingInsertion!.CrossingComponentFactory;
        var first = factory().ShouldNotBeNull();
        var second = factory().ShouldNotBeNull();
        first.GetSlider(0)!.Value = 0.8;
        var copiedAgain = ((Component)first.Clone());
        foreach (var component in new[] { first, second, copiedAgain })
        {
            component.ParameterDefinitions.Single().Name.ShouldBe("gain");
            component.WaveguideDispersion!.NEffAt(1500).ShouldBe(2.45, 1e-12);
            component.WaveguideDispersion.LossDbPerCmAt(1600).ShouldBe(1.5, 1e-12);
            var input = component.PhysicalPins.Single(pin => pin.Name == "port 1").LogicalPin.IDInFlow;
            var output = component.PhysicalPins.Single(pin => pin.Name == "port 2").LogicalPin.IDOutFlow;
            var physics = SimulationSnapshotCapture.Capture([component], [], [new(input, 1550, Complex.One)], false);
            var result = await SimulationSnapshotSolver.RunAsync(physics).WaitAsync(TimeSpan.FromSeconds(5));
            result.Fields[output].Magnitude.ShouldBe(component == second ? 0.4 : 0.8, 1e-10);
        }
    }

    [Fact]
    public void FreshCrossingPins_ResolveCapturedPairSpecificProcessFloorsWithoutLiveProvider()
    {
        var fixture = CrossingTestCircuit.Build(0.0001);
        var allowProvider = true;
        fixture.Router.ConnectionProcessFloorProvider = (start, end) =>
        {
            if (!allowProvider) throw new InvalidOperationException("Live process provider.");
            return start.Name == "port 1" || end.Name == "port 1" ? 27 : 39;
        };
        var components = new[] { fixture.ALeft.Component, fixture.ARight.Component, fixture.BTop.Component, fixture.BBottom.Component };
        var snapshot = RoutingInputSnapshot.Capture(components, fixture.Router, fixture.Manager);
        allowProvider = false;
        var graph = snapshot.CreatePrivateGraph();
        var crossing = graph.Manager.CrossingInsertion!.CrossingComponentFactory().ShouldNotBeNull();
        var terminal = graph.Components.Single(component => component.Id == fixture.ALeft.Component.Id).PhysicalPins[0];
        graph.Router.ResolveProcessFloorFor(terminal, crossing.PhysicalPins.Single(pin => pin.Name == "port 1")).ShouldBe(27);
        graph.Router.ResolveProcessFloorFor(terminal, crossing.PhysicalPins.Single(pin => pin.Name == "port 2")).ShouldBe(39);
    }
}
