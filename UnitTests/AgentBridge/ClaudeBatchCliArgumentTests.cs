using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Registration;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.AgentBridge;

public sealed class ClaudeBatchCliArgumentTests
{
    private static string[] RegistrationArguments() =>
    [
        "mcp", "add-json", "--scope", "user", "lunima",
        JsonSerializer.Serialize(new
        {
            type = "http", url = "http://127.0.0.1:12345/mcp",
            headers = new Dictionary<string, string> { ["Authorization"] = "Bearer literal % ! ^ & | ( ) μ \\" }
        })
    ];

    [SkippableFact]
    public async Task NativeToolControl_RecordsExactJsonArgumentsAndConfiguredEnvironment()
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync();
        var arguments = RegistrationArguments();
        var result = await CliProcessRunner.RunAsync(fixture.Build(arguments, native: true), TimeSpan.FromSeconds(5), default)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.ExitCode.ShouldBe(7);
        await fixture.AssertCaptureAsync(arguments);
        fixture.Child.ShouldHaveExited();
    }

    [SkippableTheory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public async Task BatchForwarder_PreservesRegistrationJsonExitCodeAndBothStreams(string extension)
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync(
            "print('stdout value', flush=True)\nprint('stderr value', file=sys.stderr, flush=True)\nos._exit(7)", extension);
        var arguments = RegistrationArguments();
        var result = await CliProcessRunner.RunAsync(fixture.Build(arguments), TimeSpan.FromSeconds(5), default)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.ExitCode.ShouldBe(7, result.Output);
        result.Output.ShouldBe("stdout value" + Environment.NewLine + "stderr value");
        await fixture.AssertCaptureAsync(arguments);
        fixture.Child.ShouldHaveExited();
        await fixture.Child.DescendantsShouldHaveExitedAsync();
    }

    [SkippableFact]
    public async Task BatchForwarder_PreservesEveryArgumentBoundaryAndLiteralMetacharacter()
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync();
        string[] arguments = ["", "with spaces", "μ漢字", "quote\"inside", "trailing\\", "two\\\\",
            "space and trailing\\", "backslash\\\"quote", "%LUNIMA_EXPANSION%", "!LUNIMA_EXPANSION!",
            "^", "&", "|", "(left right)", "<input>output", "semi;comma,equals=", "[brackets]", "*?"];
        var result = await CliProcessRunner.RunAsync(fixture.Build(arguments), TimeSpan.FromSeconds(5), default)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.ExitCode.ShouldBe(7, result.Output);
        await fixture.AssertCaptureAsync(arguments);
    }

    [SkippableFact]
    public async Task InjectionShapedArguments_AreLiteralAndCannotCreateSentinel()
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync();
        string[] arguments =
        [
            $"\" & echo injected > \"{fixture.SentinelPath}\" & rem ",
            $"\" | echo injected > \"{fixture.SentinelPath}\" & rem ",
            "%LUNIMA_EXPANSION%", "!LUNIMA_EXPANSION!", "^^^&"
        ];
        var result = await CliProcessRunner.RunAsync(fixture.Build(arguments), TimeSpan.FromSeconds(5), default)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        File.Exists(fixture.SentinelPath).ShouldBeFalse("arguments must never become shell commands");
        result.ExitCode.ShouldBe(7, result.Output);
        await fixture.AssertCaptureAsync(arguments);
    }

    [SkippableFact]
    public async Task ShimPath_WithSpacesUnicodeAndMetacharacters_SelectsExactFile()
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync(specialPath: true);
        var arguments = RegistrationArguments();
        var result = await CliProcessRunner.RunAsync(fixture.Build(arguments), TimeSpan.FromSeconds(5), default)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.ExitCode.ShouldBe(7, result.Output);
        await fixture.AssertCaptureAsync(arguments);
    }

    [SkippableTheory]
    [InlineData("nul\0argument")]
    [InlineData("line\rargument")]
    [InlineData("line\nargument")]
    public async Task UnsupportedBatchControlCharacters_AreRejectedClearlyBeforeLaunch(string argument)
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync();
        var result = await CliProcessRunner.RunAsync(fixture.Build([argument]), TimeSpan.FromSeconds(5), default)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.ExitCode.ShouldBe(CliProcessRunner.StartFailedExitCode);
        result.Output.ShouldContain("Batch arguments cannot contain NUL, CR, or LF.");
        File.Exists(fixture.CapturePath).ShouldBeFalse();
    }

    [SkippableFact]
    public async Task PreCanceledBatch_RejectsAdmissionWithoutStartingTheShim()
    {
        await using var fixture = await ClaudeBatchCliFixture.CreateAsync();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            CliProcessRunner.RunAsync(fixture.Build(RegistrationArguments()), TimeSpan.FromSeconds(5), stop.Token));
        File.Exists(fixture.CapturePath).ShouldBeFalse();
    }
}
