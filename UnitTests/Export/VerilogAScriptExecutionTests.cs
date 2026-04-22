using System.ComponentModel;
using System.Diagnostics;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Export;

/// <summary>
/// Runtime tests for the Verilog-A exporter: writes the generated component
/// files to a temp directory and runs the OpenVAF compiler against each
/// module + the top-level netlist to verify the output is a valid Verilog-A
/// program (not just a plausibly-shaped string).
///
/// <para>
/// A missing <c>openvaf</c> binary on PATH surfaces as a Skipped test
/// (<see cref="SkippableFact"/>), not a silent Passed. CI installs OpenVAF
/// explicitly — see <c>.github/workflows/xUnitTests.yaml</c>.
/// </para>
/// </summary>
public class VerilogAScriptExecutionTests
{
    private readonly ITestOutputHelper _output;
    private readonly VerilogAExporter _exporter = new();

    public VerilogAScriptExecutionTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public void GeneratedVerilogA_ForSingleWaveguide_CompilesWithOpenVAF()
    {
        SkipIfOpenVAFMissing(out var openvaf);

        var wg = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg.Identifier = "wg_solo";

        var result = _exporter.Export([wg], [], new VerilogAExportOptions
        {
            CircuitName = "single_wg_circuit",
            IncludeTestBench = false,
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            AssertEveryComponentFileCompiles(openvaf, dir, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [SkippableFact]
    public void GeneratedVerilogA_ForTwoConnectedWaveguides_CompilesWithOpenVAF()
    {
        SkipIfOpenVAFMissing(out var openvaf);

        var wg1 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg1.Identifier = "wg1";
        var wg2 = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        wg2.Identifier = "wg2";
        var conn = new WaveguideConnection { StartPin = wg1.PhysicalPins[1], EndPin = wg2.PhysicalPins[0] };

        var result = _exporter.Export([wg1, wg2], [conn], new VerilogAExportOptions
        {
            CircuitName = "two_wg_circuit",
            IncludeTestBench = false,
        });
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dir = WriteExportToTempDir(result);
        try
        {
            AssertEveryComponentFileCompiles(openvaf, dir, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void SkipIfOpenVAFMissing(out string openvafExe)
    {
        foreach (var candidate in new[] { "openvaf", "openvaf.exe" })
        {
            if (TryProbe(candidate))
            {
                openvafExe = candidate;
                return;
            }
        }
        openvafExe = "";
        Skip.If(true,
            "OpenVAF compiler not on PATH. Download from https://openvaf.semimod.de " +
            "or install via CI workflow.");
    }

    private static bool TryProbe(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, "--help")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(10_000);
            return p.HasExited;  // Any exit means the binary ran
        }
        catch (Win32Exception) { return false; }
        catch (FileNotFoundException) { return false; }
    }

    private static string WriteExportToTempDir(VerilogAExportResult result)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lunima_va_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var enc = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        foreach (var (filename, content) in result.ComponentFiles)
            File.WriteAllText(Path.Combine(dir, filename), content, enc);
        File.WriteAllText(Path.Combine(dir, $"{result.CircuitName}.va"), result.TopLevelNetlist, enc);

        return dir;
    }

    private void AssertEveryComponentFileCompiles(string openvaf, string dir, VerilogAExportResult result)
    {
        // Compile each standalone component module. OpenVAF resolves `include
        // directives relative to the input file, so running it in `dir` is enough.
        foreach (var filename in result.ComponentFiles.Keys)
        {
            var (exit, stdout, stderr) = RunOpenVAF(openvaf, Path.Combine(dir, filename));
            if (exit != 0)
            {
                _output.WriteLine($"--- {filename} ---\n{File.ReadAllText(Path.Combine(dir, filename))}");
                _output.WriteLine($"stdout: {stdout}");
                _output.WriteLine($"stderr: {stderr}");
            }
            exit.ShouldBe(0, $"OpenVAF rejected {filename}. stderr:\n{stderr}");
        }
    }

    private static (int exitCode, string stdout, string stderr) RunOpenVAF(string openvaf, string scriptPath)
    {
        var psi = new ProcessStartInfo(openvaf, $"\"{scriptPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{openvaf}'.");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit(60_000);
        Task.WaitAll(stdoutTask, stderrTask);
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
