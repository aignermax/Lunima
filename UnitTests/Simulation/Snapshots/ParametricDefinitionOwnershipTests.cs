using CAP_Core.Components.Parametric;
using CAP_Core.LightCalculation.Snapshots;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;

namespace UnitTests.Simulation.Snapshots;

public sealed class ParametricDefinitionOwnershipTests
{
    [Fact]
    public void MatrixCapture_CopiesDefinitionValuesWithoutRetainingSubclassObjectGraphs()
    {
        var source = CrossingTestCircuit.CreateCrossingComponent();
        var parameter = new EditorParameter(new object());
        var formula = new EditorFormula(new object());
        source.WaveLengthToSMatrixMap[1550].ParametricSnapshot = new([parameter], [formula]);

        var snapshot = ComponentMatrixSnapshot.Capture(source).Matrices[1550];

        snapshot.Parameters.Single().GetType().ShouldBe(typeof(ParameterDefinition));
        snapshot.Parameters.Single().ShouldNotBeSameAs(parameter);
        snapshot.Parameters.Single().Label.ShouldBe("Gain editor");
        snapshot.Parameters.Single().Unit.ShouldBe("ratio");
        snapshot.Parameters.Single().SliderNumber.ShouldBe(0);
        snapshot.ParametricConnections.Single().GetType().ShouldBe(typeof(FormulaConnection));
        snapshot.ParametricConnections.Single().ShouldNotBeSameAs(formula);
        snapshot.ParametricConnections.Single().MagnitudeFormula.ShouldBe("gain");
        snapshot.ParametricConnections.Single().PhaseDegFormula.ShouldBe("35");
    }

    [Fact]
    public void CrossingPrototype_CopiesParameterMetadataWithoutRetainingEditorSubclass()
    {
        var source = CrossingTestCircuit.CreateCrossingComponent();
        var parameter = new EditorParameter(new object());
        source.ParameterDefinitions = [parameter];

        var prototype = CrossingPrototypeValue.Capture(source);

        prototype.ParameterDefinitions.Single().GetType().ShouldBe(typeof(ParameterDefinition));
        prototype.ParameterDefinitions.Single().ShouldNotBeSameAs(parameter);
        prototype.ParameterDefinitions.Single().Name.ShouldBe("gain");
        prototype.ParameterDefinitions.Single().DefaultValue.ShouldBe(0.4);
        prototype.ParameterDefinitions.Single().MinValue.ShouldBe(0);
        prototype.ParameterDefinitions.Single().MaxValue.ShouldBe(1);
    }

    private sealed class EditorParameter(object editor)
        : ParameterDefinition("gain", 0.4, 0, 1, "Gain editor", 0, "ratio")
    {
        public object Editor { get; } = editor;
    }

    private sealed class EditorFormula(object editor) : FormulaConnection("port 1", "port 2", "gain", "35")
    {
        public object Editor { get; } = editor;
    }
}
