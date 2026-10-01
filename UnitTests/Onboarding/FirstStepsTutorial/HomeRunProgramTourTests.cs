using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Home;
using Shouldly;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Tests for the Home screen's "Run a program on your chip" entry point
/// (issue #1267): the third tour card delegates to the
/// <see cref="HomeViewModel.RunProgramTourRequested"/> callback wired by
/// MainViewModel, which owns the 4-bit-adder-example load + tour start.
/// </summary>
public class HomeRunProgramTourTests : IDisposable
{
    private readonly string _testPreferencesPath;
    private readonly string _emptyExamplesBase;
    private readonly HomeViewModel _home;

    public HomeRunProgramTourTests()
    {
        _testPreferencesPath = Path.Combine(Path.GetTempPath(), $"test-runprogram-prefs-{Guid.NewGuid()}.json");
        _emptyExamplesBase = Path.Combine(Path.GetTempPath(), $"test-runprogram-noexamples-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_emptyExamplesBase);
        _home = new HomeViewModel(
            new RecentProjectsService(new UserPreferencesService(_testPreferencesPath)),
            new UserPreferencesService(_testPreferencesPath),
            new ExampleDesignsService(_emptyExamplesBase));
    }

    public void Dispose()
    {
        if (File.Exists(_testPreferencesPath))
            File.Delete(_testPreferencesPath);
        if (Directory.Exists(_emptyExamplesBase))
            Directory.Delete(_emptyExamplesBase, recursive: true);
    }

    [Fact]
    public async Task RunProgramTourCommand_InvokesCallback()
    {
        var invoked = 0;
        _home.RunProgramTourRequested = () =>
        {
            invoked++;
            return Task.CompletedTask;
        };

        await _home.RunProgramTourCommand.ExecuteAsync(null);

        invoked.ShouldBe(1);
    }

    [Fact]
    public async Task RunProgramTourCommand_WithoutCallback_DoesNotThrow()
    {
        await _home.RunProgramTourCommand.ExecuteAsync(null);
    }
}
