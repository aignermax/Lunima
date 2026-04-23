using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Tests for PicWaveExporter. Cover the three behaviours that matter for safety:
/// (1) components with an S-matrix always become CustomComponent + real data,
/// (2) components without an S-matrix fall back to typed constructors only when
/// their name matches a known pattern, (3) everything else throws — no silent
/// stubs, no guessed physics.
/// </summary>
public class PicWaveExporterTests
{
    private readonly PicWaveExporter _exporter = new();

    // --- Helper factories ---

    private static Component MakeComponent(
        string identifier,
        string nazcaFunctionName = "",
        int pinCount = 0,
        Dictionary<int, SMatrix>? sMatrices = null)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        var comp = new Component(
            sMatrices ?? new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            nazcaFunctionName,
            "",
            parts,
            0,
            identifier,
            DiscreteRotation.R0,
            new List<PhysicalPin>());

        for (int i = 0; i < pinCount; i++)
        {
            comp.PhysicalPins.Add(new PhysicalPin
            {
                Name = i == 0 ? "in" : $"out{i}",
                ParentComponent = comp,
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 5,
                AngleDegrees = 0,
            });
        }
        return comp;
    }

    // --- Identifier sanitisation (observed via emitted script) ---

    [Fact]
    public void Export_AlphanumericIdentifier_AppearsVerbatim()
    {
        var script = _exporter.Export(
            [MakeComponent("comp_1", "ebeam_wg_strip_straight", pinCount: 2)], []);
        script.ShouldContain("'comp_1'");
    }

    [Fact]
    public void Export_HyphenatedIdentifier_HyphenBecomesUnderscore()
    {
        var script = _exporter.Export(
            [MakeComponent("my-wg-1", "ebeam_wg_strip_straight", pinCount: 2)], []);
        script.ShouldContain("'my_wg_1'");
        script.ShouldNotContain("'my-wg-1'");
    }

    [Fact]
    public void Export_LeadingDigitIdentifier_GetsUnderscorePrefix()
    {
        var script = _exporter.Export(
            [MakeComponent("1wg", "ebeam_wg_strip_straight", pinCount: 2)], []);
        script.ShouldContain("'_1wg'");
    }

    // --- Script skeleton ---

    [Fact]
    public void Export_EmptyDesign_EmitsRunnableSkeleton()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("import numpy as np");
        script.ShouldContain("from simphony import Circuit");
        script.ShouldContain("circuit = Circuit()");
        script.ShouldContain("wavelengths = np.linspace(");
        script.ShouldContain("plt.show()");
    }

    [Fact]
    public void Export_HeaderIsHonestAboutPicWaveBeingCOMBased()
    {
        // The script must not claim `pip install picwave` works — PICWave is
        // commercial COM-based. The header should point users to either the
        // real COM workflow or Simphony as an open-source alternative.
        var script = _exporter.Export([], []);

        script.ShouldContain("COM");
        script.ShouldContain("win32com");
        script.ShouldContain("Simphony");
        script.ShouldNotContain("pip install picwave");
    }

    // --- Typed component mapping (no S-matrix path) ---

    [Fact]
    public void Export_StraightWaveguide_EmitsWaveguideConstructor()
    {
        var script = _exporter.Export(
            [MakeComponent("wg1", "ebeam_wg_strip_straight", pinCount: 2)], []);
        script.ShouldContain("Waveguide(");
    }

    [Fact]
    public void Export_DirectionalCoupler_EmitsDirectionalCouplerConstructor()
    {
        var script = _exporter.Export(
            [MakeComponent("dc1", "ebeam_directional_coupler", pinCount: 4)], []);
        script.ShouldContain("DirectionalCoupler(");
    }

    [Fact]
    public void Export_GratingCoupler_EmitsGratingCouplerEvenWhenNameAlsoContainsCoupler()
    {
        // Guards against substring-order bugs: "coupler" is contained in
        // "grating_coupler", so a naive Contains("coupler") check would
        // mis-classify this as a DirectionalCoupler. The mapper must resolve
        // the more specific pattern first.
        var script = _exporter.Export(
            [MakeComponent("gc1", "ebeam_grating_coupler_te1550", pinCount: 1)], []);
        script.ShouldContain("GratingCoupler()");
        script.ShouldNotContain("DirectionalCoupler(");
    }

    [Fact]
    public void Export_Mmi_EmitsMmiWithPortCount()
    {
        var script = _exporter.Export(
            [MakeComponent("mmi1", "demo_mmi2x2_dp", pinCount: 4)], []);
        script.ShouldContain("MMI(ports=4)");
    }

    // --- Loud failure paths ---

    [Fact]
    public void Export_UnknownTypeWithoutSMatrix_Throws()
    {
        // No S-matrix, no name pattern → the exporter refuses to invent physics.
        var comp = MakeComponent("mystery", "weird_unknown_thing", pinCount: 2);

        var ex = Should.Throw<InvalidOperationException>(() => _exporter.Export([comp], []));
        ex.Message.ShouldContain("mystery");
        ex.Message.ShouldContain("neither an S-matrix");
    }

    [Fact]
    public void Export_YJunctionWithoutSMatrix_Throws()
    {
        // A Y-junction is not an MMI (different S-behaviour) and has no generic
        // pattern in the mapper. Previous versions silently emitted MMI(ports=3)
        // — that was physics-unfaithful. Now it throws.
        var comp = MakeComponent("y1", "ebeam_y_1550", pinCount: 3);

        var ex = Should.Throw<InvalidOperationException>(() => _exporter.Export([comp], []));
        ex.Message.ShouldContain("y1");
    }

    [Fact]
    public void Export_ComponentWithoutPins_Throws()
    {
        var comp = MakeComponent("ghost", "ebeam_wg_strip_straight", pinCount: 0);

        var ex = Should.Throw<InvalidOperationException>(() => _exporter.Export([comp], []));
        ex.Message.ShouldContain("no physical pins");
    }

    // --- S-matrix (pin-GUID) path ---

    [Fact]
    public void Export_ComponentWithSMatrix_AlwaysEmitsCustomComponentWithData()
    {
        // Even if the name would match Waveguide, a registered S-matrix must
        // take precedence — the measured data is truthful, the heuristic is a
        // guess.
        var comp = TestComponentFactory.CreatePhaseShifterWithPhysicalPins(forward: new Complex(0, 1));
        comp.Identifier = "ps1";

        var script = _exporter.Export([comp], []);

        script.ShouldContain("CustomComponent(s_matrices=_s_ps1");
        script.ShouldContain("_s_ps1 = {");
        script.ShouldContain("np.array([");
    }

    [Fact]
    public void Export_SMatrixAtDifferentWavelengths_StillUsesCustomComponent()
    {
        // Regression: SiEPIC grating couplers register S-matrix data at 1500nm,
        // 1509nm, 1521nm, ..., 1600nm — never exactly at 1550nm. The exporter
        // used to require an exact ContainsKey(wavelengthNm) match, causing the
        // emitted _s_ dict to become dead code and the component to fall back
        // to a generic GratingCoupler() that ignored the real measured data.
        var comp = TestComponentFactory.CreatePhaseShifterWithPhysicalPins(forward: new Complex(0.8, 0));
        comp.Identifier = "gc_like";
        comp.NazcaFunctionName = "ebeam_gc_te1550";

        // Move the S-matrix registration off the default 1550nm target.
        // (PhaseShifter factory registers at RedNM = 1550; swap it to 1549.)
        var sMatrixAt1550 = comp.WaveLengthToSMatrixMap[StandardWaveLengths.RedNM];
        comp.WaveLengthToSMatrixMap.Remove(StandardWaveLengths.RedNM);
        comp.WaveLengthToSMatrixMap[1549] = sMatrixAt1550;

        var script = _exporter.Export([comp], [], wavelengthNm: StandardWaveLengths.RedNM);

        // S-matrix data for the component is emitted
        script.ShouldContain("_s_gc_like = {");
        // AND the component uses it — not a dead dict above a typed constructor
        script.ShouldContain("CustomComponent(s_matrices=_s_gc_like");
        script.ShouldNotContain("GratingCoupler()");
    }

    [Fact]
    public void Export_PhaseShifterAsymmetric_ForwardAndBackwardEmittedIndependently()
    {
        // Forward = i → row 1 col 0 (S21) = 0 + 1j
        // Backward = 0.5 → row 0 col 1 (S12) = 0.5 + 0j
        var comp = TestComponentFactory.CreatePhaseShifterWithPhysicalPins(
            forward: new Complex(0, 1),
            backward: new Complex(0.5, 0));
        comp.Identifier = "ps_asym";

        var script = _exporter.Export([comp], [],
            wavelengthNm: StandardWaveLengths.RedNM);

        // S21 entry (row 1, col 0) should be the imaginary unit
        script.ShouldContain("0+1j");
        // S12 entry (row 0, col 1) should be the 0.5 real
        script.ShouldContain("0.5+0j");
    }

    // --- Connections ---

    [Fact]
    public void Export_ConnectionBetweenTwoKnownComponents_EmitsCircuitConnect()
    {
        var a = MakeComponent("a", "ebeam_wg_strip_straight", pinCount: 2);
        var b = MakeComponent("b", "ebeam_grating_coupler_te1550", pinCount: 1);
        var conn = new WaveguideConnection { StartPin = a.PhysicalPins[1], EndPin = b.PhysicalPins[0] };

        var script = _exporter.Export([a, b], [conn]);

        script.ShouldContain("circuit.connect('a', 'out1', 'b', 'in')");
    }

    // --- Sweep options ---

    [Fact]
    public void Export_CustomWavelengthSweep_ValuesAppearInScript()
    {
        var script = _exporter.Export([], [], wavelengthMinNm: 1310, wavelengthMaxNm: 1400, numPoints: 50);

        script.ShouldContain("1310");
        script.ShouldContain("1400");
        script.ShouldContain("50");
    }

    [Fact]
    public void Export_SimulationSectionRefusesToRunWithoutExplicitPorts()
    {
        // The generated script must fail loudly (ValueError) if the user runs
        // it without setting INPUT_PORTS / OUTPUT_PORTS — any silent success
        // with empty ports would pretend to simulate nothing.
        var script = _exporter.Export([], []);

        script.ShouldContain("INPUT_PORTS");
        script.ShouldContain("OUTPUT_PORTS");
        script.ShouldContain("raise ValueError");
    }
}
