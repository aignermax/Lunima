using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Structural tests for <see cref="SaxExporter"/>. These assert on the
/// shape of the emitted sax-based Python script: presence of the expected
/// netlist sections, correct pin-name routing through ComponentGroups, loud
/// failures on unrecognised components. The "does the script actually run?"
/// question lives in <see cref="UnitTests.Export.SaxScriptExecutionTests"/>.
/// </summary>
public class SaxExporterTests
{
    private readonly SaxExporter _exporter = new();

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
        comp.WidthMicrometers = 50;
        comp.HeightMicrometers = 10;
        return comp;
    }

    // --- Script skeleton ---

    [Fact]
    public void Export_EmptyDesign_EmitsRunnableSkeleton()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("import numpy as np");
        script.ShouldContain("import sax");
        script.ShouldContain("netlist = {");
        script.ShouldContain("sax.circuit(");
        script.ShouldContain("wavelengths_um = np.linspace(");
        script.ShouldContain("plt.show()");
    }

    [Fact]
    public void Export_HeaderPointsAtSaxAndSaxCom_NotHallucinatedSimphonyApi()
    {
        var script = _exporter.Export([], []);

        script.ShouldContain("pip install sax");
        script.ShouldContain("PICWave");
        script.ShouldContain("COM");
        // Must not repeat the old false promise that `from simphony import Circuit`
        // is part of any shipping Simphony release.
        script.ShouldNotContain("from simphony import Circuit");
    }

    // --- Identifier sanitisation (observed via emitted script) ---

    [Fact]
    public void Export_AlphanumericIdentifier_AppearsInNetlist()
    {
        var script = _exporter.Export(
            [MakeComponent("comp_1", "ebeam_wg_strip_straight", pinCount: 2)], []);
        script.ShouldContain("'comp_1': 'comp_1_model'");
    }

    [Fact]
    public void Export_HyphenatedIdentifier_HyphenBecomesUnderscore()
    {
        var script = _exporter.Export(
            [MakeComponent("my-wg-1", "ebeam_wg_strip_straight", pinCount: 2)], []);
        script.ShouldContain("'my_wg_1': 'my_wg_1_model'");
        script.ShouldNotContain("'my-wg-1'");
    }

    [Fact]
    public void Export_LeadingDigitIdentifier_GetsUnderscorePrefix()
    {
        var script = _exporter.Export(
            [MakeComponent("1wg", "ebeam_wg_strip_straight", pinCount: 2)], []);
        script.ShouldContain("'_1wg'");
    }

    // --- Analytic waveguide fallback ---

    [Fact]
    public void Export_StraightWaveguide_EmitsAnalyticWaveguideModel()
    {
        // 2-port component + "waveguide"/"straight" in name → analytic model
        // with propagation formula. No measured S-matrix needed.
        var script = _exporter.Export(
            [MakeComponent("wg1", "ebeam_wg_strip_straight", pinCount: 2)], []);

        script.ShouldContain("def wg1_model(");
        script.ShouldContain("length_um=");
        script.ShouldContain("loss_db_per_cm=");
        script.ShouldContain("sax.reciprocal(");
    }

    // --- Loud failure paths ---

    [Fact]
    public void Export_UnknownTypeWithoutSMatrix_Throws()
    {
        // No S-matrix, no waveguide-like name, not 2 ports → refuse to invent physics.
        var comp = MakeComponent("mystery", "weird_unknown_thing", pinCount: 3);

        var ex = Should.Throw<InvalidOperationException>(() => _exporter.Export([comp], []));
        ex.Message.ShouldContain("mystery");
        ex.Message.ShouldContain("no measured S-matrix");
    }

    [Fact]
    public void Export_ThreePortWithoutSMatrix_Throws()
    {
        // A Y-junction / splitter without measured data has no analytic model we
        // trust — previous PICWave exporter silently emitted MMI(ports=3) which
        // was physics-unfaithful. Now it throws.
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

    // --- Measured-S-matrix path ---

    [Fact]
    public void Export_ComponentWithSMatrix_EmitsDataAndMeasuredModel()
    {
        var comp = TestComponentFactory.CreatePhaseShifterWithPhysicalPins(forward: new Complex(0, 1));
        comp.Identifier = "ps1";

        var script = _exporter.Export([comp], []);

        // Data dict is emitted with the wavelength key.
        script.ShouldContain("_s_ps1 = {");
        script.ShouldContain("np.array([");

        // Model function looks up the dict at runtime (nearest wavelength).
        script.ShouldContain("def ps1_model(");
        script.ShouldContain("_s_ps1");
        script.ShouldContain("complex(m[");
    }

    [Fact]
    public void Export_PhaseShifterAsymmetric_ForwardAndBackwardEmittedInDataDict()
    {
        // Forward = i → row 1 col 0 = 0 + 1j
        // Backward = 0.5 → row 0 col 1 = 0.5 + 0j
        var comp = TestComponentFactory.CreatePhaseShifterWithPhysicalPins(
            forward: new Complex(0, 1),
            backward: new Complex(0.5, 0));
        comp.Identifier = "ps_asym";

        var script = _exporter.Export([comp], []);

        script.ShouldContain("0+1j");
        script.ShouldContain("0.5+0j");
    }

    // --- Netlist connections ---

    [Fact]
    public void Export_ConnectionBetweenTwoComponents_AppearsInNetlistConnections()
    {
        var a = MakeComponent("a", "ebeam_wg_strip_straight", pinCount: 2);
        var b = MakeComponent("b", "ebeam_wg_strip_straight", pinCount: 2);
        var conn = new WaveguideConnection { StartPin = a.PhysicalPins[1], EndPin = b.PhysicalPins[0] };

        var script = _exporter.Export([a, b], [conn]);

        script.ShouldContain("'a,out1': 'b,in'");
    }

    // --- Sweep options ---

    [Fact]
    public void Export_CustomWavelengthSweep_UmValuesAppearInScript()
    {
        // sax convention: wavelengths in micrometres. 1310 nm → 1.31 um.
        var script = _exporter.Export([], [], wavelengthMinNm: 1310, wavelengthMaxNm: 1400, numPoints: 50);

        script.ShouldContain("1.31");
        script.ShouldContain("1.4");
        script.ShouldContain(", 50)");
    }

    // --- Sweep validation ---

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Export_NonPositiveTargetWavelength_Throws(int wavelengthNm)
    {
        Should.Throw<ArgumentException>(() => _exporter.Export([], [], wavelengthNm: wavelengthNm));
    }

    [Fact]
    public void Export_ReversedSweepBounds_Throws()
    {
        var ex = Should.Throw<ArgumentException>(
            () => _exporter.Export([], [], wavelengthMinNm: 1600, wavelengthMaxNm: 1500));
        ex.Message.ShouldContain("minimum");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-10)]
    public void Export_InvalidNumPoints_Throws(int numPoints)
    {
        Should.Throw<ArgumentException>(() => _exporter.Export([], [], numPoints: numPoints));
    }

    // --- ComponentGroup integration ---

    [Fact]
    public void Export_ConnectionOnGroupExternalPin_ResolvesToLeafComponent()
    {
        // Groups flatten before emission; connections authored against a group's
        // outward-facing pin must unwrap to the leaf so the netlist entry
        // references a component var that's actually added.
        var leaf = MakeComponent("leaf_wg", "ebeam_wg_strip_straight", pinCount: 2);
        var other = MakeComponent("other_wg", "ebeam_wg_strip_straight", pinCount: 2);

        var group = new ComponentGroup("my group");
        group.Identifier = "my_group";
        group.AddChild(leaf);
        group.ExternalPins.Add(new GroupPin
        {
            Name = "group_out",
            InternalPin = leaf.PhysicalPins[1],
        });
        group.PhysicalPins.Add(new PhysicalPin
        {
            Name = "group_out",
            ParentComponent = group,
            LogicalPin = leaf.PhysicalPins[1].LogicalPin,
        });

        var conn = new WaveguideConnection
        {
            StartPin = group.PhysicalPins[0],
            EndPin = other.PhysicalPins[0],
        };

        var script = _exporter.Export([group, other], [conn]);

        script.ShouldContain("'leaf_wg,out1': 'other_wg,in'");
        script.ShouldNotContain("'my_group,");
    }

    [Fact]
    public void Export_GroupInternalFrozenPaths_AreEmittedAsConnections()
    {
        // Regression for the user's scenario: child↔child wiring inside a group
        // is stored in FrozenWaveguidePath, not on the canvas connection list.
        // The exporter has to pick them up or the script silently drops them.
        var a = MakeComponent("inner_a", "ebeam_wg_strip_straight", pinCount: 2);
        var b = MakeComponent("inner_b", "ebeam_wg_strip_straight", pinCount: 2);
        var c = MakeComponent("inner_c", "ebeam_wg_strip_straight", pinCount: 2);

        var group = new ComponentGroup("three-wg chain");
        group.Identifier = "my_chain";
        group.AddChild(a);
        group.AddChild(b);
        group.AddChild(c);
        group.InternalPaths.Add(new FrozenWaveguidePath
        {
            StartPin = a.PhysicalPins[1],
            EndPin = b.PhysicalPins[0],
            Path = new RoutedPath(),
        });
        group.InternalPaths.Add(new FrozenWaveguidePath
        {
            StartPin = b.PhysicalPins[1],
            EndPin = c.PhysicalPins[0],
            Path = new RoutedPath(),
        });

        var script = _exporter.Export([group], []);

        script.ShouldContain("'inner_a,out1': 'inner_b,in'");
        script.ShouldContain("'inner_b,out1': 'inner_c,in'");
    }

    // --- External ports (dangling pins) ---

    [Fact]
    public void Export_DanglingPins_BecomeCircuitLevelPorts()
    {
        // A pin that's not used in any connection is an external port of the
        // generated circuit.
        var a = MakeComponent("wg_a", "ebeam_wg_strip_straight", pinCount: 2);
        var b = MakeComponent("wg_b", "ebeam_wg_strip_straight", pinCount: 2);
        var conn = new WaveguideConnection { StartPin = a.PhysicalPins[1], EndPin = b.PhysicalPins[0] };

        var script = _exporter.Export([a, b], [conn]);

        // a.in and b.out1 are dangling.
        script.ShouldContain("'wg_a_in': 'wg_a,in'");
        script.ShouldContain("'wg_b_out1': 'wg_b,out1'");
    }

    [Fact]
    public void Export_DefaultSweepPorts_PickSourceInputAndSinkOutput()
    {
        // Chain: a → b → c.
        // a appears only as a connection Start → dangling a.in is input-ish.
        // c appears only as a connection End   → dangling c.out1 is output-ish.
        // b is a pass-through, should never be picked.
        var a = MakeComponent("a", "ebeam_wg_strip_straight", pinCount: 2);
        var b = MakeComponent("b", "ebeam_wg_strip_straight", pinCount: 2);
        var c = MakeComponent("c", "ebeam_wg_strip_straight", pinCount: 2);
        var conn1 = new WaveguideConnection { StartPin = a.PhysicalPins[1], EndPin = b.PhysicalPins[0] };
        var conn2 = new WaveguideConnection { StartPin = b.PhysicalPins[1], EndPin = c.PhysicalPins[0] };

        var script = _exporter.Export([a, b, c], [conn1, conn2]);

        script.ShouldContain("INPUT_PORT  = 'a_in'");
        script.ShouldContain("OUTPUT_PORT = 'c_out1'");
    }

    [Fact]
    public void Export_DuplicateConnection_IsEmittedOnlyOnce()
    {
        // A connection that appears in both `_canvas.Connections` and a group's
        // InternalPaths would otherwise produce two identical keys in the sax
        // netlist dict. Python collapses them to the last, so no runtime impact,
        // but the emitted file looks broken to a reviewer.
        var a = MakeComponent("a", "ebeam_wg_strip_straight", pinCount: 2);
        var b = MakeComponent("b", "ebeam_wg_strip_straight", pinCount: 2);
        var conn1 = new WaveguideConnection { StartPin = a.PhysicalPins[1], EndPin = b.PhysicalPins[0] };
        var conn2 = new WaveguideConnection { StartPin = a.PhysicalPins[1], EndPin = b.PhysicalPins[0] };

        var script = _exporter.Export([a, b], [conn1, conn2]);

        var first = script.IndexOf("'a,out1': 'b,in'", StringComparison.Ordinal);
        var second = script.IndexOf("'a,out1': 'b,in'", first + 1, StringComparison.Ordinal);

        first.ShouldBeGreaterThan(0, "the canonical edge must appear");
        second.ShouldBeLessThan(0, "the duplicate must have been collapsed");
    }

    [Fact]
    public void Export_ReverseConnection_IsEmittedOnlyOnce()
    {
        // A waveguide is bidirectional. (A→B) and (B→A) describe the same
        // physical edge; the dedup must treat them as the same.
        var a = MakeComponent("a", "ebeam_wg_strip_straight", pinCount: 2);
        var b = MakeComponent("b", "ebeam_wg_strip_straight", pinCount: 2);
        var forward = new WaveguideConnection { StartPin = a.PhysicalPins[1], EndPin = b.PhysicalPins[0] };
        var reverse = new WaveguideConnection { StartPin = b.PhysicalPins[0], EndPin = a.PhysicalPins[1] };

        var script = _exporter.Export([a, b], [forward, reverse]);

        var first = script.IndexOf("'a,out1': 'b,in'", StringComparison.Ordinal);
        var reverseStr = script.IndexOf("'b,in': 'a,out1'", StringComparison.Ordinal);
        first.ShouldBeGreaterThan(0);
        reverseStr.ShouldBeLessThan(0);
    }

    [Fact]
    public void Export_Plot_AlsoSavesPngAlongsideScript()
    {
        // Even without an interactive matplotlib backend the user should get
        // a visible result. The generated script saves a PNG next to its own
        // file path and reports the path to stdout.
        var script = _exporter.Export([], []);

        script.ShouldContain("plt.savefig(");
        script.ShouldContain("[Lunima] Spectrum saved to:");
    }
}
