using CAP.Avalonia.Services.Solvers;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers.ModeSolver;

/// <summary>
/// Validates that <see cref="PythonModeSolverService.ParseOutput"/> correctly
/// round-trips the JSON contract produced by <c>scripts/mode_solve.py</c>.
/// These tests exercise the JSON parsing path without spawning a real subprocess.
/// </summary>
public class ModeSolverJsonContractTests
{
    // ── success path ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseOutput_SuccessJson_ReturnsModes()
    {
        // mode_solve.py outputs a single-line JSON via json.dumps()
        const string json =
            """{"success":true,"backend_used":"GdsfactoryModes","modes":[{"wavelength":1.55,"mode_index":0,"n_eff":2.45,"n_g":4.2,"polarisation":"TE","mode_field_png":null},{"wavelength":1.55,"mode_index":1,"n_eff":1.87,"n_g":3.9,"polarisation":"TM","mode_field_png":null}]}""";

        var result = PythonModeSolverService.ParseOutput(json);

        result.Success.ShouldBeTrue();
        result.BackendUsed.ShouldBe("GdsfactoryModes");
        result.Modes.Count.ShouldBe(2);

        var m0 = result.Modes[0];
        m0.Wavelength.ShouldBe(1.55, tolerance: 1e-9);
        m0.ModeIndex.ShouldBe(0);
        m0.NEff.ShouldBe(2.45, tolerance: 1e-9);
        m0.NGroup.ShouldBe(4.2, tolerance: 1e-9);
        m0.Polarisation.ShouldBe("TE");
        m0.ModeFieldPng.ShouldBeNull();

        var m1 = result.Modes[1];
        m1.Polarisation.ShouldBe("TM");
        m1.NEff.ShouldBe(1.87, tolerance: 1e-9);
    }

    [Fact]
    public void ParseOutput_SuccessJson_LastLineIsUsedWhenLeadingChatIsPresent()
    {
        // Simulate library log chatter before the JSON (as Nazca/femwell can produce)
        const string stdout = """
            INFO: loading femwell...
            WARNING: no GPU found
            {"success":true,"backend_used":"EMpy","modes":[{"wavelength":1.31,"mode_index":0,"n_eff":2.10,"n_g":3.8,"polarisation":"TE","mode_field_png":null}]}
            """;

        var result = PythonModeSolverService.ParseOutput(stdout);

        result.Success.ShouldBeTrue();
        result.BackendUsed.ShouldBe("EMpy");
        result.Modes.Count.ShouldBe(1);
        result.Modes[0].Wavelength.ShouldBe(1.31, tolerance: 1e-9);
    }

    [Fact]
    public void ParseOutput_EmptyModesList_ReturnsSuccessWithZeroModes()
    {
        const string json = """{"success":true,"backend_used":"GdsfactoryModes","modes":[]}""";

        var result = PythonModeSolverService.ParseOutput(json);

        result.Success.ShouldBeTrue();
        result.Modes.ShouldBeEmpty();
    }

    // ── failure paths ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseOutput_BackendMissingJson_ReturnsMissingBackend()
    {
        const string json =
            """{"success":false,"error":"Backend 'GdsfactoryModes' requires the 'gdsfactory' Python package. Install it with: pip install gdsfactory","missing_backend":"gdsfactory"}""";

        var result = PythonModeSolverService.ParseOutput(json, stderr: "ModuleNotFoundError: No module named 'gdsfactory'");

        result.Success.ShouldBeFalse();
        result.MissingBackend.ShouldBe("gdsfactory");
        result.Error.ShouldContain("gdsfactory");
        result.RawStderr.ShouldContain("ModuleNotFoundError");
    }

    [Fact]
    public void ParseOutput_GenericErrorJson_SurfacesErrorMessage()
    {
        const string json = """{"success":false,"error":"Solve failed: singular matrix"}""";

        var result = PythonModeSolverService.ParseOutput(json, stderr: "Traceback (most recent call last):\n  ...");

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("singular matrix");
        result.MissingBackend.ShouldBeNull();
    }

    [Fact]
    public void ParseOutput_EmptyStdout_ReturnsFailure()
    {
        var result = PythonModeSolverService.ParseOutput("");

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParseOutput_NoJsonInOutput_ReturnsFailure()
    {
        const string stdout = "Traceback (most recent call last):\n  File \"mode_solve.py\", line 3\nSyntaxError: invalid syntax\n";

        var result = PythonModeSolverService.ParseOutput(stdout, stderr: "");

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParseOutput_UnknownBackendJson_ReturnsFailure()
    {
        const string json = """{"success":false,"error":"Unknown backend 'Bogus'. Known backends: GdsfactoryModes, EMpy, Tidy3D"}""";

        var result = PythonModeSolverService.ParseOutput(json);

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("Unknown backend");
    }

    // ── ModeFieldPng ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseOutput_ModeWithBase64Png_PopulatesModeFieldPng()
    {
        // Minimal 1x1 white PNG as base64 (valid PNG header)
        const string pngB64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI6QAAAABJRU5ErkJggg==";
        var json = $$"""{"success":true,"backend_used":"Tidy3D","modes":[{"wavelength":1.55,"mode_index":0,"n_eff":2.45,"n_g":4.2,"polarisation":"TE","mode_field_png":"{{pngB64}}"}]}""";

        var result = PythonModeSolverService.ParseOutput(json);

        result.Success.ShouldBeTrue();
        result.Modes[0].ModeFieldPng.ShouldBe(pngB64);
    }
}
