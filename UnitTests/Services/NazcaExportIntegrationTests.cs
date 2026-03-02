using System.Diagnostics;
using System.Text;
using CAP.Avalonia.Services;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Integration tests that verify exported Nazca Python scripts are valid.
/// - Syntax check: always runs (requires Python)
/// - GDS generation: only runs if Nazca is installed
/// Both skip gracefully if prerequisites are not available.
/// </summary>
public class NazcaExportIntegrationTests
{
    private static string? FindPython()
    {
        foreach (var cmd in new[] { "python", "python3" })
        {
            try
            {
                var psi = new ProcessStartInfo(cmd, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
                if (proc?.ExitCode == 0) return cmd;
            }
            catch { }
        }
        return null;
    }

    private static bool IsNazcaInstalled(string python)
    {
        try
        {
            var psi = new ProcessStartInfo(python, "-c \"import nazca\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static (int exitCode, string stdout, string stderr) RunPython(
        string python, string args, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo(python, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(timeoutMs);
        return (proc.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Builds a minimal but realistic Nazca script with components and chained waveguides.
    /// </summary>
    private static string BuildTestScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("import nazca as nd");
        sb.AppendLine("import nazca.demofab as demo");
        sb.AppendLine("from nazca.interconnects import Interconnect");
        sb.AppendLine();
        sb.AppendLine("ic = Interconnect(width=0.45, radius=50)");
        sb.AppendLine();
        sb.AppendLine("def create_design():");
        sb.AppendLine("    with nd.Cell(name='test_design') as design:");
        sb.AppendLine();
        sb.AppendLine("        # Components");
        sb.AppendLine("        comp_0 = demo.mmi1x2_sh().put(0.00, 0.00, 0)");
        sb.AppendLine("        comp_1 = demo.mmi2x2_dp().put(500.00, 0.00, 0)");
        sb.AppendLine();

        // Build chained waveguide segments (first has coords, rest chain)
        var segments = new List<PathSegment>
        {
            new StraightSegment(100, 0, 200, 0, 0),
            new BendSegment(200, 50, 50, 0, 90),
            new StraightSegment(250, 50, 250, 150, 90),
            new BendSegment(200, 150, 50, 90, -90),
            new StraightSegment(200, 200, 400, 200, 0)
        };

        sb.AppendLine("        # Waveguide Connection");
        SimpleNazcaExporter.AppendSegmentExport(sb, segments);
        sb.AppendLine();
        sb.AppendLine("    return design");

        return sb.ToString();
    }

    [Fact]
    public void ExportedScript_IsValidPythonSyntax()
    {
        var python = FindPython();
        if (python == null) return; // skip if no Python

        var script = BuildTestScript();
        var tmpFile = Path.Combine(Path.GetTempPath(), $"cap_syntax_test_{Guid.NewGuid():N}.py");

        try
        {
            File.WriteAllText(tmpFile, script);

            var (exitCode, stdout, stderr) = RunPython(
                python,
                $"-c \"import py_compile; py_compile.compile('{tmpFile.Replace("\\", "/")}', doraise=True)\"");

            exitCode.ShouldBe(0,
                $"Python syntax check failed.\nStderr: {stderr}\nScript:\n{script}");
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ExportedScript_ChainedSegments_AreValidPython()
    {
        var python = FindPython();
        if (python == null) return;

        // Test various segment combinations
        var segments = new List<PathSegment>
        {
            new StraightSegment(0, 0, 100, 0, 0),
            new BendSegment(100, 50, 50, 0, 90),
            new StraightSegment(150, 50, 150, 200, 90),
            new BendSegment(100, 200, 50, 90, 90),
            new StraightSegment(100, 250, 0, 250, 180)
        };

        var sb = new StringBuilder();
        sb.AppendLine("import nazca as nd");
        sb.AppendLine("def test():");
        sb.AppendLine("    with nd.Cell(name='chain_test') as c:");
        SimpleNazcaExporter.AppendSegmentExport(sb, segments);
        sb.AppendLine("    return c");

        var script = sb.ToString();
        var tmpFile = Path.Combine(Path.GetTempPath(), $"cap_chain_test_{Guid.NewGuid():N}.py");

        try
        {
            File.WriteAllText(tmpFile, script);
            var (exitCode, _, stderr) = RunPython(
                python,
                $"-c \"import py_compile; py_compile.compile('{tmpFile.Replace("\\", "/")}', doraise=True)\"");

            exitCode.ShouldBe(0, $"Chained segment syntax invalid: {stderr}");
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ExportedScript_ProducesGdsFile_WhenNazcaInstalled()
    {
        var python = FindPython();
        if (python == null) return; // skip if no Python
        if (!IsNazcaInstalled(python)) return; // skip if no Nazca

        var script = BuildTestScript();

        // Add GDS export to a temp directory
        var tmpDir = Path.Combine(Path.GetTempPath(), $"cap_gds_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        var gdsPath = Path.Combine(tmpDir, "test_output.gds").Replace("\\", "/");
        var scriptPath = Path.Combine(tmpDir, "test_export.py");

        var fullScript = script + $@"

design = create_design()
design.put()
nd.export_gds(filename='{gdsPath}')
print('GDS_EXPORT_OK')
";

        try
        {
            File.WriteAllText(scriptPath, fullScript);

            var (exitCode, stdout, stderr) = RunPython(python, $"\"{scriptPath}\"", 60000);

            // Nazca may print warnings — check for our success marker
            stdout.ShouldContain("GDS_EXPORT_OK",
                customMessage: $"Nazca script failed.\nExit code: {exitCode}\nStdout: {stdout}\nStderr: {stderr}");

            File.Exists(gdsPath).ShouldBeTrue(
                customMessage: $"GDS file was not created at {gdsPath}");

            // GDS file should be non-empty
            new FileInfo(gdsPath).Length.ShouldBeGreaterThan(0,
                "GDS file is empty");
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }
}
