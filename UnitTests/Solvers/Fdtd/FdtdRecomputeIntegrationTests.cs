using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Solvers;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Solvers.Fdtd;

/// <summary>
/// End-to-end integration test for "Recalculate S-matrix (FDTD)" on a real PDK
/// component (SiEPIC EBeam <c>ebeam_DC_2-1_te895</c> — one of the smallest, so it
/// solves quickly): render → request factory → Docker/Meep solve → convert →
/// apply, asserting the recomputed S-matrix maps onto the component's pins
/// (the #578 fix — no skipped wavelengths).
///
/// HEAVY + opt-in: it builds/runs the Meep Docker image and renders via Nazca,
/// so it only runs when <c>LUNIMA_FDTD_INTEGRATION=1</c> AND Docker + a working
/// render Python are present. Otherwise it no-ops (keeps CI fast/green).
/// </summary>
[Trait("Category", "FdtdIntegration")]
public class FdtdRecomputeIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public FdtdRecomputeIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Recompute_SiepicDc21_AppliesToAllPorts_WithoutSkippedWavelengths()
    {
        if (Environment.GetEnvironmentVariable("LUNIMA_FDTD_INTEGRATION") != "1")
        {
            _output.WriteLine("skip: set LUNIMA_FDTD_INTEGRATION=1 to run (needs Docker + SiEPIC render deps).");
            return;
        }

        var python = FindWorkingPython3();
        if (python == null) { _output.WriteLine("skip: no working python3 for the Nazca render."); return; }

        var dockerfile = FindUp(Path.Combine("scripts", "fdtd", "Dockerfile"));
        var previewScript = FindUp(Path.Combine("scripts", "render_component_preview.py"));
        if (dockerfile == null || previewScript == null)
        {
            _output.WriteLine("skip: scripts/fdtd/Dockerfile or render_component_preview.py not found.");
            return;
        }

        if (!DockerAvailable())
        {
            _output.WriteLine("skip: Docker engine not available.");
            return;
        }

        var buildContext = Directory.GetParent(Path.GetDirectoryName(dockerfile)!)!.FullName; // scripts/
        var fdtd = new DockerFdtdSMatrixService("lunima-meep:1", dockerfile, buildContext);

        // Real SiEPIC DC 2-1 component (its pins are "port 1".."port 4").
        var templates = TestPdkLoader.LoadFromPdk("siepic-ebeam-pdk.json");
        var dcTemplate = templates.FirstOrDefault(t => t.NazcaFunctionName == "ebeam_DC_2-1_te895")
                         ?? templates.FirstOrDefault(t => t.Name == "DC 2-1 TE 895");
        dcTemplate.ShouldNotBeNull("SiEPIC DC 2-1 template not found in siepic-ebeam-pdk.json");
        var component = ComponentTemplates.CreateFromTemplate(dcTemplate!, 0, 0);

        var preview = new NazcaComponentPreviewService(python, previewScript);
        var factory = new ComponentFdtdRequestFactory(preview);

        var request = await factory.BuildAsync(component);
        request.ShouldNotBeNull("render/factory produced no request — SiEPIC render deps (klayout/siepic) present?");

        var result = await fdtd.SolveAsync(request!);
        result.Success.ShouldBeTrue($"FDTD failed: {result.Error}\n{result.RawStderr}");

        var data = FdtdSMatrixConverter.ToComponentSMatrixData(result, "FDTD integration test");
        var apply = SMatrixOverrideApplicator.Apply(component, data, errorConsole: null);

        // The core of #578: ports are named after the component pins, so the
        // override maps onto every wavelength rather than being skipped.
        apply.Skipped.ShouldBeEmpty("port-name mismatch would skip wavelengths");
        apply.Applied.ShouldBeGreaterThan(0);

        // Loose passivity sanity only — absolute accuracy needs material calibration
        // (#570 stage 3); uncalibrated SOI defaults are lossy, not near 1.
        foreach (var energy in result.EnergySumPerInput.Values)
            energy.ShouldBeLessThanOrEqualTo(1.05);

        _output.WriteLine($"OK: applied {apply.Applied} wavelength(s); " +
                          $"max energy Σ|S|² = {result.EnergySumPerInput.Values.DefaultIfEmpty(0).Max():F3}");
    }

    private static string? FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static bool DockerAvailable()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version --format {{.Server.Version}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p == null) return false;
            p.WaitForExit(20000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string? FindWorkingPython3()
    {
        var envOverride = Environment.GetEnvironmentVariable("LUNIMA_TEST_PYTHON3");
        var candidates = !string.IsNullOrWhiteSpace(envOverride)
            ? new[] { envOverride, "python3", "py", "python" }
            : new[] { "/usr/bin/python3", "/usr/local/bin/python3", "python3", "py", "python" };
        foreach (var c in candidates)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = c,
                    Arguments = "-c \"print('ok')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (p == null) continue;
                p.WaitForExit(10000);
                if (p.HasExited && p.ExitCode == 0) return c;
            }
            catch { /* try next candidate */ }
        }
        return null;
    }
}
