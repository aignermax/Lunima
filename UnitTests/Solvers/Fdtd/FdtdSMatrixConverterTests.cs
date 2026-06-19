using System.Numerics;
using CAP.Avalonia.Services.Solvers;
using CAP_Core.Solvers.Fdtd;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers.Fdtd;

/// <summary>
/// Verifies <see cref="FdtdSMatrixConverter"/> maps the solver's "out@m,in@m"
/// keyed result into the row-major per-wavelength matrices the dialog stores.
/// </summary>
public class FdtdSMatrixConverterTests
{
    [Fact]
    public void ToComponentSMatrixData_PlacesEntriesAtOutInIndicesPerWavelength()
    {
        var result = new FdtdSMatrixResult
        {
            Success = true,
            Ports = new[] { "o1", "o2" },
            Wavelengths = new[] { 1.55 },
            Entries = new[]
            {
                new FdtdSEntry { Key = "o2@0,o1@0", Values = new[] { new Complex(0.9, 0.1) } },
                new FdtdSEntry { Key = "o1@0,o1@0", Values = new[] { new Complex(0.02, -0.03) } },
            },
        };

        var data = FdtdSMatrixConverter.ToComponentSMatrixData(result, "FDTD Meep 2D");

        data.SourceNote.ShouldBe("FDTD Meep 2D");
        data.Wavelengths.ShouldContainKey("1550");

        var entry = data.Wavelengths["1550"];
        entry.Rows.ShouldBe(2);
        entry.Cols.ShouldBe(2);
        entry.PortNames.ShouldBe(new[] { "o1", "o2" });

        // "o2@0,o1@0" => out=o2 (row 1), in=o1 (col 0) => flat = 1*2 + 0 = 2
        entry.Real[2].ShouldBe(0.9, 1e-9);
        entry.Imag[2].ShouldBe(0.1, 1e-9);
        // "o1@0,o1@0" => row 0, col 0 => flat 0
        entry.Real[0].ShouldBe(0.02, 1e-9);
        entry.Imag[0].ShouldBe(-0.03, 1e-9);
        // Unset element (o1<-o2, flat 1) stays zero
        entry.Real[1].ShouldBe(0.0, 1e-9);
    }

    [Fact]
    public void ToComponentSMatrixData_RoundsWavelengthMicronsToNanometreKey()
    {
        var result = new FdtdSMatrixResult
        {
            Success = true,
            Ports = new[] { "o1" },
            Wavelengths = new[] { 1.31 },
            Entries = new[] { new FdtdSEntry { Key = "o1@0,o1@0", Values = new[] { Complex.One } } },
        };

        var data = FdtdSMatrixConverter.ToComponentSMatrixData(result, "x");

        data.Wavelengths.Keys.ShouldContain("1310");
    }
}
