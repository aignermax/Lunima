using System.Numerics;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Parametric;
using CAP_Core.LightCalculation;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// Tests that Phase Shifter slider correctly controls S-matrix phase during simulation.
/// </summary>
public class PhaseShifterSimulationTests
{
    private const double Tolerance = 1e-10;

    /// <summary>
    /// Creates a minimal Phase Shifter PDK draft with a parametric S-matrix.
    /// </summary>
    private static PdkComponentDraft CreatePhaseShifterDraft() => new()
    {
        Name = "Phase Shifter",
        Category = "Modulators",
        NazcaFunction = "demo.eopm_dc",
        WidthMicrometers = 500,
        HeightMicrometers = 60,
        NazcaOriginOffsetX = 0,
        NazcaOriginOffsetY = 30,
        Pins = new List<PhysicalPinDraft>
        {
            new() { Name = "in",  OffsetXMicrometers = 0,   OffsetYMicrometers = 30, AngleDegrees = 180 },
            new() { Name = "out", OffsetXMicrometers = 500, OffsetYMicrometers = 30, AngleDegrees = 0 }
        },
        Sliders = new List<SliderDraft>
        {
            new() { SliderNumber = 0, MinVal = 0, MaxVal = 360 }
        },
        SMatrix = new PdkSMatrixDraft
        {
            WavelengthNm = 1550,
            Parameters = new List<ParameterDefinitionDraft>
            {
                new() { Name = "phase_shift", SliderNumber = 0, DefaultValue = 0, MinValue = 0, MaxValue = 360 }
            },
            Connections = new List<SMatrixConnection>
            {
                new() { FromPin = "in", ToPin = "out", MagnitudeFormula = "1.0", PhaseDegreesFormula = "phase_shift" }
            }
        }
    };

    [Fact]
    public void ConvertToTemplate_ParametricPhaseShifter_UsesCreateSMatrixWithSliders()
    {
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);

        template.CreateSMatrixWithSliders.ShouldNotBeNull(
            "Parametric SMatrix should use CreateSMatrixWithSliders factory");
        template.CreateSMatrix.ShouldBeNull(
            "Parametric SMatrix should not use static CreateSMatrix factory");
    }

    [Fact]
    public void CreateFromTemplate_PhaseShifter_HasNonLinearConnections()
    {
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        var sMatrix = component.WaveLengthToSMatrixMap[1550];
        sMatrix.NonLinearConnections.Count.ShouldBeGreaterThan(0,
            "Phase Shifter should have NonLinear connections for dynamic phase");
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(90.0, 1.0, 90.0)]
    [InlineData(180.0, 1.0, 180.0)]
    [InlineData(270.0, 1.0, 270.0)]
    public void PhaseShifterNonLinearConnection_AtSliderDegrees_ReturnsCorrectComplex(
        double sliderDegrees, double expectedMagnitude, double expectedPhaseDeg)
    {
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        var sMatrix = component.WaveLengthToSMatrixMap[1550];
        sMatrix.NonLinearConnections.Count.ShouldBeGreaterThan(0);

        // Take any NonLinear connection and call it with the slider value
        var connFn = sMatrix.NonLinearConnections.First().Value;
        var parameters = new List<object> { sliderDegrees };

        var result = connFn.CalcConnectionWeightAsync(parameters);

        result.Magnitude.ShouldBe(expectedMagnitude, Tolerance,
            $"Magnitude should be 1.0 at {sliderDegrees}°");

        double expectedPhaseRad = expectedPhaseDeg * Math.PI / 180.0;
        // Normalize both phases to [0, 2π) for comparison
        static double Normalize(double rad) => ((rad % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
        Normalize(result.Phase).ShouldBe(Normalize(expectedPhaseRad), Tolerance,
            $"Phase should be {expectedPhaseDeg}° at slider {sliderDegrees}°");
    }

    [Fact]
    public void ParametricSMatrix_PhaseFormula_EvaluatesCorrectly()
    {
        var parameters = new[] { new ParameterDefinition("phase_shift", 0, 0, 360) };
        var connections = new[] { new FormulaConnection("in", "out", "1.0", "phase_shift") };
        var parametric = new ParametricSMatrix(parameters, connections);

        // 0° → phase=0
        parametric.SetParameterValue("phase_shift", 0);
        var at0 = parametric.EvaluateConnections();
        at0[0].Value.Magnitude.ShouldBe(1.0, Tolerance);
        at0[0].Value.Phase.ShouldBe(0.0, Tolerance);

        // 90° → phase=π/2
        parametric.SetParameterValue("phase_shift", 90);
        var at90 = parametric.EvaluateConnections();
        at90[0].Value.Phase.ShouldBe(Math.PI / 2, Tolerance);

        // 180° → phase=π (complex value ≈ -1)
        parametric.SetParameterValue("phase_shift", 180);
        var at180 = parametric.EvaluateConnections();
        at180[0].Value.Real.ShouldBe(-1.0, Tolerance);
        at180[0].Value.Imaginary.ShouldBe(0.0, 1e-6);
    }

    [Fact]
    public void DemoPdk_PhaseShifter_IsLoadedAsParametric()
    {
        var templates = TestPdkLoader.LoadFromPdk("demo-pdk.json");
        var phaseShifter = templates.FirstOrDefault(t => t.Name == "Phase Shifter");

        phaseShifter.ShouldNotBeNull("Phase Shifter should exist in demo-pdk.json");
        phaseShifter!.CreateSMatrixWithSliders.ShouldNotBeNull(
            "Phase Shifter in demo-pdk.json should use parametric S-matrix");
        phaseShifter.HasSlider.ShouldBeTrue("Phase Shifter should have a slider");
    }

    [Fact]
    public void DemoPdk_PhaseShifter_SliderChangesPhaseOfConnection()
    {
        var templates = TestPdkLoader.LoadFromPdk("demo-pdk.json");
        var template = templates.First(t => t.Name == "Phase Shifter");
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        var sMatrix = component.WaveLengthToSMatrixMap[1550];
        sMatrix.NonLinearConnections.Count.ShouldBeGreaterThan(0);

        var connFn = sMatrix.NonLinearConnections.First().Value;

        // Phase at 0°: should be 1+0i
        var at0 = connFn.CalcConnectionWeightAsync(new List<object> { 0.0 });
        at0.Magnitude.ShouldBe(1.0, Tolerance);
        at0.Phase.ShouldBe(0.0, Tolerance);

        // Phase at 180°: should be -1+0i (destructive interference)
        var at180 = connFn.CalcConnectionWeightAsync(new List<object> { 180.0 });
        at180.Magnitude.ShouldBe(1.0, Tolerance);
        at180.Real.ShouldBe(-1.0, Tolerance);
    }
}
