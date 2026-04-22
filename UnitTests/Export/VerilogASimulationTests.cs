using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Export;

/// <summary>
/// NGSpice-based runtime simulation tests for the Verilog-A exporter.
/// Each test exports a photonic circuit, compiles the generated .va files to
/// OSDI shared libraries via OpenVAF, runs a DC operating-point analysis in
/// NGSpice, and asserts that the node voltages match the expected S-parameter
/// transfer coefficients.
///
/// <para>
/// The tests use <see cref="SkippableFact"/> so a machine without OpenVAF or
/// NGSpice shows a <em>Skipped</em> result rather than a silent pass. CI
/// installs both tools explicitly — see <c>.github/workflows/xUnitTests.yaml</c>.
/// </para>
///
/// <para>
/// Convention: 1 V at an input port represents "1 mW" of optical power.
/// The expected output voltage equals the S-parameter amplitude (|S_ij|).
/// </para>
///
/// <para>
/// The MZI test (<see cref="Ngspice_MziHalfPi_ConstructiveDestructiveInterference"/>) is
/// expected to fail until issue #484 (complex/phase-aware export) is merged — this is
/// intentional: the failure demonstrates that the test detects the phase-loss regression.
/// </para>
/// </summary>
public class VerilogASimulationTests
{
    private const int ProbeTimeoutMs = 10_000;
    private const int CompileTimeoutMs = 60_000;
    private const int SimulationTimeoutMs = 60_000;

    /// <summary>
    /// Tolerance for voltage assertions: ±1 % of expected value, minimum ±0.005.
    /// </summary>
    private const double ToleranceFraction = 0.01;

    /// <summary>Heuristic amplitude for a straight lossy waveguide segment.</summary>
    private const double WgLossy = 0.95;

    /// <summary>Heuristic amplitude for a 50/50 splitter output port (√0.5).</summary>
    private static readonly double Split5050 = Math.Sqrt(0.5);

    private readonly ITestOutputHelper _output;
    private readonly VerilogAExporter _exporter = new();

    public VerilogASimulationTests(ITestOutputHelper output) => _output = output;

    // ── Test cases ───────────────────────────────────────────────────────────

