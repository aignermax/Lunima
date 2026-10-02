using System.Globalization;
using CAP_Core.Analysis.MeasuredSpectrum;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.MeasuredSpectrum;

public class MeasuredSpectrumCsvReaderTests
{
    [Fact]
    public void Parse_NanometerValues_KeepsThemUnchanged()
    {
        var csv = "1550.0,0.5\n1551.0,0.6";
        var spectrum = MeasuredSpectrumCsvReader.Parse(csv, PowerUnit.Linear);
        spectrum.WavelengthNm.ShouldBe(new[] { 1550.0, 1551.0 });
        spectrum.PowerLinear.ShouldBe(new[] { 0.5, 0.6 });
    }

    [Fact]
    public void Parse_MeterValues_AutoDetectsAndConvertsToNm()
    {
        var csv = "1.55e-6,0.5\n1.551e-6,0.6";
        var spectrum = MeasuredSpectrumCsvReader.Parse(csv, PowerUnit.Linear);
        spectrum.WavelengthNm[0].ShouldBe(1550.0, 1e-6);
        spectrum.WavelengthNm[1].ShouldBe(1551.0, 1e-6);
    }

    [Fact]
    public void Parse_DecibelPower_ConvertsToLinear()
    {
        var csv = "1550.0,0\n1551.0,-3.010299956639812";
        var spectrum = MeasuredSpectrumCsvReader.Parse(csv, PowerUnit.Decibel);
        spectrum.PowerLinear[0].ShouldBe(1.0, 1e-9);
        spectrum.PowerLinear[1].ShouldBe(0.5, 1e-9);
    }

    [Fact]
    public void Parse_WithHeader_SkipsHeaderLine()
    {
        var csv = "Wavelength_nm,Power_dB\n1550.0,0.5";
        var spectrum = MeasuredSpectrumCsvReader.Parse(csv, PowerUnit.Linear);
        spectrum.WavelengthNm.ShouldBe(new[] { 1550.0 });
    }

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData("\t")]
    public void Parse_AllSeparators_ParsesBothColumns(string separator)
    {
        var csv = $"1550.0{separator}0.5\n1551.0{separator}0.6";
        var spectrum = MeasuredSpectrumCsvReader.Parse(csv, PowerUnit.Linear);
        spectrum.WavelengthNm.Count.ShouldBe(2);
        spectrum.PowerLinear[0].ShouldBe(0.5);
    }

    [Fact]
    public void Parse_UnderGermanCulture_StillParsesInvariantDecimalPoint()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var csv = "1550.5,0.5";
            var spectrum = MeasuredSpectrumCsvReader.Parse(csv, PowerUnit.Linear);
            spectrum.WavelengthNm[0].ShouldBe(1550.5);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void Parse_MalformedRow_ThrowsWithLineNumber()
    {
        var csv = "1550.0,0.5\nbroken,row\n1551.0,0.6";
        var ex = Should.Throw<FormatException>(() => MeasuredSpectrumCsvReader.Parse(csv, PowerUnit.Linear));
        ex.Message.ShouldContain("line 2");
    }

    [Fact]
    public void Parse_SingleColumnRow_ThrowsWithLineNumber()
    {
        var csv = "1550.0,0.5\n1551.0";
        var ex = Should.Throw<FormatException>(() => MeasuredSpectrumCsvReader.Parse(csv, PowerUnit.Linear));
        ex.Message.ShouldContain("line 2");
    }

    [Fact]
    public void ReadFile_SyntheticFixture_Parses400Points()
    {
        // Fixture is a synthetic cos² MZI spectrum (ΔL=100 µm, n_g=4.2) — no lab data.
        string path = Path.Combine(AppContext.BaseDirectory,
            "Analysis", "MeasuredSpectrum", "Fixtures", "synthetic_mzi_spectrum.csv");
        var spectrum = MeasuredSpectrumCsvReader.ReadFile(path, PowerUnit.Linear);
        spectrum.WavelengthNm.Count.ShouldBe(400);
        spectrum.WavelengthNm[0].ShouldBe(1500.0, 1e-3);
    }
}
