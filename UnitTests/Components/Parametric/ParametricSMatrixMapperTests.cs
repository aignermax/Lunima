using Xunit;
using Shouldly;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace UnitTests.Components.Parametric
{
    public class ParametricSMatrixMapperTests
    {
        [Fact]
        public void IsParametric_FixedConnections_ReturnsFalse()
        {
            var draft = new PdkSMatrixDraft
            {
                Connections = new List<SMatrixConnection>
                {
                    new() { FromPin = "in", ToPin = "out", Magnitude = 0.707 }
                }
            };

            ParametricSMatrixMapper.IsParametric(draft).ShouldBeFalse();
        }

        [Fact]
        public void IsParametric_WithParameters_ReturnsTrue()
        {
            var draft = new PdkSMatrixDraft
            {
                Parameters = new List<ParameterDefinitionDraft>
                {
                    new() { Name = "k", MinValue = 0, MaxValue = 1 }
                },
                Connections = new List<SMatrixConnection>
                {
                    new()
                    {
                        FromPin = "in", ToPin = "out",
                        MagnitudeFormula = "Sqrt(k)"
                    }
                }
            };

            ParametricSMatrixMapper.IsParametric(draft).ShouldBeTrue();
        }

        [Fact]
        public void IsParametric_WithFormulaButNoParameters_ReturnsTrue()
        {
            var draft = new PdkSMatrixDraft
            {
                Connections = new List<SMatrixConnection>
                {
                    new()
                    {
                        FromPin = "in", ToPin = "out",
                        MagnitudeFormula = "0.707"
                    }
                }
            };

            ParametricSMatrixMapper.IsParametric(draft).ShouldBeTrue();
        }

        [Fact]
        public void MapToParametricSMatrix_FixedValues_MapsCorrectly()
        {
            var draft = new PdkSMatrixDraft
            {
                Connections = new List<SMatrixConnection>
                {
                    new()
                    {
                        FromPin = "in", ToPin = "out",
                        Magnitude = 0.707, PhaseDegrees = 45
                    }
                }
            };

            var parametric = ParametricSMatrixMapper.MapToParametricSMatrix(draft);
            var results = parametric.EvaluateConnections();

            results.Count.ShouldBe(1);
            results[0].FromPin.ShouldBe("in");
            results[0].ToPin.ShouldBe("out");
            results[0].Value.Magnitude.ShouldBe(0.707, 1e-10);
        }

        [Fact]
        public void MapToParametricSMatrix_WithFormulas_MapsCorrectly()
        {
            var draft = new PdkSMatrixDraft
            {
                Parameters = new List<ParameterDefinitionDraft>
                {
                    new()
                    {
                        Name = "coupling_ratio",
                        DefaultValue = 0.5,
                        MinValue = 0,
                        MaxValue = 1
                    }
                },
                Connections = new List<SMatrixConnection>
                {
                    new()
                    {
                        FromPin = "in", ToPin = "out1",
                        MagnitudeFormula = "Sqrt(coupling_ratio)",
                        PhaseDegreesFormula = "0"
                    },
                    new()
                    {
                        FromPin = "in", ToPin = "out2",
                        MagnitudeFormula = "Sqrt(1 - coupling_ratio)",
                        PhaseDegreesFormula = "90"
                    }
                }
            };

            var parametric = ParametricSMatrixMapper.MapToParametricSMatrix(draft);

            parametric.Parameters.Count.ShouldBe(1);
            parametric.Parameters[0].Name.ShouldBe("coupling_ratio");
            parametric.Parameters[0].DefaultValue.ShouldBe(0.5);

            var results = parametric.EvaluateConnections();
            results.Count.ShouldBe(2);
            results[0].Value.Magnitude.ShouldBe(Math.Sqrt(0.5), 1e-10);
            results[1].Value.Magnitude.ShouldBe(Math.Sqrt(0.5), 1e-10);
        }

        [Fact]
        public void Validate_DuplicateParameterNames_ThrowsException()
        {
            var draft = new PdkSMatrixDraft
            {
                Parameters = new List<ParameterDefinitionDraft>
                {
                    new() { Name = "k", MinValue = 0, MaxValue = 1 },
                    new() { Name = "k", MinValue = 0, MaxValue = 1 }
                },
                Connections = new List<SMatrixConnection>()
            };
            var pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in" }
            };

            Should.Throw<InvalidOperationException>(
                () => ParametricSMatrixMapper.Validate(draft, "TestComp", pins));
        }

        [Fact]
        public void Validate_ParameterMinGreaterThanMax_ThrowsException()
        {
            var draft = new PdkSMatrixDraft
            {
                Parameters = new List<ParameterDefinitionDraft>
                {
                    new() { Name = "k", MinValue = 1.0, MaxValue = 0.0 }
                },
                Connections = new List<SMatrixConnection>()
            };
            var pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in" }
            };

            Should.Throw<InvalidOperationException>(
                () => ParametricSMatrixMapper.Validate(draft, "TestComp", pins));
        }

        [Fact]
        public void Validate_UnknownPinInFormula_ThrowsException()
        {
            var draft = new PdkSMatrixDraft
            {
                Parameters = new List<ParameterDefinitionDraft>
                {
                    new() { Name = "k", MinValue = 0, MaxValue = 1, DefaultValue = 0.5 }
                },
                Connections = new List<SMatrixConnection>
                {
                    new()
                    {
                        FromPin = "in", ToPin = "nonexistent",
                        MagnitudeFormula = "Sqrt(k)"
                    }
                }
            };
            var pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in" },
                new() { Name = "out" }
            };

            Should.Throw<InvalidOperationException>(
                () => ParametricSMatrixMapper.Validate(draft, "TestComp", pins));
        }

        [Fact]
        public void Validate_InvalidFormula_ThrowsException()
        {
            var draft = new PdkSMatrixDraft
            {
                Parameters = new List<ParameterDefinitionDraft>
                {
                    new() { Name = "k", MinValue = 0, MaxValue = 1, DefaultValue = 0.5 }
                },
                Connections = new List<SMatrixConnection>
                {
                    new()
                    {
                        FromPin = "in", ToPin = "out",
                        MagnitudeFormula = "Sqrt(unknown_var)"
                    }
                }
            };
            var pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in" },
                new() { Name = "out" }
            };

            Should.Throw<InvalidOperationException>(
                () => ParametricSMatrixMapper.Validate(draft, "TestComp", pins));
        }
    }
}
