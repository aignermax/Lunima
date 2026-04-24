using System.IO;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.AI;
using Moq;
using Shouldly;

namespace UnitTests.ViewModels;

/// <summary>
/// Covers the <see cref="AiAssistantViewModel.IsApiKeySet"/> flag that the
/// right-panel AI shortcut binds against — when no key is configured, a
/// "⚙ Set API key in Settings" button is shown instead of the chat input.
/// </summary>
public class AiAssistantApiKeyTests
{
    private static AiAssistantViewModel BuildViewModel()
    {
        // Isolated temp file — the auto-persist OnApiKeyChanged would
        // otherwise overwrite the developer's real Claude API key on every
        // test run (vm.ApiKey = "" as a baseline triggers persistence too).
        var tempFile = Path.Combine(Path.GetTempPath(), $"cap-test-prefs-{Guid.NewGuid()}.json");
        var vm = new AiAssistantViewModel(Mock.Of<IAiService>(), new UserPreferencesService(tempFile));
        vm.ApiKey = string.Empty; // baseline
        return vm;
    }

    [Fact]
    public void IsApiKeySet_IsFalse_WhenApiKeyIsEmpty()
    {
        var vm = BuildViewModel();

        vm.IsApiKeySet.ShouldBeFalse();
    }

    [Fact]
    public void IsApiKeySet_IsFalse_WhenApiKeyIsWhitespaceOnly()
    {
        // Whitespace must count as "not set" — otherwise the shortcut button
        // silently hides after the user accidentally pastes a space.
        var vm = BuildViewModel();

        vm.ApiKey = "   ";

        vm.IsApiKeySet.ShouldBeFalse();
    }

    [Fact]
    public void IsApiKeySet_IsTrue_WhenApiKeyIsNonEmpty()
    {
        var vm = BuildViewModel();

        vm.ApiKey = "sk-ant-test";

        vm.IsApiKeySet.ShouldBeTrue();
    }

    [Fact]
    public void IsApiKeySet_FiresPropertyChanged_WhenApiKeyChanges()
    {
        // The right-panel button's IsVisible binds to IsApiKeySet; without
        // the change notification the UI would not update when a key is
        // saved from the Settings dialog.
        var vm = BuildViewModel();
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? "");

        vm.ApiKey = "sk-ant-new";

        changes.ShouldContain(nameof(AiAssistantViewModel.ApiKey));
        changes.ShouldContain(nameof(AiAssistantViewModel.IsApiKeySet));
    }
}
