using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
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
    /// End-to-end simulation test for a heuristic lossy waveguide using the
    /// twin-node complex model introduced in issue #484.
    /// <para>
    /// The module has 4 electrical nodes (port0_re, port0_im, port1_re, port1_im).
    /// With S21_re = 0.95 and S21_im = 0 (heuristic, S11 = S22 = 0), driving
    /// port0_re = 1V should yield port1_re ≈ 0.95V and port1_im ≈ 0V.
    /// </para>
    /// </summary>
    [SkippableFact]
    public void LossyWaveguide_TwinNodeComplexModel_TransmitsCorrectAmplitude()
    {
        SkipIfToolsMissing(out var openvaf, out var ngspice);

        var wg = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg.NazcaFunctionName = "ebeam_wg_te1550";
        wg.Identifier = "wg1";

        var result = _exporter.Export([wg], [], new VerilogAExportOptions
        {
            CircuitName = "wg_sim",
            IncludeTestBench = false,
            WavelengthNm = 9999,  // force heuristic: S21_re = 0.95, S21_im = 0
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            var moduleName = result.ComponentFiles.Keys.First().Replace(".va", "");
            CompileToOsdi(openvaf, dir, result.ComponentFiles.Keys.First());

            // Twin-node netlist: port0 = (in_re, in_im), port1 = (out_re, out_im)
            // S11 = S22 = 0 so the module contributes nothing to port0 — no over-constraint.
            var netlist = $@"* Waveguide transmission test (twin-node complex model, issue #484)
V1_re in_re 0 DC 1
V1_im in_im 0 DC 0
Rload_re out_re 0 1e6
Rload_im out_im 0 1e6
N1 in_re in_im out_re out_im {moduleName}_mod
.model {moduleName}_mod {moduleName}
.control
  pre_osdi {moduleName}.osdi
  op
  print v(out_re) v(out_im)
  quit
.endc
.end
";
            File.WriteAllText(Path.Combine(dir, "tb.sp"), netlist);

            var (exit, stdout, stderr) = RunNgspice(ngspice, dir, Path.Combine(dir, "tb.sp"));
            _output.WriteLine($"ngspice stdout:\n{stdout}");
            if (!string.IsNullOrWhiteSpace(stderr)) _output.WriteLine($"ngspice stderr:\n{stderr}");

            stdout.ShouldNotContain("DC solution failed", Case.Insensitive,
                "Issue #484 twin-node fix should resolve the DC convergence failure. " +
                "If NGSpice still fails here, check the N-element 4-port syntax for your NGSpice version.");

            var outRe = ParseNgspicePrintedVoltage(stdout, "v(out_re)");
            outRe.ShouldBe(0.95, tolerance: 0.01,
                "With heuristic waveguide S21 = 0.95+0j, driving port0_re=1V should yield port1_re ≈ 0.95V.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
}
