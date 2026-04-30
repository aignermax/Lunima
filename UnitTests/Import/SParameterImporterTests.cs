using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using CAP_DataAccess.Import;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;

namespace UnitTests.Import;

/// <summary>
/// Unit tests for Lumerical and Touchstone S-parameter importers.
/// All tests use in-memory temp files — no external files required.
/// All numeric interpolations go through <see cref="Inv"/> + the <see cref="Fmt"/>
/// helper so the test suite remains correct on non-en-US machines (production
/// parsers use InvariantCulture; tests must too, or they write "0,95" on de-DE
/// and silently fail to parse).
/// </summary>
public class SParameterImporterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Round-trip-safe double formatter — "R" guarantees exact re-parse.</summary>
    private static string Fmt(double d) => d.ToString("R", Inv);

    // ── LumericalSParameterImporter ──────────────────────────────────────────

    [Fact]
    public async Task Lumerical_Sparam_ParsesBlockedFormat()
    {
        double freq1550 = 299_792_458.0 / 1550e-9;
        var content = $@"('port 1','TE',0,'port 2',0,'transmission')
(3,3)
{Fmt(freq1550)}  0.95  0.05
{Fmt(freq1550 * 0.99)}  0.94  0.06
{Fmt(freq1550 * 1.01)}  0.96  0.04
('port 2','TE',0,'port 1',0,'transmission')
(3,3)
{Fmt(freq1550)}  0.95  0.05
{Fmt(freq1550 * 0.99)}  0.94  0.06
{Fmt(freq1550 * 1.01)}  0.96  0.04
";
        using var tmp = WriteTempFile(content, ".sparam");
        var result = await new LumericalSParameterImporter().ImportAsync(tmp.Path);

        result.PortCount.ShouldBe(2);
        result.SMatricesByWavelengthNm.ShouldNotBeEmpty();
        result.SourceFormat.ShouldBe("Lumerical SiEPIC");
    }

    [Fact]
    public async Task Lumerical_Sparam_ThrowsOnMissingFile()
    {
        await Should.ThrowAsync<SParameterImportException>(() =>
            new LumericalSParameterImporter().ImportAsync("/nonexistent/file.sparam"));
    }

    [Fact]
    public async Task Lumerical_Sparam_ReportsCorrectWavelengths()
    {
        double f1500 = 299_792_458.0 / 1500e-9;
        double f1600 = 299_792_458.0 / 1600e-9;

        var content = $@"('port 1','TE',0,'port 2',0,'transmission')
(2,3)
{Fmt(f1500)}  0.9  0.1
{Fmt(f1600)}  0.85  0.15
('port 2','TE',0,'port 1',0,'transmission')
(2,3)
{Fmt(f1500)}  0.9  0.1
{Fmt(f1600)}  0.85  0.15
";
        using var tmp = WriteTempFile(content, ".sparam");
        var result = await new LumericalSParameterImporter().ImportAsync(tmp.Path);

        result.SMatricesByWavelengthNm.Keys.ShouldContain(1500);
        result.SMatricesByWavelengthNm.Keys.ShouldContain(1600);
    }

    [Fact]
    public async Task Lumerical_GcTxt_ParsesPackedFormat()
    {
        double f1550 = 299_792_458.0 / 1550e-9;
        // freq |S11| ang(S11) |S21| ang(S21) |S12| ang(S12) |S22| ang(S22)
        var content = $"{Fmt(f1550)} 0.05 10 0.95 80 0.95 80 0.05 10\n";
        using var tmp = WriteTempFile(content, ".txt");
        var result = await new LumericalSParameterImporter().ImportAsync(tmp.Path);

        result.PortCount.ShouldBe(2);
        result.SMatricesByWavelengthNm.ShouldNotBeEmpty();
        result.SourceFormat.ShouldBe("Lumerical GC TXT");
    }

    [Fact]
    public async Task Lumerical_Sparam_ComplexValuesAreCorrect()
    {
        // Use the exact frequency that round-trips back to 1550 nm via the
        // parser's integer-nm rounding. Round-trip safe "R" formatting avoids
        // the original G6-precision bug.
        double freq = 299_792_458.0 / 1550e-9;
        double mag = 0.9;
        double phase = Math.PI / 4;

        var content = $@"('port 1','TE',0,'port 2',0,'transmission')
(1,3)
{Fmt(freq)}  {Fmt(mag)}  {Fmt(phase)}
";
        using var tmp = WriteTempFile(content, ".sparam");
        var result = await new LumericalSParameterImporter().ImportAsync(tmp.Path);

        var matrix = result.SMatricesByWavelengthNm[1550];
        int p1 = result.PortNames.IndexOf("port 1");
        int p2 = result.PortNames.IndexOf("port 2");
        var entry = matrix[p1, p2];

        entry.Magnitude.ShouldBe(mag, tolerance: 1e-6);
        entry.Phase.ShouldBe(phase, tolerance: 1e-6);
    }

    [Fact]
    public async Task Lumerical_Sparam_PortsSortedNumerically()
    {
        // With >9 ports, lexicographic sort puts "port 10" before "port 2".
        // Natural sort must preserve "port 1, port 2, ..., port 10".
        double freq = 299_792_458.0 / 1550e-9;
        var sb = new System.Text.StringBuilder();
        foreach (var (o, i) in new[] { ("port 1", "port 2"), ("port 2", "port 10"), ("port 10", "port 1") })
        {
            sb.AppendLine(Inv, $"('{o}','TE',0,'{i}',0,'transmission')");
            sb.AppendLine("(1,3)");
            sb.AppendLine(Inv, $"{Fmt(freq)}  0.5  0.0");
        }
        using var tmp = WriteTempFile(sb.ToString(), ".sparam");
        var result = await new LumericalSParameterImporter().ImportAsync(tmp.Path);

        result.PortNames.ShouldBe(new[] { "port 1", "port 2", "port 10" });
    }

    // ── TouchstoneImporter ───────────────────────────────────────────────────

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public async Task Touchstone_S2P_MA_IsCultureSafe(string cultureName)
    {
        // Pins the InvariantCulture promise on the production parser: a
        // fr-FR developer running tests must still get the same result.
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);
        try
        {
            double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
            var content = $@"# GHz S MA R 50
{Fmt(freqGHz)} 0.05 10 0.95 80 0.95 80 0.05 10
";
            using var tmp = WriteTempFile(content, ".s2p");
            var result = await new TouchstoneImporter().ImportAsync(tmp.Path);

            result.SMatricesByWavelengthNm.ShouldNotBeEmpty();
            result.SMatricesByWavelengthNm.Values.First()[0, 0].Magnitude.ShouldBe(0.05, tolerance: 1e-6);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("Hz",  1.0)]
    [InlineData("kHz", 1e3)]
    [InlineData("MHz", 1e6)]
    [InlineData("GHz", 1e9)]
    public async Task Touchstone_S2P_AllFreqUnits_ProduceSameWavelength(string unit, double multiplier)
    {
        // Target 1550 nm for every unit; without proper unit scaling, most of
        // these would land on different wavelength keys.
        double targetFreqHz = 299_792_458.0 / 1550e-9;
        double freqInUnit = targetFreqHz / multiplier;

        var content = $@"# {unit} S MA R 50
{Fmt(freqInUnit)} 0.1 0 0.9 0 0.9 0 0.1 0
";
        using var tmp = WriteTempFile(content, ".s2p");
        var result = await new TouchstoneImporter().ImportAsync(tmp.Path);

        result.SMatricesByWavelengthNm.Keys.ShouldContain(1550);
    }

    [Fact]
    public async Task Touchstone_S2P_RIFormat_ParsesRealAndImaginary()
    {
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        var content = $@"# GHz S RI R 50
{Fmt(freqGHz)} 0.03 0.04 0.6 0.8 0.6 0.8 0.03 0.04
";
        using var tmp = WriteTempFile(content, ".s2p");
        var result = await new TouchstoneImporter().ImportAsync(tmp.Path);

        var s11 = result.SMatricesByWavelengthNm.Values.First()[0, 0];
        s11.Real.ShouldBe(0.03, tolerance: 1e-6);
        s11.Imaginary.ShouldBe(0.04, tolerance: 1e-6);
    }

    [Fact]
    public async Task Touchstone_S2P_DBFormat_ConvertsMagnitudeCorrectly()
    {
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        // -20 dB → linear magnitude 0.1
        var content = $@"# GHz S DB R 50
{Fmt(freqGHz)} -20 45 -0.45 80 -0.45 80 -20 45
";
        using var tmp = WriteTempFile(content, ".s2p");
        var result = await new TouchstoneImporter().ImportAsync(tmp.Path);

        result.SMatricesByWavelengthNm.Values.First()[0, 0].Magnitude.ShouldBe(0.1, tolerance: 1e-5);
    }

    [Fact]
    public async Task Touchstone_S4P_PreservesAsymmetricMatrix()
    {
        // Column-major ordering sensitivity: symmetric 2×2 data hides a
        // transpose. A 4-port file with 16 distinct values catches it.
        // Touchstone S4P column-major: freq S11 S21 S31 S41 S12 S22 S32 S42 S13 S23 S33 S43 S14 S24 S34 S44
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        // Use RI so each cell is (row, col) for easy indexing:
        // pair k → row = k%4, col = k/4 (Touchstone convention).
        var pairs = new System.Text.StringBuilder();
        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                pairs.Append(Inv, $" {row}.{col}");        // real = row.col
                pairs.Append(Inv, $" {row}{col}.0e-1");    // imag = 0.row col
            }
        }
        var content = $"# GHz S RI R 50\n{Fmt(freqGHz)}{pairs}\n";
        using var tmp = WriteTempFile(content, ".s4p");
        var result = await new TouchstoneImporter().ImportAsync(tmp.Path);

        result.PortCount.ShouldBe(4);
        var m = result.SMatricesByWavelengthNm.Values.First();
        // S[1,2] and S[2,1] must be different (asymmetric) — this is the
        // only test that proves column-major reading works correctly.
        m[1, 2].Real.ShouldBe(1.2, tolerance: 1e-6);
        m[2, 1].Real.ShouldBe(2.1, tolerance: 1e-6);
        m[0, 3].Real.ShouldBe(0.3, tolerance: 1e-6);
        m[3, 0].Real.ShouldBe(3.0, tolerance: 1e-6);
    }

    [Fact]
    public async Task Touchstone_UnknownFreqUnit_Throws()
    {
        // A typo in the `#` header (e.g. "THZ" for THz) must fail loudly;
        // silent fallback to Hz would produce wildly wrong wavelengths.
        var content = @"# THz S MA R 50
0.19 0.1 0 0.9 0 0.9 0 0.1 0
";
        using var tmp = WriteTempFile(content, ".s2p");
        var ex = await Should.ThrowAsync<SParameterImportException>(() =>
            new TouchstoneImporter().ImportAsync(tmp.Path));
        ex.Message.ShouldContain("THz");
    }

    [Fact]
    public async Task Touchstone_UnknownDataFormat_Throws()
    {
        // "MAG" instead of "MA" is a realistic typo — silent fallback to RI
        // would import the values as real/imag and corrupt the simulation.
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        var content = $@"# GHz S MAG R 50
{Fmt(freqGHz)} 0.05 10 0.95 80 0.95 80 0.05 10
";
        using var tmp = WriteTempFile(content, ".s2p");
        var ex = await Should.ThrowAsync<SParameterImportException>(() =>
            new TouchstoneImporter().ImportAsync(tmp.Path));
        ex.Message.ShouldContain("MAG");
    }

    [Fact]
    public async Task Touchstone_YParameterType_Throws()
    {
        // Y-parameters interpreted as S-parameters would produce wrong
        // simulation. Must reject explicitly.
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        var content = $@"# GHz Y MA R 50
{Fmt(freqGHz)} 0.05 10 0.95 80 0.95 80 0.05 10
";
        using var tmp = WriteTempFile(content, ".s2p");
        var ex = await Should.ThrowAsync<SParameterImportException>(() =>
            new TouchstoneImporter().ImportAsync(tmp.Path));
        ex.Message.ShouldContain("Y");
    }

    [Fact]
    public async Task Touchstone_NoiseDataAfterSParams_IsIgnored()
    {
        // Real VNA Touchstone files append noise-parameter blocks. The noise
        // block re-uses the frequency column but drops to fewer tokens per
        // row. Detect via non-monotonic frequency and stop there — the
        // noise values must not appear as extra "wavelengths".
        double f1 = 299_792_458.0 / 1550e-9 / 1e9; // GHz
        double f2 = 299_792_458.0 / 1540e-9 / 1e9;
        var content = $@"# GHz S MA R 50
{Fmt(f1)} 0.1 0 0.9 0 0.9 0 0.1 0
{Fmt(f2)} 0.11 0 0.88 0 0.88 0 0.11 0
! Noise block (lower frequencies, fewer columns)
{Fmt(f1 * 0.5)} 1.5 0.0 50 0
{Fmt(f2 * 0.5)} 1.6 0.0 50 0
";
        using var tmp = WriteTempFile(content, ".s2p");
        var result = await new TouchstoneImporter().ImportAsync(tmp.Path);

        // Only the two genuine S-parameter frequencies should survive;
        // 1550 and 1540 nm land in the dict, the noise block at 3100/3080 nm
        // must not.
        result.SMatricesByWavelengthNm.Count.ShouldBe(2);
        result.SMatricesByWavelengthNm.Keys.ShouldContain(1550);
        result.SMatricesByWavelengthNm.Keys.ShouldContain(1540);
    }

    [Fact]
    public async Task Touchstone_CommentsAndBlankLinesIgnored()
    {
        double freqGHz = 299_792_458.0 / 1550e-9 / 1e9;
        var content = $@"! This is a comment
# GHz S MA R 50
! Another comment

{Fmt(freqGHz)} 0.05 10 0.95 80 0.95 80 0.05 10
";
        using var tmp = WriteTempFile(content, ".s2p");
        var result = await new TouchstoneImporter().ImportAsync(tmp.Path);

        result.SMatricesByWavelengthNm.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Touchstone_ThrowsOnMissingFile()
    {
        await Should.ThrowAsync<SParameterImportException>(() =>
            new TouchstoneImporter().ImportAsync("/nonexistent/file.s2p"));
    }

    // ── SParameterConverter ──────────────────────────────────────────────────

    [Fact]
    public async Task Converter_ProducesRowMajorLayoutWithFromPortAsRow()
    {
        // Pins the row-major convention: row=from-port, col=to-port.
        // Asymmetric values ensure a transpose would fail this test.
        double freq = 299_792_458.0 / 1550e-9;
        var content = $@"('port 1','TE',0,'port 2',0,'transmission')
(1,3)
{Fmt(freq)}  0.9  0.0
('port 2','TE',0,'port 1',0,'transmission')
(1,3)
{Fmt(freq)}  0.5  0.0
('port 1','TE',0,'port 1',0,'transmission')
(1,3)
{Fmt(freq)}  0.1  0.0
('port 2','TE',0,'port 2',0,'transmission')
(1,3)
{Fmt(freq)}  0.2  0.0
";
        using var tmp = WriteTempFile(content, ".sparam");
        var imported = await new LumericalSParameterImporter().ImportAsync(tmp.Path);

        var data = SParameterConverter.ToComponentSMatrixData(imported);

        data.Wavelengths.ShouldContainKey("1550");
        var entry = data.Wavelengths["1550"];
        entry.Rows.ShouldBe(2);
        entry.Cols.ShouldBe(2);
        entry.Real.Count.ShouldBe(4);
        // Row-major: [r*n + c]. The block header `('port 1', ..., 'port 2', ...)`
        // in Lumerical format means OUT=port 1, IN=port 2 — the parser maps
        // that to matrix[outIdx, inIdx], i.e. row 0 col 1 = 0.9.
        entry.Real[0 * 2 + 1].ShouldBe(0.9, tolerance: 1e-6); // port1→port2
        entry.Real[1 * 2 + 0].ShouldBe(0.5, tolerance: 1e-6); // port2→port1
        entry.Real[0 * 2 + 0].ShouldBe(0.1, tolerance: 1e-6);
        entry.Real[1 * 2 + 1].ShouldBe(0.2, tolerance: 1e-6);
    }

    [Fact]
    public void Converter_WavelengthKeysAreCultureInvariant()
    {
        // On fr-FR the default ToString() of 1550 is "1550" (no decimal),
        // but in more exotic cultures numeric formatting can inject non-ASCII
        // digits (arabic-indic, etc.). Invariant culture guarantees the JSON
        // key is always plain ASCII "1550".
        var imported = new ImportedSParameters
        {
            SourceFormat = "Test",
            PortCount = 1,
            PortNames = new List<string> { "p" },
            SMatricesByWavelengthNm = { [1550] = new Complex[1, 1] { { new(1, 0) } } }
        };

        var data = SParameterConverter.ToComponentSMatrixData(imported);

        data.Wavelengths.ShouldContainKey("1550");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Writes a temp file whose wrapper deletes it at end-of-scope.</summary>
    private static TempFile WriteTempFile(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sparam_test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        return new TempFile(path);
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }
        public TempFile(string path) => Path = path;
        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
