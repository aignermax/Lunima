using CAP_DataAccess.Import;
using Shouldly;
using System.Numerics;

namespace UnitTests.Import;

/// <summary>
/// Unit tests for Lumerical and Touchstone S-parameter importers.
/// All tests use in-memory temp files — no external files required.
/// </summary>
public class SParameterImporterTests
{
    // ── LumericalSParameterImporter ──────────────────────────────────────────

    [Fact]
    public async Task Lumerical_Sparam_ParsesBlockedFormat()
    {
        const double freq1550 = 299_792_458.0 / 1550e-9; // ~193.4 THz

        var content = $@"('port 1','TE',0,'port 2',0,'transmission')
(3,3)
{freq1550:G15}  0.95  0.05
{freq1550 * 0.99:G15}  0.94  0.06
{freq1550 * 1.01:G15}  0.96  0.04
('port 2','TE',0,'port 1',0,'transmission')
(3,3)
{freq1550:G15}  0.95  0.05
{freq1550 * 0.99:G15}  0.94  0.06
{freq1550 * 1.01:G15}  0.96  0.04
";
        var path = WriteTempFile(content, ".sparam");
        var importer = new LumericalSParameterImporter();

        var result = await importer.ImportAsync(path);

        result.PortCount.ShouldBe(2);
        result.SMatricesByWavelengthNm.ShouldNotBeEmpty();
        result.SourceFormat.ShouldBe("Lumerical SiEPIC");
    }

    [Fact]
    public async Task Lumerical_Sparam_ThrowsOnMissingFile()
    {
        var importer = new LumericalSParameterImporter();
        await Should.ThrowAsync<SParameterImportException>(() =>
            importer.ImportAsync("/nonexistent/file.sparam"));
    }

    [Fact]
    public async Task Lumerical_Sparam_ReportsCorrectWavelengths()
    {
        // Frequencies at 1500 nm and 1600 nm
        double f1500 = 299_792_458.0 / 1500e-9;
        double f1600 = 299_792_458.0 / 1600e-9;

        var content = $@"('port 1','TE',0,'port 2',0,'transmission')
(2,3)
{f1500:G15}  0.9  0.1
{f1600:G15}  0.85  0.15
('port 2','TE',0,'port 1',0,'transmission')
(2,3)
{f1500:G15}  0.9  0.1
{f1600:G15}  0.85  0.15
";
        var path = WriteTempFile(content, ".sparam");
        var importer = new LumericalSParameterImporter();

        var result = await importer.ImportAsync(path);

        result.SMatricesByWavelengthNm.Keys.ShouldContain(1500);
        result.SMatricesByWavelengthNm.Keys.ShouldContain(1600);
    }

    [Fact]
    public async Task Lumerical_GcTxt_ParsesPackedFormat()
    {
        // GC .txt format: freq |S11| ang(S11) |S21| ang(S21) |S12| ang(S12) |S22| ang(S22)
        double f1550 = 299_792_458.0 / 1550e-9;
        var content = $"{f1550:G15} 0.05 10 0.95 80 0.95 80 0.05 10\n";
        var path = WriteTempFile(content, ".txt");
        var importer = new LumericalSParameterImporter();

        var result = await importer.ImportAsync(path);

        result.PortCount.ShouldBe(2);
        result.SMatricesByWavelengthNm.ShouldNotBeEmpty();
        result.SourceFormat.ShouldBe("Lumerical GC TXT");
    }

    [Fact]
    public async Task Lumerical_Sparam_ComplexValuesAreCorrect()
    {
        double freq = 299_792_458.0 / 1550e-9;
        double mag = 0.9;
        double phase = Math.PI / 4; // 45 degrees

        var content = $@"('port 1','TE',0,'port 2',0,'transmission')
(1,3)
{freq:G15}  {mag:G6}  {phase:G6}
";
        var path = WriteTempFile(content, ".sparam");
        var importer = new LumericalSParameterImporter();

        var result = await importer.ImportAsync(path);

        var matrix = result.SMatricesByWavelengthNm[1550];
        int port1Idx = result.PortNames.IndexOf("port 1");
        int port2Idx = result.PortNames.IndexOf("port 2");

        var entry = matrix[port1Idx, port2Idx];
        entry.Magnitude.ShouldBe(mag, tolerance: 1e-6);
        entry.Phase.ShouldBe(phase, tolerance: 1e-6);
    }

    // ── TouchstoneImporter ───────────────────────────────────────────────────

    [Fact]
    public async Task Touchstone_S2P_ParsesMAFormat()
    {
        // 2-port, MA format (magnitude-angle in degrees), GHz
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9; // ~193.4 GHz
        var content = $@"# GHz S MA R 50
{freqGHz:G6} 0.05 10 0.95 80 0.95 80 0.05 10
";
        var path = WriteTempFile(content, ".s2p");
        var importer = new TouchstoneImporter();

        var result = await importer.ImportAsync(path);

        result.PortCount.ShouldBe(2);
        result.SMatricesByWavelengthNm.ShouldNotBeEmpty();
        result.SourceFormat.ShouldBe("Touchstone S2P");
    }

