using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Home;
using Shouldly;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Tests for the Home screen's "Store a number in light" entry point
/// (issue #1422): the third tour card delegates to the
/// <see cref="HomeViewModel.StoreNumberTourRequested"/> callback wired by
/// MainViewModel, which owns the RAM 2x4-example load + tour start.
/// </summary>
public class HomeStoreNumberTourTests : IDisposable
{
    private readonly string _testPreferencesPath;
    private readonly string _emptyExamplesBase;
    private readonly HomeViewModel _home;

    public HomeStoreNumberTourTests()
    {
        _testPreferencesPath = Path.Combine(Path.GetTempPath(), $"test-storenumber-prefs-{Guid.NewGuid()}.json");
        _emptyExamplesBase = Path.Combine(Path.GetTempPath(), $"test-storenumber-noexamples-{Guid.NewGuid():N}");
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
    public async Task StoreNumberTourCommand_InvokesCallback()
    {
        var invoked = 0;
        _home.StoreNumberTourRequested = () =>
        {
            invoked++;
            return Task.CompletedTask;
        };

        await _home.StoreNumberTourCommand.ExecuteAsync(null);

        invoked.ShouldBe(1);
    }

    [Fact]
    public async Task StoreNumberTourCommand_WithoutCallback_DoesNotThrow()
    {
        await _home.StoreNumberTourCommand.ExecuteAsync(null);
    }
}
