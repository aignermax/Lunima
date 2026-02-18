using Xunit;
using Shouldly;
using CAP_DataAccess.Components.ComponentDraftMapper;

namespace UnitTests.Components.Parametric
{
    public class PdkLoaderParametricTests
    {
        [Fact]
        public void LoadFromJson_ParametricSMatrix_ParsesParametersCorrectly()
        {
            var loader = new PdkLoader();
            var json = @"{
                ""name"": ""Parametric PDK"",
                ""components"": [{
                    ""name"": ""Tunable Coupler"",
                    ""nazcaFunction"": ""pdk.coupler"",
                    ""widthMicrometers"": 120,
                    ""heightMicrometers"": 50,
                    ""pins"": [
                        { ""name"": ""in"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 25, ""angleDegrees"": 180 },
                        { ""name"": ""out1"", ""offsetXMicrometers"": 120, ""offsetYMicrometers"": 12.5, ""angleDegrees"": 0 },
                        { ""name"": ""out2"", ""offsetXMicrometers"": 120, ""offsetYMicrometers"": 37.5, ""angleDegrees"": 0 }
                    ],
                    ""sMatrix"": {
                        ""wavelengthNm"": 1550,
                        ""parameters"": [
                            { ""name"": ""coupling_ratio"", ""defaultValue"": 0.5, ""minValue"": 0, ""maxValue"": 1, ""label"": ""Coupling Ratio"" }
                        ],
                        ""connections"": [
                            { ""fromPin"": ""in"", ""toPin"": ""out1"", ""magnitudeFormula"": ""Sqrt(coupling_ratio)"", ""phaseDegreesFormula"": ""0"" },
                            { ""fromPin"": ""in"", ""toPin"": ""out2"", ""magnitudeFormula"": ""Sqrt(1 - coupling_ratio)"", ""phaseDegreesFormula"": ""90"" }
                        ]
                    }
                }]
            }";

            var pdk = loader.LoadFromJson(json);
            var comp = pdk.Components[0];

            comp.SMatrix.ShouldNotBeNull();
            comp.SMatrix.Parameters.ShouldNotBeNull();
            comp.SMatrix.Parameters.Count.ShouldBe(1);
            comp.SMatrix.Parameters[0].Name.ShouldBe("coupling_ratio");
            comp.SMatrix.Parameters[0].DefaultValue.ShouldBe(0.5);
            comp.SMatrix.Parameters[0].MinValue.ShouldBe(0);
            comp.SMatrix.Parameters[0].MaxValue.ShouldBe(1);
            comp.SMatrix.Parameters[0].Label.ShouldBe("Coupling Ratio");
        }

        [Fact]
        public void LoadFromJson_ParametricConnections_ParsesFormulasCorrectly()
        {
            var loader = new PdkLoader();
            var json = @"{
                ""name"": ""Parametric PDK"",
                ""components"": [{
                    ""name"": ""Phase Shifter"",
                    ""nazcaFunction"": ""pdk.phase"",
                    ""widthMicrometers"": 200,
                    ""heightMicrometers"": 20,
                    ""pins"": [
                        { ""name"": ""in"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 10, ""angleDegrees"": 180 },
                        { ""name"": ""out"", ""offsetXMicrometers"": 200, ""offsetYMicrometers"": 10, ""angleDegrees"": 0 }
                    ],
                    ""sMatrix"": {
                        ""wavelengthNm"": 1550,
                        ""parameters"": [
                            { ""name"": ""phase_shift"", ""defaultValue"": 0, ""minValue"": 0, ""maxValue"": 360 }
                        ],
                        ""connections"": [
                            { ""fromPin"": ""in"", ""toPin"": ""out"", ""magnitudeFormula"": ""0.99"", ""phaseDegreesFormula"": ""phase_shift"" }
                        ]
                    }
                }]
            }";

            var pdk = loader.LoadFromJson(json);
            var conn = pdk.Components[0].SMatrix!.Connections[0];

            conn.IsParametric.ShouldBeTrue();
            conn.MagnitudeFormula.ShouldBe("0.99");
            conn.PhaseDegreesFormula.ShouldBe("phase_shift");
        }