    [Fact]
    public async Task Touchstone_S2P_ParsesRIFormat()
    {
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        var content = $@"# GHz S RI R 50
{freqGHz:G6} 0.03 0.04 0.6 0.8 0.6 0.8 0.03 0.04
";
        var path = WriteTempFile(content, ".s2p");
        var importer = new TouchstoneImporter();

        var result = await importer.ImportAsync(path);

        var matrix = result.SMatricesByWavelengthNm.Values.First();
        var s11 = matrix[0, 0];
        s11.Real.ShouldBe(0.03, tolerance: 1e-6);
        s11.Imaginary.ShouldBe(0.04, tolerance: 1e-6);
    }

    [Fact]
    public async Task Touchstone_S2P_ParsesDBFormat()
    {
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        var content = $@"# GHz S DB R 50
{freqGHz:G6} -20 45 -0.45 80 -0.45 80 -20 45
";
        var path = WriteTempFile(content, ".s2p");
        var importer = new TouchstoneImporter();

        var result = await importer.ImportAsync(path);

        result.SMatricesByWavelengthNm.ShouldNotBeEmpty();
        var matrix = result.SMatricesByWavelengthNm.Values.First();
        // S11 in dB: -20 dB → magnitude 0.1
        matrix[0, 0].Magnitude.ShouldBe(0.1, tolerance: 1e-5);
    }

    [Fact]
    public async Task Touchstone_CommentsAndBlankLinesIgnored()
    {
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        var content = $@"! This is a comment
# GHz S MA R 50
! Another comment

{freqGHz:G6} 0.05 10 0.95 80 0.95 80 0.05 10
";
        var path = WriteTempFile(content, ".s2p");
        var importer = new TouchstoneImporter();

        var result = await importer.ImportAsync(path);

        result.SMatricesByWavelengthNm.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Touchstone_ThrowsOnMissingFile()
    {
        var importer = new TouchstoneImporter();
        await Should.ThrowAsync<SParameterImportException>(() =>
            importer.ImportAsync("/nonexistent/file.s2p"));
    }

    // ── SParameterConverter ──────────────────────────────────────────────────

    [Fact]
    public async Task Converter_ProducesCorrectRowMajorLayout()
    {
        double freq = 299_792_458.0 / 1550e-9;
        var content = $@"('port 1','TE',0,'port 2',0,'transmission')
(1,3)
{freq:G15}  0.9  1.0
('port 2','TE',0,'port 1',0,'transmission')
(1,3)
{freq:G15}  0.8  0.5
('port 1','TE',0,'port 1',0,'transmission')
(1,3)
{freq:G15}  0.1  0.2
('port 2','TE',0,'port 2',0,'transmission')
(1,3)
{freq:G15}  0.05  0.3
";
        var path = WriteTempFile(content, ".sparam");
        var imported = await new LumericalSParameterImporter().ImportAsync(path);

        var data = SParameterConverter.ToComponentSMatrixData(imported);

        data.Wavelengths.ShouldContainKey("1550");
        var entry = data.Wavelengths["1550"];
        entry.Rows.ShouldBe(2);
        entry.Cols.ShouldBe(2);
        entry.Real.Count.ShouldBe(4); // 2x2 = 4
        entry.Imag.Count.ShouldBe(4);
        entry.PortNames.ShouldNotBeNull();
        entry.PortNames!.Count.ShouldBe(2);
    }

    // ── Integration test ─────────────────────────────────────────────────────

    [Fact]
    public async Task SParameterImportViewModel_ImportsAndStoresData()
    {
        // Arrange: create a real .sparam file, a real dictionary, and the ViewModel
        double freq = 299_792_458.0 / 1550e-9;
        var sparamContent = $@"('port 1','TE',0,'port 2',0,'transmission')
(1,3)
{freq:G15}  0.95  0.1
('port 2','TE',0,'port 1',0,'transmission')
(1,3)
{freq:G15}  0.95  0.1
";
        var filePath = WriteTempFile(sparamContent, ".sparam");

        var storedSMatrices = new CAP_DataAccess.Persistence.PIR.ComponentSMatrixData().GetType()
            == typeof(CAP_DataAccess.Persistence.PIR.ComponentSMatrixData)
            ? new Dictionary<string, CAP_DataAccess.Persistence.PIR.ComponentSMatrixData>()
            : null;

        storedSMatrices = new Dictionary<string, CAP_DataAccess.Persistence.PIR.ComponentSMatrixData>();

        var vm = new CAP.Avalonia.ViewModels.Import.SParameterImportViewModel
        {
            StoredSMatrices = storedSMatrices,
            FilePath = filePath,
            ComponentIdentifier = "test_component"
        };

        // Act: run import command
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert: ViewModel reflects success and dictionary has data
        vm.LastImportSucceeded.ShouldBeTrue(vm.StatusText);
        storedSMatrices.ShouldContainKey("test_component");
        storedSMatrices["test_component"].Wavelengths.ShouldContainKey("1550");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string WriteTempFile(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sparam_test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        return path;
    }
}
