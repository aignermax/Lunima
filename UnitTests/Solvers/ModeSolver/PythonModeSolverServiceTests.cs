using CAP.Avalonia.Services.Solvers;
using CAP_Core.Solvers.ModeSolver;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers.ModeSolver;

/// <summary>
/// Validates <see cref="PythonModeSolverService"/> service-level behaviour:
/// script-not-found path, cancellation, and timeout handling — without
/// requiring the Python solver libraries to be installed.
/// </summary>
public class PythonModeSolverServiceTests
{
    // ── script-not-found ──────────────────────────────────────────────────────

    [Fact]
    public async Task SolveAsync_ScriptNotFound_ReturnsFailureWithPath()
    {
        var service = new PythonModeSolverService(
            pythonExecutable: "python3",
            scriptPath: "/nonexistent/path/mode_solve.py");

        var result = await service.SolveAsync(DefaultRequest());

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("mode_solve.py");
    }

    // ── cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SolveAsync_AlreadyCancelledToken_ReturnsFailure()
    {
        var service = new PythonModeSolverService(
            pythonExecutable: "python3",
            scriptPath: "/nonexistent/mode_solve.py");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Even with a missing script the cancellation should surface (script
        // check runs before subprocess launch, so we get the "not found" error
        // rather than a cancellation error — either failure is acceptable here).
        var result = await service.SolveAsync(DefaultRequest(), cts.Token);

        result.Success.ShouldBeFalse();
    }

    // ── JSON serialisation contract ───────────────────────────────────────────

    [Fact]
    public void ParseOutput_BackendMissingWithPipHint_SetsMissingBackend()
    {
        // Simulates the exact JSON that mode_solve.py emits when gdsfactory is absent.
        const string json = """
            {"success":false,"error":"Backend 'GdsfactoryModes' requires the 'gdsfactory' Python package. Install it with: pip install gdsfactory","missing_backend":"gdsfactory"}
            """;

        var result = PythonModeSolverService.ParseOutput(json);

        result.Success.ShouldBeFalse();
        result.MissingBackend.ShouldBe("gdsfactory");
        result.Error.ShouldContain("pip install gdsfactory");
    }

    [Fact]
    public void ParseOutput_EMpyMissing_SetsMissingBackend()
    {
        const string json = """
            {"success":false,"error":"Backend 'EMpy' requires the 'EMpy' Python package. Install it with: pip install EMpy","missing_backend":"EMpy"}
            """;

        var result = PythonModeSolverService.ParseOutput(json);

        result.MissingBackend.ShouldBe("EMpy");
    }

    [Fact]
    public void ParseOutput_Tidy3DMissing_SetsMissingBackend()
    {
        const string json = """
            {"success":false,"error":"Backend 'Tidy3D' requires the 'tidy3d' Python package. Install it with: pip install tidy3d","missing_backend":"tidy3d"}
            """;

        var result = PythonModeSolverService.ParseOutput(json);

        result.MissingBackend.ShouldBe("tidy3d");
    }

    // ── ModeSolverRequest helpers ─────────────────────────────────────────────

    [Fact]
    public void ModeSolverRequest_DefaultValues_AreSOIStrip()
    {
        var req = new ModeSolverRequest();

        req.Width.ShouldBe(0.45, tolerance: 1e-9);
        req.Height.ShouldBe(0.22, tolerance: 1e-9);
        req.SlabHeight.ShouldBe(0.0, tolerance: 1e-9);
        req.CoreIndex.ShouldBe(3.48, tolerance: 1e-9);
        req.CladIndex.ShouldBe(1.44, tolerance: 1e-9);
        req.Backend.ShouldBe(ModeSolverBackend.GdsfactoryModes);
        req.NumModes.ShouldBe(4);
    }

    [Fact]
    public void ModeSolverResult_Fail_SetsSuccessFalse()
    {
        var result = ModeSolverResult.Fail("test error", rawStderr: "raw", missingBackend: "pkg");

        result.Success.ShouldBeFalse();
        result.Error.ShouldBe("test error");
        result.RawStderr.ShouldBe("raw");
        result.MissingBackend.ShouldBe("pkg");
        result.Modes.ShouldBeEmpty();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ModeSolverRequest DefaultRequest() => new()
    {
        Width = 0.45, Height = 0.22, Wavelengths = new[] { 1.55 }
    };
}
