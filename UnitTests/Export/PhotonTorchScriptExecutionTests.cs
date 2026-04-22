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
/// The test skips gracefully (passes with a log line) when Python or the
/// <c>photontorch</c> package is not available on the machine, so local dev
/// checkouts without the Python stack don't turn red. In CI we install the
/// stack explicitly — see <c>.github/workflows/</c>.
/// </summary>
public class PhotonTorchScriptExecutionTests
{
    private readonly ITestOutputHelper _output;
    private readonly PhotonTorchExporter _exporter = new();

    public PhotonTorchScriptExecutionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void GeneratedScript_ForTwoWaveguides_RunsWithoutError()
    {
        if (!PythonWithPhotonTorchAvailable(out var python, out var reason))
        {
            _output.WriteLine($"SKIP: {reason}");
            return;
        }

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

        var (exit, stdout, stderr) = RunPythonScript(python!, script);

        _output.WriteLine($"stdout: {stdout}");
        if (!string.IsNullOrEmpty(stderr)) _output.WriteLine($"stderr: {stderr}");

        exit.ShouldBe(0, $"Network construction must succeed. Script:\n{script}\n\nstderr:\n{stderr}");
        stdout.ShouldContain("BUILD_OK:");
    }

    [Fact]
    public void GeneratedScript_WithDirectionalCoupler_RunsWithoutError()
    {
        if (!PythonWithPhotonTorchAvailable(out var python, out var reason))
        {
            _output.WriteLine($"SKIP: {reason}");
            return;
        }

        var dc = TestComponentFactoryExtensions.CreateDirectionalCouplerWithPhysicalPins();
        dc.Identifier = "dc1";

        var script = _exporter.Export([dc], []);
        _output.WriteLine($"--- generated script ---\n{script}\n--- end ---");

        var (exit, stdout, stderr) = RunPythonScript(python!, script);

        _output.WriteLine($"stdout: {stdout}");
        if (!string.IsNullOrEmpty(stderr)) _output.WriteLine($"stderr: {stderr}");

        exit.ShouldBe(0, $"DC-only Network construction must succeed. stderr:\n{stderr}");
        stdout.ShouldContain("BUILD_OK:");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether a Python interpreter with <c>photontorch</c> is reachable.
    /// Tries <c>python</c> first, then <c>python3</c>. Returns the executable
    /// that succeeded in <paramref name="pythonExe"/> — null if neither worked.
    /// </summary>
    private static bool PythonWithPhotonTorchAvailable(out string? pythonExe, out string reason)
    {
        foreach (var candidate in new[] { "python", "python3" })
        {
            if (TryProbe(candidate, out var probeReason))
            {
                pythonExe = candidate;
                reason = "";
                return true;
            }
            // Keep the last probe reason in case both fail.
            reason = probeReason;
        }

        pythonExe = null;
        reason = "No usable Python found (need 'python' or 'python3' with photontorch installed).";
        return false;
    }

    private static bool TryProbe(string pythonExe, out string reason)
    {
        try
        {
            var psi = new ProcessStartInfo(pythonExe, "-c \"import photontorch; print(photontorch.__version__)\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                reason = $"Failed to start '{pythonExe}'.";
                return false;
            }
            p.WaitForExit(30_000);
            if (p.ExitCode != 0)
            {
                reason = $"'{pythonExe}' found but photontorch import failed (exit {p.ExitCode}): {p.StandardError.ReadToEnd().Trim()}";
                return false;
            }
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Probe for '{pythonExe}' threw: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Marker inserted by the exporter before the simulation block. The integration
    /// test truncates the script at this marker so we test only what *we* generate
    /// (the Network construction), independent of photontorch's own torch-version
    /// compatibility (photontorch 0.4.1 uses the removed torch.solve on torch≥1.9).
    /// </summary>
    private const string SimulationMarker = "# ── Simulation";

    private static (int exitCode, string stdout, string stderr) RunPythonScript(string pythonExe, string script)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"lunima_pt_{Guid.NewGuid():N}.py");

        // Truncate before the simulation section: we only verify the exporter's
        // output, not photontorch's simulation runtime.
        var markerIdx = script.IndexOf(SimulationMarker, StringComparison.Ordinal);
        var constructionOnly = markerIdx > 0 ? script[..markerIdx] : script;

        var headless =
            "import matplotlib\nmatplotlib.use('Agg')\n"
            + constructionOnly
            + "\nprint('BUILD_OK:', type(nw).__name__)\n";
        // Write as UTF-8 without BOM — Python 3 defaults to UTF-8 for source files
        // but chokes on a BOM before the first statement in some versions.
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

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(120_000);
            return (p.ExitCode, stdout, stderr);
        }
        finally
        {
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
    }
}
