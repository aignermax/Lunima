using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Home;
using Shouldly;
using Xunit;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Tests for the Home screen's "Connect two chiplets" entry point
/// (issue #1288): the fourth tour card delegates to the
/// <see cref="HomeViewModel.ConnectChipletsTourRequested"/> callback wired by
/// MainViewModel, which owns the Two-Chiplets-example load + tour start.
/// </summary>
public class HomeConnectChipletsTourTests : IDisposable
{
    private readonly string _testPreferencesPath;
    private readonly string _emptyExamplesBase;
    private readonly HomeViewModel _home;

    public HomeConnectChipletsTourTests()
    {
        _testPreferencesPath = Path.Combine(Path.GetTempPath(), $"test-chiplets-prefs-{Guid.NewGuid()}.json");
        _emptyExamplesBase = Path.Combine(Path.GetTempPath(), $"test-chiplets-noexamples-{Guid.NewGuid():N}");
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
    public async Task ConnectChipletsTourCommand_InvokesCallback()
    {
        var invoked = 0;
        _home.ConnectChipletsTourRequested = () =>
        {
            invoked++;
            return Task.CompletedTask;
        };

        await _home.ConnectChipletsTourCommand.ExecuteAsync(null);

        invoked.ShouldBe(1);
    }

    [Fact]
    public async Task ConnectChipletsTourCommand_WithoutCallback_DoesNotThrow()
    {
        await _home.ConnectChipletsTourCommand.ExecuteAsync(null);
    }
}
