using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Home;
using CommunityToolkit.Mvvm.Input;
using Shouldly;
using Xunit;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Tests for the Home screen's "Learn Lunima" list (issue #1301): the five
/// guided tours appear as one numbered, data-driven list in learning order,
/// and each entry carries the existing tour command instance — clicking a row
/// starts exactly the tour the previous standalone button started.
/// </summary>
public class HomeLearnTourListTests : IDisposable
{
    private readonly string _testPreferencesPath;
    private readonly string _emptyExamplesBase;
    private readonly HomeViewModel _home;

    public HomeLearnTourListTests()
    {
        _testPreferencesPath = Path.Combine(Path.GetTempPath(), $"test-tourlist-prefs-{Guid.NewGuid():N}.json");
        _emptyExamplesBase = Path.Combine(Path.GetTempPath(), $"test-tourlist-noexamples-{Guid.NewGuid():N}");
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
    public void TourEntries_HasFiveTours_InLearningOrder()
    {
        _home.TourEntries.Count.ShouldBe(5);
        _home.TourEntries.Select(e => e.Number).ShouldBe(new[] { 1, 2, 3, 4, 5 });
        _home.TourEntries.Select(e => e.NameKey).ShouldBe(new[]
        {
            "Home.Learn.FirstSteps",
            "Home.WatchComputeTour",
            "Home.StoreNumberTour",
            "Home.RunProgramTour",
            "Home.ConnectChipletsTour",
        });
    }

    [Fact]
    public void TourEntries_CarriesTheExistingCommandInstances()
    {
        _home.TourEntries[0].Command.ShouldBeSameAs(_home.LearnTutorialCommand);
        _home.TourEntries[1].Command.ShouldBeSameAs(_home.WatchComputeTourCommand);
        _home.TourEntries[2].Command.ShouldBeSameAs(_home.StoreNumberTourCommand);
        _home.TourEntries[3].Command.ShouldBeSameAs(_home.RunProgramTourCommand);
        _home.TourEntries[4].Command.ShouldBeSameAs(_home.ConnectChipletsTourCommand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task TourEntryCommand_InvokesTheMatchingTourCallback(int entryIndex)
    {
        var invoked = new int[5];
        _home.LearnTutorialRequested = () => { invoked[0]++; return Task.CompletedTask; };
        _home.WatchComputeTourRequested = () => { invoked[1]++; return Task.CompletedTask; };
        _home.StoreNumberTourRequested = () => { invoked[2]++; return Task.CompletedTask; };
        _home.RunProgramTourRequested = () => { invoked[3]++; return Task.CompletedTask; };
        _home.ConnectChipletsTourRequested = () => { invoked[4]++; return Task.CompletedTask; };

        await ((IAsyncRelayCommand)_home.TourEntries[entryIndex].Command).ExecuteAsync(null);

        invoked[entryIndex].ShouldBe(1);
        invoked.Sum().ShouldBe(1, "a list row must start only its own tour");
    }

    [Fact]
    public void Show_RebuildsTourEntries_KeepingTheSameCommandInstances()
    {
        var commands = _home.TourEntries.Select(e => e.Command).ToArray();

        _home.Show();

        _home.TourEntries.Count.ShouldBe(5);
        _home.TourEntries.Select(e => e.Command).ShouldBe(commands);
    }
}
