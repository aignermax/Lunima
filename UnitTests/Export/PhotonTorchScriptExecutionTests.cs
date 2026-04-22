using System.ComponentModel;
using System.Diagnostics;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Export;

/// <summary>
/// Runtime tests for the PhotonTorch exporter: actually executes the generated
/// script with a local Python interpreter and verifies it does not raise.
///
/// <para>
/// The test uses <see cref="SkippableFact"/> so a missing Python / photontorch
/// install shows up as a Skipped test (not a silent Passed). CI installs the
/// stack explicitly — see <c>.github/workflows/xUnitTests.yaml</c>.
/// </para>
/// </summary>
public class PhotonTorchScriptExecutionTests
{
    private readonly ITestOutputHelper _output;
    private readonly PhotonTorchExporter _exporter = new();

    public PhotonTorchScriptExecutionTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public void GeneratedScript_ForTwoWaveguides_RunsWithoutError()
    {
        SkipIfPythonMissing(out var python);

        var wg1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg1.Identifier = "wg1";
        wg1.WidthMicrometers = 100;
        var wg2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg2.Identifier = "wg2";
        wg2.WidthMicrometers = 100;
        var conn = new WaveguideConnection
        {
            StartPin = wg1.PhysicalPins[1],
            EndPin = wg2.PhysicalPins[0]
        };

        var script = _exporter.Export([wg1, wg2], [conn]);

        var (exit, stdout, stderr) = RunPythonScript(python, script);

        _output.WriteLine($"stdout: {stdout}");
        if (!string.IsNullOrEmpty(stderr)) _output.WriteLine($"stderr: {stderr}");

        exit.ShouldBe(0, $"Network construction must succeed. Script:\n{script}\n\nstderr:\n{stderr}");
        stdout.ShouldContain("BUILD_OK:");
    }

    [SkippableFact]
    public void GeneratedScript_WithDirectionalCoupler_RunsWithoutError()
    {
        SkipIfPythonMissing(out var python);

        var dc = TestComponentFactoryExtensions.CreateDirectionalCouplerWithPhysicalPins();
        dc.Identifier = "dc1";

        var script = _exporter.Export([dc], []);

        var (exit, stdout, stderr) = RunPythonScript(python, script);

        _output.WriteLine($"stdout: {stdout}");
        if (!string.IsNullOrEmpty(stderr)) _output.WriteLine($"stderr: {stderr}");

        exit.ShouldBe(0, $"DC-only Network construction must succeed. stderr:\n{stderr}");
        stdout.ShouldContain("BUILD_OK:");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void SkipIfPythonMissing(out string pythonExe)
    {
        var reasons = new List<string>();
        foreach (var candidate in new[] { "python3", "python" })
        {
            if (TryProbe(candidate, out var probeReason))
            {
                pythonExe = candidate;
                return;
            }
            reasons.Add($"{candidate}: {probeReason}");
            _output.WriteLine($"probe '{candidate}' failed: {probeReason}");
        }
        pythonExe = "";  // unreachable after Skip
        Skip.If(true,
            "Python with photontorch not found on PATH. Probed:\n  " + string.Join("\n  ", reasons));
    }

    private static bool TryProbe(string pythonExe, out string reason)
    {
        try
        {
            // Use ArgumentList (not a single pre-quoted Arguments string) — on Linux
            // the double-quotes in `-c "import photontorch"` were being passed through
            // literally, so Python saw the quotes as part of the source and did nothing
            // useful. ArgumentList builds argv element-by-element and is OS-agnostic.
            var psi = new ProcessStartInfo(pythonExe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import photontorch; print(photontorch.__version__)");

            using var p = Process.Start(psi);
            if (p == null)
            {
                reason = $"Process.Start returned null for '{pythonExe}'";
                return false;
            }
            p.WaitForExit(30_000);
            if (p.ExitCode != 0)
            {
                var stderr = p.StandardError.ReadToEnd().Trim();
                reason = $"exit {p.ExitCode}: {stderr}";
                return false;
            }
            reason = "";
            return true;
        }
        catch (Win32Exception ex) { reason = $"Win32Exception: {ex.Message}"; return false; }
        catch (FileNotFoundException ex) { reason = $"FileNotFoundException: {ex.Message}"; return false; }
        catch (InvalidOperationException ex) { reason = $"InvalidOperationException: {ex.Message}"; return false; }
    }

    /// <summary>
    /// Prefix of the header line <see cref="PhotonTorchScriptWriter.AppendSimulation"/>
    /// writes before the simulation block. The integration test truncates here so
    /// we only verify the Network construction — photontorch 0.4.1's runtime
    /// simulation path depends on a torch&lt;1.9 install.
    /// </summary>
    private const string SimulationMarker = "# ── Simulation ";

    private static (int exitCode, string stdout, string stderr) RunPythonScript(string pythonExe, string script)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"lunima_pt_{Guid.NewGuid():N}.py");

        var markerIdx = script.IndexOf(SimulationMarker, StringComparison.Ordinal);
        var constructionOnly = markerIdx > 0 ? script[..markerIdx] : script;

        var headless =
            "import matplotlib\nmatplotlib.use('Agg')\n"
            + constructionOnly
            + "\nprint('BUILD_OK:', type(nw).__name__)\n";

        File.WriteAllText(scriptPath, headless, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            var psi = new ProcessStartInfo(pythonExe, $"\"{scriptPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start '{pythonExe}'.");

            // Read both pipes concurrently to avoid OS pipe-buffer deadlock when
            // the child emits many stderr deprecation warnings (photontorch + torch do).
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            p.WaitForExit(120_000);
            Task.WaitAll(stdoutTask, stderrTask);
            return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        finally
        {
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
    }
}