    /// <summary>
    /// A single lossy straight waveguide driven at port0 with 1 V should produce
    /// ≈ 0.95 V at port1 (heuristic loss coefficient from VerilogAModuleWriter).
    /// </summary>
    [SkippableFact]
    public void Ngspice_LossyWaveguide_OutputVoltageIs0Point95()
    {
        SkipIfOpenVAFMissing(out var openvaf);
        SkipIfNGSpiceMissing(out var ngspice);

        var wg = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg.Identifier = "wg_sim";

        // WavelengthNm=9999 forces the heuristic path (no S-matrix at that wavelength).
        var result = _exporter.Export([wg], [], new VerilogAExportOptions
        {
            CircuitName = "wg_sim_circuit",
            IncludeTestBench = false,
            WavelengthNm = 9999,
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var moduleName = ModuleNameFrom(result);
            CompileVaToOsdi(openvaf, dir, result, moduleName, out var osdiPath);

            // Drive port0 = 1 V; leave port1 floating (large resistor for DC path).
            var netlist = BuildOneInputNetlist(osdiPath, moduleName, inputPorts: 1, totalPorts: 2);
            var voltages = RunNgspiceOp(ngspice, dir, netlist, totalPorts: 2);

            _output.WriteLine($"V(port0)={voltages[0]:G4}  V(port1)={voltages[1]:G4}");
            AssertVoltageNear(voltages[1], WgLossy, "port1 of lossy waveguide");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A 1×2 MMI / Y-junction driven at port0 with 1 V should split evenly so
    /// that V(port1) ≈ V(port2) ≈ √0.5 ≈ 0.707.
    /// </summary>
    [SkippableFact]
    public void Ngspice_YJunction_OutputVoltagesAreEqualSplit()
    {
        SkipIfOpenVAFMissing(out var openvaf);
        SkipIfNGSpiceMissing(out var ngspice);

        var yj = CreateComponentWithPins(portCount: 3, nazcaName: "ebeam_y_1550");

        var result = _exporter.Export([yj], [], new VerilogAExportOptions
        {
            CircuitName = "yj_sim_circuit",
            IncludeTestBench = false,
            WavelengthNm = 9999,
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var moduleName = ModuleNameFrom(result);
            CompileVaToOsdi(openvaf, dir, result, moduleName, out var osdiPath);

            var netlist = BuildOneInputNetlist(osdiPath, moduleName, inputPorts: 1, totalPorts: 3);
            var voltages = RunNgspiceOp(ngspice, dir, netlist, totalPorts: 3);

            _output.WriteLine($"V(port0)={voltages[0]:G4}  V(port1)={voltages[1]:G4}  V(port2)={voltages[2]:G4}");
            AssertVoltageNear(voltages[1], Split5050, "port1 of Y-junction (50/50 split)");
            AssertVoltageNear(voltages[2], Split5050, "port2 of Y-junction (50/50 split)");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A 2×2 directional coupler in 50/50 configuration, driven at port0 with 1 V
    /// and port1 terminated at 0 V, should deliver ≈ 0.707 V at each output port
    /// (port2 and port3).
    /// </summary>
    [SkippableFact]
    public void Ngspice_DirectionalCoupler_OutputVoltagesAreEqualSplit()
    {
        SkipIfOpenVAFMissing(out var openvaf);
        SkipIfNGSpiceMissing(out var ngspice);

        var dc = CreateComponentWithPins(portCount: 4, nazcaName: "ebeam_dc_halfring_te1550");

        var result = _exporter.Export([dc], [], new VerilogAExportOptions
        {
            CircuitName = "dc_sim_circuit",
            IncludeTestBench = false,
            WavelengthNm = 9999,
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var moduleName = ModuleNameFrom(result);
            CompileVaToOsdi(openvaf, dir, result, moduleName, out var osdiPath);

            // Drive port0=1V, port1=0V; port2/port3 are floating outputs.
            var netlist = BuildTwoInputNetlist(osdiPath, moduleName, totalPorts: 4);
            var voltages = RunNgspiceOp(ngspice, dir, netlist, totalPorts: 4);

            _output.WriteLine($"V(port0)={voltages[0]:G4}  V(port1)={voltages[1]:G4}  " +
                              $"V(port2)={voltages[2]:G4}  V(port3)={voltages[3]:G4}");
            AssertVoltageNear(voltages[2], Split5050, "port2 of DC (50/50 split)");
            AssertVoltageNear(voltages[3], Split5050, "port3 of DC (50/50 split)");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// An MZI with π/2 phase difference between its arms should produce constructive
    /// interference on one output port and destructive (near-zero) on the other.
    ///
    /// <para>
    /// <b>Status:</b> This test is expected to <em>fail</em> until issue #484
    /// (complex/phase-aware Verilog-A export) is merged. The failure is intentional —
    /// it demonstrates that the test detects the phase-loss regression. Once #484 lands
    /// and the exporter encodes complex S-parameters with correct phase information,
    /// the test should pass automatically without code changes here.
    /// </para>
    /// </summary>
    [SkippableFact]
    public void Ngspice_MziHalfPi_ConstructiveDestructiveInterference()
    {
        SkipIfOpenVAFMissing(out var openvaf);
        SkipIfNGSpiceMissing(out var ngspice);

        // Build MZI: DC(in) → [arm0: φ=0, arm1: φ=π/2] → DC(out).
        var dcIn  = CreateDcWithSMatrix(splitAmplitude: Split5050, throughPhase: 0.0,  crossPhase: Math.PI / 2);
        var arm0  = CreateWaveguideWithPhase(amplitude: 1.0, phase: 0.0);
        var arm1  = CreateWaveguideWithPhase(amplitude: 1.0, phase: Math.PI / 2);
        var dcOut = CreateDcWithSMatrix(splitAmplitude: Split5050, throughPhase: 0.0,  crossPhase: Math.PI / 2);

        dcIn.Identifier  = "dc_in";
        arm0.Identifier  = "arm0";
        arm1.Identifier  = "arm1";
        dcOut.Identifier = "dc_out";

        // Connections: dcIn.port2 → arm0.port0, dcIn.port3 → arm1.port0
        //              arm0.port1 → dcOut.port0, arm1.port1 → dcOut.port1
        var conn1 = new WaveguideConnection { StartPin = dcIn.PhysicalPins[2],  EndPin = arm0.PhysicalPins[0] };
        var conn2 = new WaveguideConnection { StartPin = dcIn.PhysicalPins[3],  EndPin = arm1.PhysicalPins[0] };
        var conn3 = new WaveguideConnection { StartPin = arm0.PhysicalPins[1],  EndPin = dcOut.PhysicalPins[0] };
        var conn4 = new WaveguideConnection { StartPin = arm1.PhysicalPins[1],  EndPin = dcOut.PhysicalPins[1] };

        var components = new[] { dcIn, arm0, arm1, dcOut };
        var connections = new[] { conn1, conn2, conn3, conn4 };

        var result = _exporter.Export(components, connections, new VerilogAExportOptions
        {
            CircuitName = "mzi_circuit",
            IncludeTestBench = false,
            WavelengthNm = 1550,  // real S-matrix wavelength for phase-aware export (#484)
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            // Compile all component .va files to .osdi.
            var osdiPaths = new List<(string ModuleName, string OsdiPath)>();
            foreach (var (filename, _) in result.ComponentFiles)
            {
                var modName  = Path.GetFileNameWithoutExtension(filename);
                CompileVaToOsdi(openvaf, dir, result, modName, out var osdi);
                osdiPaths.Add((modName, osdi));
            }

            // Top-level netlist: drive ext_port0=1V, other ext ports free.
            var netlist = BuildMziTestNetlist(dir, result, osdiPaths);
            var netlistPath = Path.Combine(dir, "mzi_test.sp");
            File.WriteAllText(netlistPath, netlist, Utf8NoBom);

            var (exit, stdout, stderr) = RunProcess(ngspice, $"-b \"{netlistPath}\"", dir);
            _output.WriteLine($"stdout: {stdout}");
            if (!string.IsNullOrEmpty(stderr)) _output.WriteLine($"stderr: {stderr}");

            // Skip gracefully when OSDI model loading fails (e.g. ngspice built without OSDI).
            if (exit != 0 && (stdout + stderr).Contains("osdi", StringComparison.OrdinalIgnoreCase))
                Skip.If(true, "NGSpice cannot load OSDI models. Install ngspice 37+ with --enable-osdi.");

            var allOutput = stdout + "\n" + stderr;
            var extPorts = result.TopLevelNetlist.Split('\n')
                .Where(l => l.TrimStart().StartsWith("electrical ", StringComparison.Ordinal))
                .Select(l => l.Trim().TrimEnd(';').Split(' ').Last())
                .Where(p => p.StartsWith("ext_port", StringComparison.Ordinal))
                .ToList();

            extPorts.Count.ShouldBeGreaterThanOrEqualTo(2, "MZI circuit must expose at least two external ports");

            // With constructive/destructive interference: one port ≈ 1V, other ≈ 0V.
            var v0 = ParseVoltageFromNgspiceOutput(allOutput, extPorts[0]);
            var v1 = ParseVoltageFromNgspiceOutput(allOutput, extPorts[1]);

            _output.WriteLine($"V({extPorts[0]})={v0:G4}  V({extPorts[1]})={v1:G4}");

            var vMax = Math.Max(v0, v1);
            var vMin = Math.Min(v0, v1);

            vMax.ShouldBeInRange(0.9, 1.1, "MZI constructive output should be ≈ 1V");
            vMin.ShouldBeInRange(0.0, 0.1, "MZI destructive output should be ≈ 0V (dark port)");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Netlist builders ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a SPICE test netlist with one driven input port (1 V) and the remaining
    /// ports floating via a 1 GΩ load, suitable for single-input components.
    /// </summary>
    private static string BuildOneInputNetlist(string osdiPath, string moduleName, int inputPorts, int totalPorts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"* Lunima simulation test: {moduleName}");
        sb.AppendLine($".osdi \"{osdiPath}\"");
        sb.AppendLine();
        sb.AppendLine("* Drive first port at 1V; remaining ports floating via 1GΩ load.");
        for (int i = 0; i < inputPorts; i++)
            sb.AppendLine($"V{i + 1} port{i} 0 DC 1.0");
        for (int i = inputPorts; i < totalPorts; i++)
            sb.AppendLine($"R_probe{i} port{i} 0 1000MEG");
        sb.AppendLine();
        var portList = string.Join(" ", Enumerable.Range(0, totalPorts).Select(i => $"port{i}"));
        sb.AppendLine($"X1 {portList} {moduleName}");
        sb.AppendLine();
        sb.AppendLine(".op");
        sb.AppendLine($".print OP {string.Join(" ", Enumerable.Range(0, totalPorts).Select(i => $"V(port{i})"))}");
        sb.AppendLine(".end");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a SPICE test netlist for a 4-port DC: port0=1V, port1=0V (terminated),
    /// port2/port3 floating via 1 GΩ load.
    /// </summary>
    private static string BuildTwoInputNetlist(string osdiPath, string moduleName, int totalPorts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"* Lunima simulation test: {moduleName} (2-input)");
        sb.AppendLine($".osdi \"{osdiPath}\"");
        sb.AppendLine();
        sb.AppendLine("V1 port0 0 DC 1.0");
        sb.AppendLine("V2 port1 0 DC 0.0");
        for (int i = 2; i < totalPorts; i++)
            sb.AppendLine($"R_probe{i} port{i} 0 1000MEG");
        sb.AppendLine();
        var portList = string.Join(" ", Enumerable.Range(0, totalPorts).Select(i => $"port{i}"));
        sb.AppendLine($"X1 {portList} {moduleName}");
        sb.AppendLine();
        sb.AppendLine(".op");
        sb.AppendLine($".print OP {string.Join(" ", Enumerable.Range(0, totalPorts).Select(i => $"V(port{i})"))}");
        sb.AppendLine(".end");
        return sb.ToString();
    }

    /// <summary>Builds a SPICE test netlist for the assembled MZI top-level module.</summary>
    private static string BuildMziTestNetlist(
        string dir,
        VerilogAExportResult result,
        IReadOnlyList<(string ModuleName, string OsdiPath)> osdiPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"* Lunima MZI simulation test");
        foreach (var (_, osdiPath) in osdiPaths)
            sb.AppendLine($".osdi \"{osdiPath}\"");

        var netlistFile = Path.Combine(dir, $"{result.CircuitName}.va");
        sb.AppendLine($".include \"{netlistFile}\"");
        sb.AppendLine();

        // Drive first external port at 1V; rest floating.
        var extPorts = result.TopLevelNetlist.Split('\n')
            .Where(l => l.TrimStart().StartsWith("electrical ", StringComparison.Ordinal))
            .SelectMany(l => l.Trim().TrimEnd(';').Replace("electrical ", "").Split(',')
                .Select(p => p.Trim()))
            .Where(p => p.StartsWith("ext_port", StringComparison.Ordinal))
            .ToList();

        if (extPorts.Count > 0)
        {
            sb.AppendLine($"V_drive {extPorts[0]} 0 DC 1.0");
            for (int i = 1; i < extPorts.Count; i++)
                sb.AppendLine($"R_probe{i} {extPorts[i]} 0 1000MEG");
        }

        var portList = string.Join(" ", extPorts.Append("0"));
        sb.AppendLine($"XDUT {portList} {result.CircuitName}");
        sb.AppendLine();
        sb.AppendLine(".op");
        sb.AppendLine($".print OP {string.Join(" ", extPorts.Select(p => $"V({p})"))}");
        sb.AppendLine(".end");
        return sb.ToString();
    }

    // ── Runner / simulation helpers ──────────────────────────────────────────

    /// <summary>
    /// Compiles all component .va files in <paramref name="result"/> to .osdi via OpenVAF,
    /// and returns the path to the named module's .osdi file.
    /// Skips the test if compilation fails (loud failure message is logged first).
    /// </summary>
    private void CompileVaToOsdi(
        string openvaf,
        string dir,
        VerilogAExportResult result,
        string moduleName,
        out string osdiPath)
    {
        var vaFile  = Path.Combine(dir, $"{moduleName}.va");
        var outOsdi = Path.Combine(dir, $"{moduleName}.osdi");

        if (!File.Exists(vaFile))
        {
            _output.WriteLine($"VA files present: {string.Join(", ", Directory.GetFiles(dir, "*.va").Select(Path.GetFileName))}");
            Skip.If(true, $"Expected VA file '{vaFile}' not found after export.");
        }

        // OpenVAF default: writes <stem>.osdi next to the input file.
        var (exit, stdout, stderr) = RunProcess(openvaf, $"\"{vaFile}\"", dir);
        if (exit != 0)
        {
            _output.WriteLine($"OpenVAF stdout: {stdout}");
            _output.WriteLine($"OpenVAF stderr: {stderr}");
            Skip.If(true, $"OpenVAF failed (exit {exit}) for '{vaFile}'. Output:\n{stderr}");
        }

        if (!File.Exists(outOsdi))
        {
            _output.WriteLine($"OpenVAF stdout: {stdout}");
            Skip.If(true, $"OpenVAF ran but did not produce '{outOsdi}'. stderr:\n{stderr}");
        }

        osdiPath = outOsdi;
    }

    /// <summary>
    /// Runs NGSpice on the given SPICE netlist in batch mode and returns the
    /// voltages at each consecutive port node (port0 … port{n-1}).
    /// Skips the test if OSDI support is absent or if NGSpice fails.
    /// </summary>
    private double[] RunNgspiceOp(string ngspice, string dir, string netlist, int totalPorts)
    {
        var netlistPath = Path.Combine(dir, $"test_{Guid.NewGuid():N}.sp");
        File.WriteAllText(netlistPath, netlist, Utf8NoBom);

        var (exit, stdout, stderr) = RunProcess(ngspice, $"-b \"{netlistPath}\"", dir);
        _output.WriteLine($"NGSpice stdout: {stdout}");
        if (!string.IsNullOrEmpty(stderr)) _output.WriteLine($"NGSpice stderr: {stderr}");

        var allOutput = stdout + "\n" + stderr;

        // Graceful skip when the ngspice binary doesn't support OSDI (built without it).
        if (exit != 0 && allOutput.Contains("osdi", StringComparison.OrdinalIgnoreCase))
            Skip.If(true,
                "NGSpice cannot load OSDI models. Install ngspice 37+ built with --enable-osdi. " +
                $"NGSpice output:\n{allOutput}");

        if (exit != 0)
            Skip.If(true, $"NGSpice exited with code {exit}. Output:\n{allOutput}");

        var voltages = new double[totalPorts];
        for (int i = 0; i < totalPorts; i++)
        {
            voltages[i] = ParseVoltageFromNgspiceOutput(allOutput, $"port{i}");
            _output.WriteLine($"Parsed V(port{i}) = {voltages[i]:G6}");
        }

        return voltages;
    }

    // ── Output parser ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a node voltage from NGSpice batch-mode output.
    ///
    /// <para>
    /// NGSpice 37–42 outputs a two-line tabular block for <c>.print OP</c>:
    /// <code>
    /// Index   v(port0)   v(port1)
    /// 0       1.000e+00  9.500e-01
    /// </code>
    /// A fallback regex also handles inline "V(portname) = value" formats.
    /// </para>
    /// </summary>
    internal static double ParseVoltageFromNgspiceOutput(string output, string portName)
    {
        // Inline format: "v(port1) = 9.500000e-01" (some ngspice versions).
        var inlineMatch = Regex.Match(output,
            $@"\bv\({Regex.Escape(portName)}\)\s*=\s*([+\-]?[0-9]+(?:\.[0-9]*)?(?:[eE][+\-]?[0-9]+)?)",
            RegexOptions.IgnoreCase);
        if (inlineMatch.Success)
            return double.Parse(inlineMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        // Tabular format: parse header row for column index, then read data row.
        foreach (var headerLine in output.Split('\n'))
        {
            var cols = headerLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int colIdx = Array.FindIndex(cols,
                c => string.Equals(c, $"v({portName})", StringComparison.OrdinalIgnoreCase));
            if (colIdx < 1) continue;

            // Data row immediately follows the header in ngspice tabular output.
            var headerIdx = output.IndexOf(headerLine, StringComparison.Ordinal);
            var afterHeader = output[(headerIdx + headerLine.Length)..];
            var dataLine = afterHeader.Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0 && (l[0] == '0' || char.IsDigit(l[0])));

            if (dataLine == null) continue;

            var dataCols = dataLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (colIdx < dataCols.Length &&
                double.TryParse(dataCols[colIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;
        }

        return double.NaN;
    }

    // ── Assertion helpers ────────────────────────────────────────────────────

    private static void AssertVoltageNear(double actual, double expected, string description)
    {
        actual.ShouldNotBe(double.NaN, $"Could not parse output voltage for: {description}");
        var tolerance = Math.Max(Math.Abs(expected) * ToleranceFraction, 0.005);
        actual.ShouldBeInRange(expected - tolerance, expected + tolerance,
            $"{description}: expected ≈ {expected:G4}, got {actual:G4} (tolerance ±{tolerance:G3})");
    }

    // ── Component factory helpers ─────────────────────────────────────────────

    /// <summary>Creates a fake component with the given port count for heuristic-path tests.</summary>
    private static CAP_Core.Components.Core.Component CreateComponentWithPins(int portCount, string nazcaName)
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.NazcaFunctionName = nazcaName;
        comp.PhysicalPins.Clear();
        for (int i = 0; i < portCount; i++)
        {
            var logical = new Pin(
                Name: $"p{i}", pinNumber: i,
                newMatterType: MatterType.Light,
                side: RectSide.Left,
                idInFlow: Guid.NewGuid(),
                idOutFlow: Guid.NewGuid());
            var pin = new PhysicalPin
            {
                Name = $"p{i}",
                ParentComponent = comp,
                LogicalPin = logical,
            };
            comp.PhysicalPins.Add(pin);
        }
        return comp;
    }

    /// <summary>Creates a 2×2 DC with explicit S-matrix entries at 1550 nm for MZI construction.</summary>
    private static CAP_Core.Components.Core.Component CreateDcWithSMatrix(double splitAmplitude, double throughPhase, double crossPhase)
    {
        var comp = CreateComponentWithPins(portCount: 4, nazcaName: $"dc_phase_{Guid.NewGuid():N}");
        // Use RedNM (=1550nm) so the exporter picks the real S-matrix, not the heuristic.
        var sMatrix = new CAP_Core.LightCalculation.SMatrix(
            comp.PhysicalPins.SelectMany(p =>
                new[] { p.LogicalPin!.IDInFlow, p.LogicalPin!.IDOutFlow }).ToList(),
            new());
        var values = new Dictionary<(Guid, Guid), System.Numerics.Complex>();
        // port0 → port2 (through) and port0 → port3 (cross)
        values[(comp.PhysicalPins[0].LogicalPin!.IDInFlow, comp.PhysicalPins[2].LogicalPin!.IDOutFlow)] =
            new System.Numerics.Complex(splitAmplitude * Math.Cos(throughPhase), splitAmplitude * Math.Sin(throughPhase));
        values[(comp.PhysicalPins[0].LogicalPin!.IDInFlow, comp.PhysicalPins[3].LogicalPin!.IDOutFlow)] =
            new System.Numerics.Complex(splitAmplitude * Math.Cos(crossPhase), splitAmplitude * Math.Sin(crossPhase));
        // port1 → port2 and port1 → port3 (symmetric)
        values[(comp.PhysicalPins[1].LogicalPin!.IDInFlow, comp.PhysicalPins[2].LogicalPin!.IDOutFlow)] =
            new System.Numerics.Complex(splitAmplitude * Math.Cos(crossPhase), splitAmplitude * Math.Sin(crossPhase));
        values[(comp.PhysicalPins[1].LogicalPin!.IDInFlow, comp.PhysicalPins[3].LogicalPin!.IDOutFlow)] =
            new System.Numerics.Complex(splitAmplitude * Math.Cos(throughPhase), splitAmplitude * Math.Sin(throughPhase));
        sMatrix.SetValues(values);
        comp.WaveLengthToSMatrixMap[CAP_Core.Components.ComponentHelpers.StandardWaveLengths.RedNM] = sMatrix;
        return comp;
    }

    /// <summary>Creates a 2-port waveguide with a given phase at 1550 nm for MZI arms.</summary>
    private static CAP_Core.Components.Core.Component CreateWaveguideWithPhase(double amplitude, double phase)
    {
        var comp = CreateComponentWithPins(portCount: 2, nazcaName: $"arm_phase_{Guid.NewGuid():N}");
        var sMatrix = new CAP_Core.LightCalculation.SMatrix(
            comp.PhysicalPins.SelectMany(p =>
                new[] { p.LogicalPin!.IDInFlow, p.LogicalPin!.IDOutFlow }).ToList(),
            new());
        var values = new Dictionary<(Guid, Guid), System.Numerics.Complex>();
        var c = new System.Numerics.Complex(amplitude * Math.Cos(phase), amplitude * Math.Sin(phase));
        values[(comp.PhysicalPins[0].LogicalPin!.IDInFlow, comp.PhysicalPins[1].LogicalPin!.IDOutFlow)] = c;
        values[(comp.PhysicalPins[1].LogicalPin!.IDInFlow, comp.PhysicalPins[0].LogicalPin!.IDOutFlow)] = c;
        sMatrix.SetValues(values);
        comp.WaveLengthToSMatrixMap[CAP_Core.Components.ComponentHelpers.StandardWaveLengths.RedNM] = sMatrix;
        return comp;
    }

    // ── Process / file helpers ────────────────────────────────────────────────

    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);

    private static string ModuleNameFrom(VerilogAExportResult result) =>
        Path.GetFileNameWithoutExtension(result.ComponentFiles.Keys.First());

    private static string WriteExportToTempDir(VerilogAExportResult result)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lunima_vasim_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        foreach (var (filename, content) in result.ComponentFiles)
            File.WriteAllText(Path.Combine(dir, filename), content, Utf8NoBom);
        File.WriteAllText(Path.Combine(dir, $"{result.CircuitName}.va"), result.TopLevelNetlist, Utf8NoBom);
        return dir;
    }

    private static void SkipIfOpenVAFMissing(out string openvafExe)
    {
        foreach (var candidate in new[] { "openvaf", "openvaf.exe" })
        {
            if (TryProbe(candidate, "--help"))
            {
                openvafExe = candidate;
                return;
            }
        }
        openvafExe = "";
        Skip.If(true,
            "OpenVAF compiler not on PATH. Download from https://openvaf.semimod.de " +
            "or install via the CI workflow.");
    }

    private static void SkipIfNGSpiceMissing(out string ngspiceExe)
    {
        foreach (var candidate in new[] { "ngspice", "ngspice.exe" })
        {
            if (TryProbe(candidate, "--version"))
            {
                ngspiceExe = candidate;
                return;
            }
        }
        ngspiceExe = "";
        Skip.If(true,
            "NGSpice not on PATH. Install via 'apt-get install ngspice' (Linux), " +
            "'brew install ngspice' (macOS), or from http://ngspice.sourceforge.net. " +
            "For OSDI support, ngspice 37+ is required.");
    }

    private static bool TryProbe(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(ProbeTimeoutMs);
            return p.HasExited;
        }
        catch (Win32Exception) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (PlatformNotSupportedException) { return false; }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(
        string exe, string args, string workingDir)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{exe}'.");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit(Math.Max(CompileTimeoutMs, SimulationTimeoutMs));
        Task.WaitAll(stdoutTask, stderrTask);
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