        [Fact]
        public void LoadFromJson_MixedFixedAndFormula_ParsesCorrectly()
        {
            var loader = new PdkLoader();
            var json = @"{
                ""name"": ""Mixed PDK"",
                ""components"": [{
                    ""name"": ""Mixed Component"",
                    ""nazcaFunction"": ""pdk.mixed"",
                    ""widthMicrometers"": 100,
                    ""heightMicrometers"": 50,
                    ""pins"": [
                        { ""name"": ""in"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 25, ""angleDegrees"": 180 },
                        { ""name"": ""out"", ""offsetXMicrometers"": 100, ""offsetYMicrometers"": 25, ""angleDegrees"": 0 }
                    ],
                    ""sMatrix"": {
                        ""wavelengthNm"": 1550,
                        ""connections"": [
                            { ""fromPin"": ""in"", ""toPin"": ""out"", ""magnitude"": 0.95, ""phaseDegrees"": 0 }
                        ]
                    }
                }]
            }";

            var pdk = loader.LoadFromJson(json);
            var conn = pdk.Components[0].SMatrix!.Connections[0];

            conn.IsParametric.ShouldBeFalse();
            conn.Magnitude.ShouldBe(0.95);
            conn.PhaseDegrees.ShouldBe(0);
        }

        [Fact]
        public void LoadFromJson_InvalidFormulaParameter_ThrowsException()
        {
            var loader = new PdkLoader();
            var json = @"{
                ""name"": ""Invalid PDK"",
                ""components"": [{
                    ""name"": ""Bad Component"",
                    ""nazcaFunction"": ""pdk.bad"",
                    ""widthMicrometers"": 100,
                    ""heightMicrometers"": 50,
                    ""pins"": [
                        { ""name"": ""in"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 25, ""angleDegrees"": 180 },
                        { ""name"": ""out"", ""offsetXMicrometers"": 100, ""offsetYMicrometers"": 25, ""angleDegrees"": 0 }
                    ],
                    ""sMatrix"": {
                        ""wavelengthNm"": 1550,
                        ""parameters"": [
                            { ""name"": ""k"", ""defaultValue"": 0.5, ""minValue"": 0, ""maxValue"": 1 }
                        ],
                        ""connections"": [
                            { ""fromPin"": ""in"", ""toPin"": ""out"", ""magnitudeFormula"": ""Sqrt(unknown_var)"" }
                        ]
                    }
                }]
            }";

            Should.Throw<InvalidOperationException>(() => loader.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromJson_BackwardsCompatible_FixedValuesStillWork()
        {
            var loader = new PdkLoader();
            var json = @"{
                ""name"": ""Legacy PDK"",
                ""components"": [{
                    ""name"": ""Legacy Splitter"",
                    ""nazcaFunction"": ""pdk.splitter"",
                    ""widthMicrometers"": 100,
                    ""heightMicrometers"": 50,
                    ""pins"": [
                        { ""name"": ""in"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 25, ""angleDegrees"": 180 },
                        { ""name"": ""out"", ""offsetXMicrometers"": 100, ""offsetYMicrometers"": 25, ""angleDegrees"": 0 }
                    ],
                    ""sMatrix"": {
                        ""wavelengthNm"": 1550,
                        ""connections"": [
                            { ""fromPin"": ""in"", ""toPin"": ""out"", ""magnitude"": 0.707, ""phaseDegrees"": 45 }
                        ]
                    }
                }]
            }";

            var pdk = loader.LoadFromJson(json);
            var comp = pdk.Components[0];

            comp.SMatrix.ShouldNotBeNull();
            comp.SMatrix.Parameters.ShouldBeNull();
            comp.SMatrix.Connections[0].Magnitude.ShouldBe(0.707);
            comp.SMatrix.Connections[0].PhaseDegrees.ShouldBe(45);
            comp.SMatrix.Connections[0].IsParametric.ShouldBeFalse();
        }
    }
}
