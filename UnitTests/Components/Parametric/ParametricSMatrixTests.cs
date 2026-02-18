using Xunit;
using Shouldly;
using CAP_Core.Components.Parametric;

namespace UnitTests.Components.Parametric
{
    public class ParametricSMatrixTests
    {
        [Fact]
        public void Constructor_WithValidInput_CreatesInstance()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "Sqrt(coupling_ratio)", "0")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);

            sMatrix.Parameters.Count.ShouldBe(1);
            sMatrix.Connections.Count.ShouldBe(1);
        }

        [Fact]
        public void GetParameterValue_ReturnsDefaultValue()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "coupling_ratio", "0")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);

            sMatrix.GetParameterValue("coupling_ratio").ShouldBe(0.5);
        }

        [Fact]
        public void SetParameterValue_UpdatesValue()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "coupling_ratio", "0")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);
            sMatrix.SetParameterValue("coupling_ratio", 0.8);

            sMatrix.GetParameterValue("coupling_ratio").ShouldBe(0.8);
        }

        [Fact]
        public void SetParameterValue_ClampsToRange()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "coupling_ratio", "0")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);
            sMatrix.SetParameterValue("coupling_ratio", 1.5);

            sMatrix.GetParameterValue("coupling_ratio").ShouldBe(1.0);
        }

        [Fact]
        public void SetParameterValue_ClampsToMinimum()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "coupling_ratio", "0")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);
            sMatrix.SetParameterValue("coupling_ratio", -0.5);

            sMatrix.GetParameterValue("coupling_ratio").ShouldBe(0.0);
        }

        [Fact]
        public void SetParameterValue_RaisesEvent()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "coupling_ratio", "0")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);
            bool eventRaised = false;
            sMatrix.ParameterChanged += (_, _) => eventRaised = true;

            sMatrix.SetParameterValue("coupling_ratio", 0.8);

            eventRaised.ShouldBeTrue();
        }

        [Fact]
        public void SetParameterValue_SameValue_DoesNotRaiseEvent()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "coupling_ratio", "0")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);
            bool eventRaised = false;
            sMatrix.ParameterChanged += (_, _) => eventRaised = true;

            sMatrix.SetParameterValue("coupling_ratio", 0.5);

            eventRaised.ShouldBeFalse();
        }

        [Fact]
        public void EvaluateConnections_WithDefaultValues_ReturnsCorrectResult()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out1", "Sqrt(coupling_ratio)", "0"),
                new FormulaConnection("in", "out2", "Sqrt(1 - coupling_ratio)", "90")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);
            var results = sMatrix.EvaluateConnections();

            results.Count.ShouldBe(2);
            results[0].FromPin.ShouldBe("in");
            results[0].ToPin.ShouldBe("out1");
            results[0].Value.Magnitude.ShouldBe(Math.Sqrt(0.5), 1e-10);

            results[1].FromPin.ShouldBe("in");
            results[1].ToPin.ShouldBe("out2");
            results[1].Value.Magnitude.ShouldBe(Math.Sqrt(0.5), 1e-10);
        }

        [Fact]
        public void EvaluateConnections_AfterParameterChange_ReflectsNewValues()
        {
            var parameters = new[]
            {
                new ParameterDefinition("coupling_ratio", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "Sqrt(coupling_ratio)", "0")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);
            sMatrix.SetParameterValue("coupling_ratio", 0.25);
            var results = sMatrix.EvaluateConnections();

            results[0].Value.Magnitude.ShouldBe(0.5, 1e-10);
        }

        [Fact]
        public void EvaluateConnections_PhaseFormula_ComputesCorrectPhase()
        {
            var parameters = new[]
            {
                new ParameterDefinition("phase_shift", 90, 0, 360)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "1.0", "phase_shift")
            };

            var sMatrix = new ParametricSMatrix(parameters, connections);
            var results = sMatrix.EvaluateConnections();

            double expectedPhaseRad = 90.0 * Math.PI / 180.0;
            results[0].Value.Phase.ShouldBe(expectedPhaseRad, 1e-10);
        }

        [Fact]
        public void Constructor_InvalidFormula_ThrowsException()
        {
            var parameters = new[]
            {
                new ParameterDefinition("x", 0.5, 0, 1)
            };
            var connections = new[]
            {
                new FormulaConnection("in", "out", "Sqrt(unknown_param)", "0")
            };

            Should.Throw<InvalidOperationException>(
                () => new ParametricSMatrix(parameters, connections));
        }

        [Fact]
        public void GetParameterValue_UnknownParameter_ThrowsException()
        {
            var sMatrix = new ParametricSMatrix(
                new[] { new ParameterDefinition("x", 0, 0, 1) },
                new[] { new FormulaConnection("in", "out", "x", "0") });

            Should.Throw<ArgumentException>(
                () => sMatrix.GetParameterValue("nonexistent"));
        }

        [Fact]
        public void SetParameterValue_UnknownParameter_ThrowsException()
        {
            var sMatrix = new ParametricSMatrix(
                new[] { new ParameterDefinition("x", 0, 0, 1) },
                new[] { new FormulaConnection("in", "out", "x", "0") });

            Should.Throw<ArgumentException>(
                () => sMatrix.SetParameterValue("nonexistent", 0.5));
        }

        [Fact]
        public void EvaluateConnections_ConstantFormulas_WorksWithoutParameters()
        {
            var connections = new[]
            {
                new FormulaConnection("in", "out", "0.707", "45")
            };

            var sMatrix = new ParametricSMatrix(
                Array.Empty<ParameterDefinition>(), connections);
            var results = sMatrix.EvaluateConnections();

            results.Count.ShouldBe(1);
            results[0].Value.Magnitude.ShouldBe(0.707, 1e-10);
        }
    }
}
