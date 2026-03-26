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

            // Should have 2 connections (forward + reciprocal)
            results.Count.ShouldBe(2);

            results.ShouldContain(c => c.FromPin == "in" && c.ToPin == "out");
            var forward = results.First(c => c.FromPin == "in" && c.ToPin == "out");
            forward.Value.Magnitude.ShouldBe(0.707, 1e-10);

            results.ShouldContain(c => c.FromPin == "out" && c.ToPin == "in");
            var reverse = results.First(c => c.FromPin == "out" && c.ToPin == "in");
            reverse.Value.Magnitude.ShouldBe(0.707, 1e-10);
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
            // Should have 4 connections (2 forward + 2 reciprocal)
            results.Count.ShouldBe(4);

            // Check forward connections
            var inToOut1 = results.First(c => c.FromPin == "in" && c.ToPin == "out1");
            inToOut1.Value.Magnitude.ShouldBe(Math.Sqrt(0.5), 1e-10);

            var inToOut2 = results.First(c => c.FromPin == "in" && c.ToPin == "out2");
            inToOut2.Value.Magnitude.ShouldBe(Math.Sqrt(0.5), 1e-10);

            // Check reciprocal connections
            results.ShouldContain(c => c.FromPin == "out1" && c.ToPin == "in");
            results.ShouldContain(c => c.FromPin == "out2" && c.ToPin == "in");
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

        [Fact]
        public void MapToParametricSMatrix_AutomaticallyAddsReverseConnections()
        {
            // Arrange: MMI 1x2 with only forward connections
            var draft = new PdkSMatrixDraft
            {
                Connections = new List<SMatrixConnection>
                {
                    new() { FromPin = "in", ToPin = "out1", Magnitude = 0.707, PhaseDegrees = 0 },
                    new() { FromPin = "in", ToPin = "out2", Magnitude = 0.707, PhaseDegrees = 0 }
                }
            };

            // Act
            var parametric = ParametricSMatrixMapper.MapToParametricSMatrix(draft);
            var results = parametric.EvaluateConnections();

            // Assert: Should have 4 connections (2 forward + 2 reverse)
            results.Count.ShouldBe(4, "Should automatically add reciprocal connections");

            // Check forward connections exist
            results.ShouldContain(c => c.FromPin == "in" && c.ToPin == "out1");
            results.ShouldContain(c => c.FromPin == "in" && c.ToPin == "out2");

            // Check reverse connections were added
            results.ShouldContain(c => c.FromPin == "out1" && c.ToPin == "in");
            results.ShouldContain(c => c.FromPin == "out2" && c.ToPin == "in");

            // Check magnitudes are preserved
            var out1ToIn = results.First(c => c.FromPin == "out1" && c.ToPin == "in");
            out1ToIn.Value.Magnitude.ShouldBe(0.707, 1e-10);
        }

        [Fact]
        public void MapToParametricSMatrix_DoesNotDuplicateExistingReverseConnections()
        {
            // Arrange: Connection that already has both directions
            var draft = new PdkSMatrixDraft
            {
                Connections = new List<SMatrixConnection>
                {
                    new() { FromPin = "a", ToPin = "b", Magnitude = 0.9, PhaseDegrees = 0 },
                    new() { FromPin = "b", ToPin = "a", Magnitude = 0.9, PhaseDegrees = 0 }
                }
            };

            // Act
            var parametric = ParametricSMatrixMapper.MapToParametricSMatrix(draft);
            var results = parametric.EvaluateConnections();

            // Assert: Should still have exactly 2 connections (no duplicates)
            results.Count.ShouldBe(2, "Should not duplicate existing reverse connections");
        }

        [Fact]
        public void MapToParametricSMatrix_ReciprocityWorksWithFormulas()
        {
            // Arrange: Parametric connection with formula
            var draft = new PdkSMatrixDraft
            {
                Parameters = new List<ParameterDefinitionDraft>
                {
                    new() { Name = "loss", DefaultValue = 0.01, MinValue = 0, MaxValue = 0.1 }
                },
                Connections = new List<SMatrixConnection>
                {
                    new()
                    {
                        FromPin = "in", ToPin = "out",
                        MagnitudeFormula = "Sqrt(1 - loss)",
                        PhaseDegreesFormula = "0"
                    }
                }
            };

            // Act
            var parametric = ParametricSMatrixMapper.MapToParametricSMatrix(draft);
            var results = parametric.EvaluateConnections();

            // Assert: Should have 2 connections with same magnitude formula
            results.Count.ShouldBe(2);
            var reverse = results.First(c => c.FromPin == "out" && c.ToPin == "in");
            reverse.Value.Magnitude.ShouldBe(Math.Sqrt(1 - 0.01), 1e-10);
        }
    }
}
