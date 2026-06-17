using System.Text.Json;
using CAP.Avalonia.Services.Solvers;
using CAP_Core.Solvers.Fdtd;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers.Fdtd;

/// <summary>
/// Verifies <see cref="FdtdJsonContract"/> round-trips the JSON contract of
/// <c>scripts/fdtd_sparams.py</c> without spawning Docker or Meep.
/// </summary>
public class FdtdSMatrixContractTests
{
    private const string SuccessJson =
        """
        {"success":true,"is_3d":false,"resolution":20,"ports":["o1","o2"],"wavelengths":[1.5,1.55,1.6],"s":{"o2@0,o1@0":[[0.1,0.2],[0.3,0.4],[0.5,-0.6]],"o1@0,o1@0":[[0.0,0.0],[0.01,0.0],[0.0,0.0]]},"energy_sum_per_input":{"o1@0":0.98}}
        """;

    [Fact]
    public void ParseOutput_SuccessJson_ReturnsEntriesAndWavelengths()
    {
        var result = FdtdJsonContract.ParseOutput(SuccessJson);

        result.Success.ShouldBeTrue();
        result.Is3D.ShouldBeFalse();
        result.Wavelengths.Count.ShouldBe(3);
        result.Entries.Count.ShouldBe(2);

        var t = result.Entries.First(e => e.Key == "o2@0,o1@0");
        t.Values.Count.ShouldBe(3);
        t.Values[0].Real.ShouldBe(0.1, 1e-9);
        t.Values[2].Imaginary.ShouldBe(-0.6, 1e-9);
        result.EnergySumPerInput["o1@0"].ShouldBe(0.98, 1e-9);
    }

    [Fact]
    public void ParseOutput_LeadingChatter_UsesTrailingJsonLine()
    {
        var stdout = "INFO: loading meep...\nWARNING: no GPU\n" + SuccessJson;

        var result = FdtdJsonContract.ParseOutput(stdout);

        result.Success.ShouldBeTrue();
        result.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public void ParseOutput_FailureJson_SurfacesErrorAndMissingDependency()
    {
        const string json =
            """{"success":false,"error":"FDTD backend missing","missing_backend":"gdsfactory"}""";

        var result = FdtdJsonContract.ParseOutput(json, stderr: "ModuleNotFoundError: gdsfactory");

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("backend missing");
        result.MissingDependency.ShouldBe("gdsfactory");
        result.RawStderr.ShouldContain("ModuleNotFoundError");
    }

    [Fact]
    public void ParseOutput_EmptyStdout_ReturnsFailure()
    {
        var result = FdtdJsonContract.ParseOutput("");

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParseOutput_NoJsonInOutput_ReturnsFailure()
    {
        var result = FdtdJsonContract.ParseOutput("Traceback (most recent call last):\nSyntaxError\n");

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public void SerialiseRequest_IncludesPortsLayerAndSettings()
    {
        var request = new FdtdSMatrixRequest
        {
            GdsPath = @"C:\tmp\comp.gds",
            Ports = new[]
            {
                new FdtdPort { Name = "o1", X = 0, Y = 0, Orientation = 180, Width = 0.5 },
                new FdtdPort { Name = "o2", X = 12, Y = 0, Orientation = 0, Width = 0.5 },
            },
            LayerNumber = 1,
            LayerDatatype = 0,
            WavelengthPoints = 7,
            Is3D = true,
        };

        var json = FdtdJsonContract.SerialiseRequest(request, "/data/comp.gds");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("gds_path").GetString().ShouldBe("/data/comp.gds");
        root.GetProperty("is_3d").GetBoolean().ShouldBeTrue();
        root.GetProperty("wavelength_points").GetInt32().ShouldBe(7);
        root.GetProperty("ports").GetArrayLength().ShouldBe(2);
        root.GetProperty("ports")[0].GetProperty("name").GetString().ShouldBe("o1");
        root.GetProperty("ports")[1].GetProperty("orientation").GetDouble().ShouldBe(0);
        root.GetProperty("layer")[0].GetInt32().ShouldBe(1);
    }
}
