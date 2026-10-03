using System.Globalization;
using System.Numerics;
using CAP_Core.Analysis.OnaAnalysis;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.OnaAnalysis;

public class WavelengthSweepResultTests
{
    private static WavelengthSweepResult CreateResult(double[] wavelengths)
    {
        var pin = Guid.NewGuid();
        var dataPoints = wavelengths
            .Select(wl => new WavelengthDataPoint(
                wl, new Dictionary<Guid, Complex> { { pin, new Complex(0.5, 0) } }, 1.0))
            .ToList();
        var config = new WavelengthSweepConfiguration(
            (int)wavelengths[0], (int)Math.Ceiling(wavelengths[^1]), wavelengths.Length);
        return new WavelengthSweepResult(config, dataPoints, new List<Guid> { pin });
    }

    [Fact]
    public void GetWavelengthValues_KeepsSubNmResolution()
    {
        var result = CreateResult(new[] { 1550.0, 1550.25, 1550.5 });

        result.GetWavelengthValues().ShouldBe(new[] { 1550.0, 1550.25, 1550.5 });
    }

    [Fact]
    public void GenerateCsvContent_SubNmWavelengths_AreWrittenWithInvariantCulture()
    {
        var result = CreateResult(new[] { 1550.0, 1550.25, 1550.5 });

        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var csv = result.GenerateCsvContent();
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            lines[1].ShouldStartWith("1550,");
            // sub-nm wavelengths must use a dot decimal separator, not the de-DE comma
            lines[2].ShouldStartWith("1550.25,");
            lines[3].ShouldStartWith("1550.5,");
            csv.ShouldNotContain("1550,25");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void GenerateCsvContent_AllIntegerSweep_KeepsLegacyFormat()
    {
        var result = CreateResult(new[] { 1500.0, 1550.0, 1600.0 });

        var csv = result.GenerateCsvContent();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Integral wavelengths print without a decimal point, so an all-integer
        // request produces byte-identical CSV to the old integer grid.
        lines[1].ShouldStartWith("1500,");
        lines[2].ShouldStartWith("1550,");
        lines[3].ShouldStartWith("1600,");
    }
}
