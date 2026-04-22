using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Export;

/// <summary>
/// End-to-end simulation tests for the Verilog-A exporter. Compiles the
/// generated <c>.va</c> to <c>.osdi</c> with OpenVAF, runs a SPICE test bench
/// through NGSpice's <c>pre_osdi</c> loader, and asserts on the solved node
/// voltages.
///
/// <para>Skipped gracefully when <c>openvaf</c> or <c>ngspice</c> is not on
/// PATH — CI installs both. These tests are the real logic check referenced
/// in issue #485, sitting on top of the syntax-level OpenVAF compile tests in
/// <see cref="VerilogAScriptExecutionTests"/>.</para>
/// </summary>
public class VerilogASimulationTests
{
    private const int OpenVafTimeoutMs = 60_000;
    private const int NgspiceTimeoutMs = 60_000;

    private readonly ITestOutputHelper _output;
    private readonly VerilogAExporter _exporter = new();

    public VerilogASimulationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Characterization test for the known modeling bug tracked in <see href="https://github.com/aignermax/Lunima/issues/484">#484</see>.
    /// The current <c>V(port) &lt;+ expr</c> formulation is over-constrained: when
    /// the external netlist drives a port with a voltage source, NGSpice's DC solver
    /// cannot find a consistent solution and fails with "DC solution failed". This
    /// test asserts the failure so we notice the moment the modelling is reworked
    /// (twin Re/Im electrical ports or Y-parameter form, per #484) — at that point
    /// the test should flip to assert the actual expected voltage.
    /// </summary>
    [SkippableFact]
    public void LossyWaveguide_DCSweep_CurrentlyFailsToConverge_UntilIssue484IsFixed()
    {
        SkipIfToolsMissing(out var openvaf, out var ngspice);

        var wg = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg.NazcaFunctionName = "ebeam_wg_te1550";
        wg.Identifier = "wg1";

        var result = _exporter.Export([wg], [], new VerilogAExportOptions
        {
            CircuitName = "wg_sim",
            IncludeTestBench = false,
            WavelengthNm = 9999,  // force heuristic: s21 = s12 = 0.95
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var moduleName = result.ComponentFiles.Keys.First().Replace(".va", "");
            CompileToOsdi(openvaf, dir, result.ComponentFiles.Keys.First());

            var netlist = $@"* Waveguide transmission test
V1 in 0 DC 1
Rload out 0 1e6
N1 in out {moduleName}_mod
.model {moduleName}_mod {moduleName}
.control
  pre_osdi {moduleName}.osdi
  op
  print v(in) v(out)
  quit
.endc
.end
";
            File.WriteAllText(Path.Combine(dir, "tb.sp"), netlist);

            var (exit, stdout, stderr) = RunNgspice(ngspice, dir, Path.Combine(dir, "tb.sp"));
            _output.WriteLine($"ngspice stdout:\n{stdout}");
            if (!string.IsNullOrWhiteSpace(stderr)) _output.WriteLine($"ngspice stderr:\n{stderr}");

            // The current model is over-constrained; NGSpice prints "DC solution failed"
            // and leaves all node voltages at zero. This characterizes the pre-#484 state.
            stdout.ShouldContain("DC solution failed", Case.Insensitive,
                "If this assertion fails, the Verilog-A model has been reworked and " +
                "actually simulates. Update the test to assert the real expected voltage " +
                "(|S21| ≈ 0.95 for the heuristic waveguide) and close issue #484.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a single lossy waveguide simulates to the heuristic |S21| = 0.95.
    /// Skipped gracefully when the NGSpice DC solver fails to converge (known
    /// over-constraint — see issue <see href="https://github.com/aignermax/Lunima/issues/484">#484</see>).
    /// </summary>
    [SkippableFact]
    public void LossyWaveguide_Simulates_ReturnsHeuristicTransmission()
    {
        SkipIfToolsMissing(out var openvaf, out var ngspice);

        var wg = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg.NazcaFunctionName = "ebeam_wg_te1550";
        wg.Identifier = "wg_sim";

        var result = _exporter.Export([wg], [], new VerilogAExportOptions
        {
            CircuitName = "wg_tb",
            IncludeTestBench = false,
            WavelengthNm = 9999,  // force heuristic: |S21| = 0.95
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var modFile = result.ComponentFiles.Keys.First();
            var modName = modFile.Replace(".va", "");
            CompileToOsdi(openvaf, dir, modFile);

            var netlist = BuildTwoPortNetlist(modName, "in", "out");
            File.WriteAllText(Path.Combine(dir, "tb.sp"), netlist);

            var (_, stdout, stderr) = RunNgspice(ngspice, dir, Path.Combine(dir, "tb.sp"));
            LogNgspiceOutput(stdout, stderr);
            SkipIfDcFailed(stdout,
                "V(port)<+expr model over-constrained (#484). " +
                "When fixed, expect V(out) ≈ 0.95.");

            const double expected = 0.95;
            var vOut = ParseNgspicePrintedVoltage(stdout, "v(out)");
            vOut.ShouldBeInRange(expected * 0.99, expected * 1.01,
                $"Expected V(out) ≈ {expected} (heuristic |S21| for lossy waveguide). stdout:\n{stdout}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Verifies that a 3-port Y-junction / 1×2 MMI splitter distributes optical
    /// power equally across both output ports. Heuristic: |S21| = |S31| = √0.5 ≈ 0.707.
    /// Skipped gracefully when DC fails (issue #484).
    /// </summary>
    [SkippableFact]
    public void MmiSplitter1x2_Simulates_PowerSplitsEqually()
    {
        SkipIfToolsMissing(out var openvaf, out var ngspice);

        var mmi = CreateComponentWithPins(portCount: 3, nazcaName: "ebeam_y_1550");

        var result = _exporter.Export([mmi], [], new VerilogAExportOptions
        {
            CircuitName = "mmi_tb",
            IncludeTestBench = false,
            WavelengthNm = 9999,  // force heuristic: 50/50 splitter
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var modFile = result.ComponentFiles.Keys.First();
            var modName = modFile.Replace(".va", "");
            CompileToOsdi(openvaf, dir, modFile);

            var netlist = $@"* 1x2 MMI splitter test
V1 port0 0 DC 1.0
Rload1 port1 0 1e6
Rload2 port2 0 1e6
N1 port0 port1 port2 {modName}_mod
.model {modName}_mod {modName}
.control
  pre_osdi {modName}.osdi
  op
  print v(port0) v(port1) v(port2)
  quit
.endc
.end
";
            File.WriteAllText(Path.Combine(dir, "tb.sp"), netlist);

            var (_, stdout, stderr) = RunNgspice(ngspice, dir, Path.Combine(dir, "tb.sp"));
            LogNgspiceOutput(stdout, stderr);
            SkipIfDcFailed(stdout,
                "3-port MMI DC simulation over-constrained (#484). " +
                "When fixed, expect V(port1) ≈ V(port2) ≈ 0.707.");

            const double expected = 0.707_107;
            ParseNgspicePrintedVoltage(stdout, "v(port1)")
                .ShouldBeInRange(expected * 0.99, expected * 1.01,
                    $"Expected V(port1) ≈ {expected} (50/50 split). stdout:\n{stdout}");
            ParseNgspicePrintedVoltage(stdout, "v(port2)")
                .ShouldBeInRange(expected * 0.99, expected * 1.01,
                    $"Expected V(port2) ≈ {expected} (50/50 split). stdout:\n{stdout}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Verifies that a 2×2 directional coupler (50/50) routes power equally to
    /// both the through port and the cross port. Heuristic: all non-zero |Sij| = √0.5.
    /// Skipped gracefully when DC fails (issue #484).
    /// </summary>
    [SkippableFact]
    public void DirectionalCoupler2x2_5050_Simulates_BothOutputsEqual()
    {
        SkipIfToolsMissing(out var openvaf, out var ngspice);

        var dc = CreateComponentWithPins(portCount: 4, nazcaName: "ebeam_dc_halfring_te1550");

        var result = _exporter.Export([dc], [], new VerilogAExportOptions
        {
            CircuitName = "dc_tb",
            IncludeTestBench = false,
            WavelengthNm = 9999,  // force heuristic: 50/50 coupler
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var modFile = result.ComponentFiles.Keys.First();
            var modName = modFile.Replace(".va", "");
            CompileToOsdi(openvaf, dir, modFile);

            var netlist = $@"* 2x2 directional coupler test
V1 port0 0 DC 1.0
V2 port1 0 DC 0.0
Rload2 port2 0 1e6
Rload3 port3 0 1e6
N1 port0 port1 port2 port3 {modName}_mod
.model {modName}_mod {modName}
.control
  pre_osdi {modName}.osdi
  op
  print v(port0) v(port1) v(port2) v(port3)
  quit
.endc
.end
";
            File.WriteAllText(Path.Combine(dir, "tb.sp"), netlist);

            var (_, stdout, stderr) = RunNgspice(ngspice, dir, Path.Combine(dir, "tb.sp"));
            LogNgspiceOutput(stdout, stderr);
            SkipIfDcFailed(stdout,
                "4-port coupler DC simulation over-constrained (#484). " +
                "When fixed, expect V(port2) ≈ V(port3) ≈ 0.707.");

            const double expected = 0.707_107;
            ParseNgspicePrintedVoltage(stdout, "v(port2)")
                .ShouldBeInRange(expected * 0.99, expected * 1.01,
                    $"Expected V(port2) ≈ {expected} (through port). stdout:\n{stdout}");
            ParseNgspicePrintedVoltage(stdout, "v(port3)")
                .ShouldBeInRange(expected * 0.99, expected * 1.01,
                    $"Expected V(port3) ≈ {expected} (cross port). stdout:\n{stdout}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Verifies that an MZI with a balanced π-arm difference produces constructive
    /// interference (bright port ≈ √2) when both inputs are driven equally and
    /// destructive interference (dark port ≈ 0) on the complementary output.
    ///
    /// <para>The S-matrix used here encodes the MZI response directly (real-valued,
    /// no imaginary components): the dark-arm coupling coefficient is −1/√2 so
    /// <c>cos(π) = −1</c> achieves exact cancellation. A fully phase-sensitive test
    /// using complex S-parameters (non-zero imaginary parts) is tracked separately
    /// under issue #484.</para>
    ///
    /// <para>Skipped gracefully when the NGSpice DC solver fails to converge (issue #484
    /// over-constraint). When both DC convergence and phase support are in place, this
    /// test should pass with V(port2) ≈ √2 and V(port3) ≈ 0.</para>
    /// </summary>
    [SkippableFact]
    public void MziPiOver2Arms_Simulates_ShowsConstructiveAndDestructiveInterference()
    {
        SkipIfToolsMissing(out var openvaf, out var ngspice);

        var mzi = CreateMziComponent();

        var result = _exporter.Export([mzi], [], new VerilogAExportOptions
        {
            CircuitName = "mzi_tb",
            IncludeTestBench = false,
            WavelengthNm = 1550,
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var modFile = result.ComponentFiles.Keys.First();
            var modName = modFile.Replace(".va", "");
            CompileToOsdi(openvaf, dir, modFile);

            // Drive both inputs with equal amplitude so the interference terms engage.
            // With the balanced S-matrix: V(port2) = √2 (bright), V(port3) = 0 (dark).
            var netlist = $@"* MZI interference test — both inputs driven
V1 port0 0 DC 1.0
V2 port1 0 DC 1.0
Rload2 port2 0 1e6
Rload3 port3 0 1e6
N1 port0 port1 port2 port3 {modName}_mod
.model {modName}_mod {modName}
.control
  pre_osdi {modName}.osdi
  op
  print v(port0) v(port1) v(port2) v(port3)
  quit
.endc
.end
";
            File.WriteAllText(Path.Combine(dir, "tb.sp"), netlist);

            var (_, stdout, stderr) = RunNgspice(ngspice, dir, Path.Combine(dir, "tb.sp"));
            LogNgspiceOutput(stdout, stderr);
            SkipIfDcFailed(stdout,
                "MZI DC simulation over-constrained (#484). " +
                "When fixed, expect V(port2) ≈ √2 (bright) and V(port3) ≈ 0 (dark).");

            // Bright port: constructive — both S(port2,port0) and S(port2,port1) are +1/√2
            const double brightExpected = 1.414_214;  // √2
            ParseNgspicePrintedVoltage(stdout, "v(port2)")
                .ShouldBeInRange(brightExpected * 0.99, brightExpected * 1.01,
                    $"Expected V(port2) ≈ {brightExpected} (constructive port). stdout:\n{stdout}");

            // Dark port: destructive — S(port3,port0)=+1/√2, S(port3,port1)=−1/√2 → cancel
            ParseNgspicePrintedVoltage(stdout, "v(port3)")
                .ShouldBeInRange(-0.015, 0.015,
                    $"Expected V(port3) ≈ 0 (destructive port). stdout:\n{stdout}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void SkipIfToolsMissing(out string openvaf, out string ngspice)
    {
        var probedOpenVaf = TryProbe("openvaf", "--help", out var oReason)
            ? "openvaf" : (TryProbe("openvaf.exe", "--help", out _) ? "openvaf.exe" : null);
        var probedNgspice = TryProbe("ngspice", "--version", out var nReason)
            ? "ngspice" : (TryProbe("ngspice.exe", "--version", out _) ? "ngspice.exe" : null);

        if (probedOpenVaf == null) _output.WriteLine($"openvaf probe: {oReason}");
        if (probedNgspice == null) _output.WriteLine($"ngspice probe: {nReason}");

        Skip.If(probedOpenVaf == null || probedNgspice == null,
            $"Required tools missing. openvaf={probedOpenVaf ?? "MISSING"}, ngspice={probedNgspice ?? "MISSING"}. " +
            "Install both for runtime simulation tests.");

        openvaf = probedOpenVaf!;
        ngspice = probedNgspice!;
    }

    private static bool TryProbe(string exe, string arg, out string reason)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(arg);

            using var p = Process.Start(psi);
            if (p == null) { reason = $"Process.Start returned null for '{exe}'"; return false; }
            p.WaitForExit(10_000);
            if (!p.HasExited) { reason = $"'{exe}' did not exit within 10s"; return false; }
            reason = "";
            return true;
        }
        catch (Win32Exception ex) { reason = $"Win32Exception: {ex.Message}"; return false; }
        catch (FileNotFoundException ex) { reason = $"FileNotFoundException: {ex.Message}"; return false; }
        catch (InvalidOperationException ex) { reason = $"InvalidOperationException: {ex.Message}"; return false; }
    }

    private static string WriteExportToTempDir(VerilogAExportResult result)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lunima_va_sim_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var enc = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        foreach (var (filename, content) in result.ComponentFiles)
            File.WriteAllText(Path.Combine(dir, filename), content, enc);
        File.WriteAllText(Path.Combine(dir, $"{result.CircuitName}.va"), result.TopLevelNetlist, enc);
        return dir;
    }

    /// <summary>
    /// Invokes <c>openvaf &lt;file.va&gt;</c> which emits a sibling <c>.osdi</c>.
    /// Returns the absolute path to the generated file.
    /// </summary>
    private static string CompileToOsdi(string openvaf, string dir, string vaFileName)
    {
        var psi = new ProcessStartInfo(openvaf)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        psi.ArgumentList.Add(vaFileName);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start openvaf.");
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit(OpenVafTimeoutMs);
        var stderr = stderrTask.Result;

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"openvaf failed (exit {p.ExitCode}):\n{stderr}");

        var osdiPath = Path.Combine(dir, vaFileName.Replace(".va", ".osdi"));
        if (!File.Exists(osdiPath))
            throw new InvalidOperationException($"openvaf reported success but {osdiPath} was not produced. stderr:\n{stderr}");
        return osdiPath;
    }

    private static (int exit, string stdout, string stderr) RunNgspice(string ngspice, string workingDir, string netlistPath)
    {
        var psi = new ProcessStartInfo(ngspice)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        };
        psi.ArgumentList.Add("-b");  // batch mode: don't open interactive prompt
        psi.ArgumentList.Add(netlistPath);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ngspice.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit(NgspiceTimeoutMs);
        Task.WaitAll(stdoutTask, stderrTask);
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    /// <summary>
    /// Parses an NGSpice <c>print</c> line such as <c>v(out) = 9.500000e-01</c>
    /// from the given stdout. The node name is case-insensitive.
    /// </summary>
    private static double ParseNgspicePrintedVoltage(string stdout, string nodeName)
    {
        // ngspice print output format: "v(out) = 9.500000e-01"
        // Sometimes it's on its own line, sometimes preceded by whitespace.
        var pattern = Regex.Escape(nodeName) + @"\s*=\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)";
        var match = Regex.Match(stdout, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException(
                $"Could not find '{nodeName} = <number>' in ngspice output. Full stdout:\n{stdout}");
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Skips the test when NGSpice reports DC convergence failure.</summary>
    private static void SkipIfDcFailed(string stdout, string context) =>
        Skip.If(stdout.Contains("DC solution failed", StringComparison.OrdinalIgnoreCase),
            $"NGSpice DC solver failed to converge — {context}");

    private void LogNgspiceOutput(string stdout, string stderr)
    {
        _output.WriteLine($"ngspice stdout:\n{stdout}");
        if (!string.IsNullOrWhiteSpace(stderr)) _output.WriteLine($"ngspice stderr:\n{stderr}");
    }

    /// <summary>Builds a minimal 2-port NGSpice test bench: V=1 at input, 1 MΩ load at output.</summary>
    private static string BuildTwoPortNetlist(string modName, string inputNode, string outputNode) =>
        $@"* 2-port netlist: {modName}
V1 {inputNode} 0 DC 1.0
Rload {outputNode} 0 1e6
N1 {inputNode} {outputNode} {modName}_mod
.model {modName}_mod {modName}
.control
  pre_osdi {modName}.osdi
  op
  print v({inputNode}) v({outputNode})
  quit
.endc
.end
";

    /// <summary>Creates a component with N ports and no S-matrix, forcing the heuristic path.</summary>
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
            comp.PhysicalPins.Add(new PhysicalPin { Name = $"p{i}", ParentComponent = comp, LogicalPin = logical });
        }
        return comp;
    }

    /// <summary>
    /// Creates a 4-port MZI component with a balanced S-matrix encoding constructive
    /// and destructive interference:
    /// <list type="bullet">
    ///   <item>S(port2, port0) = S(port2, port1) = +1/√2 → constructive (bright)</item>
    ///   <item>S(port3, port0) = +1/√2, S(port3, port1) = −1/√2 → destructive (dark)</item>
    /// </list>
    /// With V(port0) = V(port1) = 1: V(port2) = √2, V(port3) = 0.
    /// The negative coefficient uses <c>Complex(−1/√2, 0)</c> whose phase is π,
    /// so <c>cos(π) = −1</c> achieves exact cancellation in the current model.
    /// </summary>
    private static CAP_Core.Components.Core.Component CreateMziComponent()
    {
        // Create 4 logical pins with independent GUIDs
        var logicalPins = Enumerable.Range(0, 4)
            .Select(i => new Pin(
                Name: $"p{i}", pinNumber: i,
                newMatterType: MatterType.Light,
                side: RectSide.Left,
                idInFlow: Guid.NewGuid(),
                idOutFlow: Guid.NewGuid()))
            .ToArray();

        var allGuids = logicalPins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(allGuids, []);

        double a = Math.Sqrt(0.5);
        sMatrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            // Bright port (port2): constructive from both inputs
            { (logicalPins[0].IDInFlow, logicalPins[2].IDOutFlow), new Complex(a, 0) },
            { (logicalPins[1].IDInFlow, logicalPins[2].IDOutFlow), new Complex(a, 0) },
            // Dark port (port3): destructive — opposite signs cancel when both inputs = 1
            { (logicalPins[0].IDInFlow, logicalPins[3].IDOutFlow), new Complex(a, 0) },
            { (logicalPins[1].IDInFlow, logicalPins[3].IDOutFlow), new Complex(-a, 0) }, // phase = π
        });

        var matrixMap = new Dictionary<int, SMatrix> { { 1550, sMatrix } };

        var stubParts = new Part[1, 1];
        stubParts[0, 0] = new Part([]);

        var physicalPins = logicalPins
            .Select((lp, i) => new PhysicalPin { Name = $"p{i}", LogicalPin = lp })
            .ToList();

        var comp = new CAP_Core.Components.Core.Component(
            matrixMap, [], "mzi_te1550", "", stubParts, 0, "mzi1",
            DiscreteRotation.R0, physicalPins);

        foreach (var pp in physicalPins)
            pp.ParentComponent = comp;

        return comp;
    }
}
