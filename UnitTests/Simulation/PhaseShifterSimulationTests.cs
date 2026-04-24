using System.Globalization;
using System.Numerics;
using System.Threading;
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

    // ── Regression guards added in the post-review fix pass ──────────────────

    [Fact]
    public void ClonedPhaseShifter_DoesNotCrash_AndKeepsParametricBehavior()
    {
        // Clone() used to throw because MathExpressionReader tried to re-parse
        // the raw-formula string "mag=1.0;phase=phase_shift", which is not
        // valid NCalc syntax. The SMatrix now carries a ParametricRebuild
        // factory that Clone() uses instead; this test verifies (a) no
        // exception, and (b) the clone still evaluates the phase correctly.
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);
        var original = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        var clone = (CAP_Core.Components.Core.Component)original.Clone();

        var sMatrix = clone.WaveLengthToSMatrixMap[1550];
        sMatrix.NonLinearConnections.Count.ShouldBeGreaterThan(0,
            "Cloned component must retain parametric connections");

        var connFn = sMatrix.NonLinearConnections.First().Value;
        var at90 = connFn.CalcConnectionWeightAsync(new List<object> { 90.0 });
        at90.Magnitude.ShouldBe(1.0, Tolerance);
        at90.Imaginary.ShouldBe(1.0, Tolerance); // phase=90° → (0, 1)
        at90.Real.ShouldBe(0.0, 1e-9);
    }

    [Fact]
    public void ClonedPhaseShifter_HasIndependentParameterState()
    {
        // Multi-instance isolation: two clones must not share one
        // ParametricSMatrix._currentValues dictionary, or setting slider on
        // instance A would leak into instance B's evaluation.
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);
        var a = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var b = (CAP_Core.Components.Core.Component)a.Clone();

        // Call A's connection function at 180° and B's at 0°. If state were
        // shared, B's evaluation would pick up the 180° A just set.
        var aFn = a.WaveLengthToSMatrixMap[1550].NonLinearConnections.First().Value;
        var bFn = b.WaveLengthToSMatrixMap[1550].NonLinearConnections.First().Value;

        var aAt180 = aFn.CalcConnectionWeightAsync(new List<object> { 180.0 });
        var bAt0   = bFn.CalcConnectionWeightAsync(new List<object> { 0.0 });

        aAt180.Real.ShouldBe(-1.0, Tolerance);
        bAt0.Real.ShouldBe(1.0, Tolerance);
    }

    [Fact]
    public void LoadingPdk_WithOutOfRangeSliderNumber_ThrowsAtLoadTime()
    {
        // Bad PDKs must fail at load time, not silently produce a broken
        // simulation. Slider count on this component is 1, so sliderNumber=5
        // is invalid.
        var draft = CreatePhaseShifterDraft();
        draft.SMatrix!.Parameters![0].SliderNumber = 5;

        var ex = Should.Throw<InvalidOperationException>(() =>
            ParametricSMatrixMapper.Validate(draft.SMatrix, draft.Name, draft.Pins, draft.Sliders!.Count));
        ex.Message.ShouldContain("sliderNumber 5");
        ex.Message.ShouldContain("only 1 slider");
    }

    [Fact]
    public void LoadingPdk_WithSliderNumberOnComponentWithoutSliders_ThrowsAtLoadTime()
    {
        var draft = CreatePhaseShifterDraft();
        draft.Sliders = new List<SliderDraft>();
        // SliderNumber is 0 in the default draft; still invalid because the
        // component has zero sliders.

        var ex = Should.Throw<InvalidOperationException>(() =>
            ParametricSMatrixMapper.Validate(draft.SMatrix!, draft.Name, draft.Pins, sliderCount: 0));
        ex.Message.ShouldContain("only 0 slider");
    }

    [Fact]
    public void LoadingPdk_WithoutSliderBinding_IsAccepted()
    {
        // A parameter without SliderNumber = null is legal: the formula
        // evaluates against the parameter's DefaultValue. This case is how
        // constants-with-named-labels would work.
        var draft = CreatePhaseShifterDraft();
        draft.SMatrix!.Parameters![0].SliderNumber = null;

        Should.NotThrow(() =>
            ParametricSMatrixMapper.Validate(draft.SMatrix, draft.Name, draft.Pins, draft.Sliders!.Count));
    }

    [Fact]
    public void UnboundParameter_EvaluatesAgainstDefaultValue()
    {
        // Behavioural follow-up to the NotThrow test above: null SliderNumber
        // plus DefaultValue=90 must actually produce phase=π/2 on evaluation.
        // A regression that silently forces unbound parameters to 0 would
        // fail here.
        var parameters = new[] { new ParameterDefinition("phase_shift", defaultValue: 90, minValue: 0, maxValue: 360) };
        var connections = new[] { new FormulaConnection("in", "out", "1.0", "phase_shift") };
        var parametric = new ParametricSMatrix(parameters, connections);

        var result = parametric.EvaluateConnections();

        result[0].Value.Magnitude.ShouldBe(1.0, Tolerance);
        result[0].Value.Imaginary.ShouldBe(1.0, Tolerance);
        result[0].Value.Real.ShouldBe(0.0, 1e-9);
    }

    [Fact]
    public void ParameterDefinitionDraft_SliderNumberNull_RoundTripsThroughJson()
    {
        // Guards the JSON contract: a null SliderNumber must survive
        // serialize → deserialize. A JsonIgnoreCondition regression that
        // drops the field would silently turn null into 0 on reload.
        var draft = new ParameterDefinitionDraft
        {
            Name = "phase_shift",
            DefaultValue = 45,
            MinValue = 0,
            MaxValue = 360,
            SliderNumber = null,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(draft);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<ParameterDefinitionDraft>(json);

        reloaded.ShouldNotBeNull();
        reloaded!.SliderNumber.ShouldBeNull();
    }

    [Fact]
    public void ParameterDefinitionDraft_SliderNumberZero_RoundTripsThroughJson()
    {
        // Mirror test for the bound case: 0 must stay 0, not default to null.
        var draft = new ParameterDefinitionDraft
        {
            Name = "phase_shift",
            DefaultValue = 45,
            MinValue = 0,
            MaxValue = 360,
            SliderNumber = 0,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(draft);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<ParameterDefinitionDraft>(json);

        reloaded!.SliderNumber.ShouldBe(0);
    }

    [Fact]
    public void DoubleCloned_PhaseShifter_StillEvaluatesCorrectly()
    {
        // Clone chain preservation: ParametricRebuild must carry forward
        // through multiple clones so that the grandchild of the original
        // component still has a working parametric evaluator, not the
        // crashing MathExpressionReader fallback.
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);
        var original = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var firstClone = (CAP_Core.Components.Core.Component)original.Clone();
        var secondClone = (CAP_Core.Components.Core.Component)firstClone.Clone();

        var connFn = secondClone.WaveLengthToSMatrixMap[1550].NonLinearConnections.First().Value;
        var at90 = connFn.CalcConnectionWeightAsync(new List<object> { 90.0 });

        at90.Magnitude.ShouldBe(1.0, Tolerance);
        at90.Imaginary.ShouldBe(1.0, Tolerance);
        at90.Real.ShouldBe(0.0, 1e-9);
    }

    [Fact]
    public void PhaseSweep_0To360_RotatesCounterclockwise_Unnormalized()
    {
        // Sign-flip sensitivity: without raw (non-normalized) phase checks,
        // a sign-flip bug on deg→rad still passes the 0/180 assertions.
        // Here we walk the slider in 15° steps and verify atan2(Im, Re)
        // actually follows the slider value (unwrapping by accumulating).
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var connFn = component.WaveLengthToSMatrixMap[1550].NonLinearConnections.First().Value;

        double lastPhase = 0;
        double accumulated = 0;
        for (int deg = 0; deg <= 360; deg += 15)
        {
            var c = connFn.CalcConnectionWeightAsync(new List<object> { (double)deg });
            double phase = Math.Atan2(c.Imaginary, c.Real);

            // Unwrap: assume forward rotation, add 2π on branch cut.
            double delta = phase - lastPhase;
            if (delta < -Math.PI) delta += 2 * Math.PI;
            accumulated += delta;
            lastPhase = phase;

            double expectedRad = deg * Math.PI / 180.0;
            accumulated.ShouldBe(expectedRad, 1e-6,
                $"Phase at {deg}° should trace a counterclockwise rotation");
        }
    }

    [Fact]
    public void TwoPhaseShifters_Combined_PhasesAddModuloTwoPi()
    {
        // Physical composition check: the complex product of two parametric
        // connection outputs (e.g. two phase shifters in series) should have
        // phase equal to the sum of the two slider settings. Catches any
        // accidental global-state sharing between parametric VMs.
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);
        var a = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var b = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        var aFn = a.WaveLengthToSMatrixMap[1550].NonLinearConnections.First().Value;
        var bFn = b.WaveLengthToSMatrixMap[1550].NonLinearConnections.First().Value;

        var c1 = aFn.CalcConnectionWeightAsync(new List<object> { 60.0 });
        var c2 = bFn.CalcConnectionWeightAsync(new List<object> { 120.0 });
        var composed = c1 * c2;

        composed.Real.ShouldBe(-1.0, 1e-9);
        composed.Imaginary.ShouldBe(0.0, 1e-9);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void FormulaEvaluator_ParsesDecimalLiterals_CultureInvariant(string cultureName)
    {
        // NCalc defaults to thread culture. Without InvariantCulture the
        // literal "0.707" on de-DE parses as 707. Pin the promised
        // invariant-culture behaviour across the three most likely locales.
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);
        try
        {
            var evaluator = new FormulaEvaluator();
            var result = evaluator.Evaluate("0.707", new Dictionary<string, double>());
            result.ShouldBe(0.707, 1e-9);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void NonParametric_Component_StillUsesStaticFactory()
    {
        // Regression guard: the new parametric branch must not divert
        // non-parametric components from their classic CreateSMatrix path.
        var draft = CreatePhaseShifterDraft();
        draft.SMatrix = new PdkSMatrixDraft
        {
            WavelengthNm = 1550,
            Connections = new List<SMatrixConnection>
            {
                new() { FromPin = "in", ToPin = "out", Magnitude = 1.0, PhaseDegrees = 0 }
            }
        };
        draft.Sliders = new List<SliderDraft>();

        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);

        template.CreateSMatrix.ShouldNotBeNull("Static S-matrix must still use the static factory");
        template.CreateSMatrixWithSliders.ShouldBeNull("Static S-matrix must not be routed through the parametric factory");
    }

    [Fact]
    public void PhaseShifter_At360_ApproximatelyEqualsAt0()
    {
        // Boundary: 360° and 0° are physically identical endpoints on the
        // circular parameter. A clamping bug (Math.Clamp(360, 0, 360) works,
        // but also a negative-one-sentinel-leak could break this).
        var draft = CreatePhaseShifterDraft();
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "Test PDK", null);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var connFn = component.WaveLengthToSMatrixMap[1550].NonLinearConnections.First().Value;

        var at0 = connFn.CalcConnectionWeightAsync(new List<object> { 0.0 });
        var at360 = connFn.CalcConnectionWeightAsync(new List<object> { 360.0 });

        at0.Real.ShouldBe(at360.Real, 1e-9);
        at0.Imaginary.ShouldBe(at360.Imaginary, 1e-9);
    }
}
