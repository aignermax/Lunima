using CAP.Avalonia.ViewModels.Settings;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

/// <summary>
/// Covers the Settings-window navigation commands on <see cref="CAP.Avalonia.ViewModels.MainViewModel"/>.
/// <see cref="CAP.Avalonia.ViewModels.MainViewModel.OpenAiSettingsCommand"/> is a deep-link shortcut used
/// by the right-panel AI placeholder — a regression that drops the page-type
/// argument would silently fall back to "open Settings on whatever page
/// happens to be first" with no failing test, so it is pinned explicitly.
/// </summary>
public class MainViewModelSettingsNavigationTests
{
    [Fact]
    public void OpenSettingsWindowCommand_PassesNullPageType()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        Type? captured = typeof(object); // sentinel value we know the code overwrites
        vm.ShowSettingsWindowAsync = t =>
        {
            captured = t;
            return Task.CompletedTask;
        };

        vm.OpenSettingsWindowCommand.Execute(null);

        captured.ShouldBeNull();
    }

    [Fact]
    public void OpenAiSettingsCommand_PassesAiAssistantSettingsPageType()
    {
        // If this argument drops to null the right-panel "⚙ Set API key in
        // Settings" shortcut silently degrades to "open Settings" without
        // selecting the AI page — a regression that would only surface as a
        // user complaint. Pin the deep-link argument explicitly.
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        Type? captured = null;
        vm.ShowSettingsWindowAsync = t =>
        {
            captured = t;
            return Task.CompletedTask;
        };

        vm.OpenAiSettingsCommand.Execute(null);

        captured.ShouldBe(typeof(AiAssistantSettingsPage));
    }
}
