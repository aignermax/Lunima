using System.IO;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Tests for VerilogAFileWriter — verifies that files are written to the correct locations.
/// </summary>
public class VerilogAFileWriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly VerilogAFileWriter _writer = new();

    public VerilogAFileWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static VerilogAExportResult MakeResult(string circuitName)
    {
        return new VerilogAExportResult
        {
            Success = true,
            CircuitName = circuitName,
            TopLevelNetlist = "// top-level netlist",
            SpiceTestBench = "* spice bench",
            ComponentFiles = new Dictionary<string, string>
            {
                ["waveguide.va"] = "// waveguide module"
            }
        };
    }

    [Fact]
    public async Task WriteAsync_CreatesFilesUnderOutputDirectory()
    {
        var result = MakeResult("TestCircuit");

        await _writer.WriteAsync(result, _tempRoot);

        File.Exists(Path.Combine(_tempRoot, "TestCircuit.va")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempRoot, "TestCircuit.sp")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempRoot, "README.md")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempRoot, "components", "waveguide.va")).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteAsync_TwoCircuitsInSubfolders_DoNotOverwriteEachOther()
    {
        // Simulates the issue-493 scenario: two different circuits exported into
        // per-circuit subfolders under the same base directory.
        var resultA = MakeResult("CircuitA");
        var resultB = MakeResult("CircuitB");

        var dirA = Path.Combine(_tempRoot, "CircuitA");
        var dirB = Path.Combine(_tempRoot, "CircuitB");

        await _writer.WriteAsync(resultA, dirA);
        await _writer.WriteAsync(resultB, dirB);

        // Both sets of files must coexist.
        File.Exists(Path.Combine(dirA, "CircuitA.va")).ShouldBeTrue();
        File.Exists(Path.Combine(dirB, "CircuitB.va")).ShouldBeTrue();
        File.Exists(Path.Combine(dirA, "README.md")).ShouldBeTrue();
        File.Exists(Path.Combine(dirB, "README.md")).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteAsync_ReadmeMentionsSubfolderLayout()
    {
        var result = MakeResult("MyCircuit");

        await _writer.WriteAsync(result, _tempRoot);

        var readme = await File.ReadAllTextAsync(Path.Combine(_tempRoot, "README.md"));
        readme.ShouldContain("LunimaExport/<CircuitName>/");
    }
}
