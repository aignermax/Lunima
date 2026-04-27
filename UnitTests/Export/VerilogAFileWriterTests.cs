using System.IO;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Tests the VerilogAFileWriter behaviour that PR #493 introduces:
/// the README it emits documents the per-circuit subfolder layout, so users
/// who open an exported folder discover where re-exports of other circuits go.
/// The actual subfolder placement is tested at the call site (the ViewModel
/// constructs the subfolder path); this class only owns the README content.
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

    [Fact]
    public async Task WriteAsync_ReadmeMentionsSubfolderLayout()
    {
        var result = new VerilogAExportResult
        {
            Success = true,
            CircuitName = "MyCircuit",
            TopLevelNetlist = "// top-level netlist",
            ComponentFiles = new Dictionary<string, string>
            {
                ["waveguide.va"] = "// waveguide module"
            }
        };

        await _writer.WriteAsync(result, _tempRoot);

        var readme = await File.ReadAllTextAsync(Path.Combine(_tempRoot, "README.md"));
        readme.ShouldContain("LunimaExport/<CircuitName>/");
    }
}
