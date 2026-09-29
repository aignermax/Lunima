using System;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Panels;

/// <summary>
/// Status-bar shortcut hints (#1162): the bar shows only the hints relevant to the
/// current canvas mode — the old all-shortcuts string overflowed and slid under the
/// process badge; the full reference now lives in the (?) flyout next to the hints.
/// </summary>
[Collection("LocalizationSingleton")]
public class StatusBarShortcutHintsTests
{
    private const int MaxHintLength = 120;
    private static readonly string[] LocaleCodes = { "en", "de", "es", "ja", "zh-Hans" };

    [Fact]
    public void KeyForMode_EveryInteractionMode_HasAHintKey()
    {
        foreach (InteractionMode mode in Enum.GetValues<InteractionMode>())
        {
            StatusBarShortcutHints.KeyForMode(mode)
                .ShouldStartWith("StatusBar.Hints.");
        }
    }

    [Fact]
    public void KeyForMode_KeysExistInEveryShippedLocale()
    {
        foreach (InteractionMode mode in Enum.GetValues<InteractionMode>())
        {
            var key = StatusBarShortcutHints.KeyForMode(mode);
            foreach (var code in LocaleCodes)
            {
                LocalizationResourceLoader.Load(code).ContainsKey(key).ShouldBeTrue(
                    $"hint for mode {mode} ({key}) is missing in strings-{code}.json");
            }
        }
    }

    [Fact]
    public void ModeShortcutHints_InitializesToTheSelectModeHint()
    {
        var interaction = CreateInteraction();

        interaction.ModeShortcutHints.ShouldBe(
            LocalizationService.Instance.Translate("StatusBar.Hints.Select"));
    }

    [Theory]
    [InlineData(InteractionMode.Connect, "StatusBar.Hints.Connect")]
    [InlineData(InteractionMode.Delete, "StatusBar.Hints.Delete")]
    [InlineData(InteractionMode.Probe, "StatusBar.Hints.Probe")]
    [InlineData(InteractionMode.Cut, "StatusBar.Hints.Cut")]
    [InlineData(InteractionMode.PickAnalysisOutput, "StatusBar.Hints.PickOutput")]
    public void ModeShortcutHints_FollowsTheCurrentMode(InteractionMode mode, string expectedKey)
    {
        var interaction = CreateInteraction();

        interaction.CurrentMode = mode;

        interaction.ModeShortcutHints.ShouldBe(
            LocalizationService.Instance.Translate(expectedKey));
    }

    [Fact]
    public void ModeShortcutHints_StaysShortEnoughForTheStatusBar_InEveryMode()
    {
        var interaction = CreateInteraction();

        foreach (InteractionMode mode in Enum.GetValues<InteractionMode>())
        {
            interaction.CurrentMode = mode;
            interaction.ModeShortcutHints.Length.ShouldBeLessThan(MaxHintLength,
                $"the hint for {mode} is growing back toward the old all-shortcuts string (#1162)");
        }
    }

    [Fact]
    public void RefreshModeShortcutHints_AfterLanguageSwitch_ReReadsTheTranslation()
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage(SupportedLanguage.English.Code);
        var interaction = CreateInteraction();
        try
        {
            loc.SetLanguage("de");

            interaction.RefreshModeShortcutHints();

            interaction.ModeShortcutHints.ShouldContain("Drehen");
        }
        finally
        {
            loc.SetLanguage(SupportedLanguage.English.Code);
        }
    }

    private static CanvasInteractionViewModel CreateInteraction() =>
        new(new DesignCanvasViewModel(), new CommandManager());
}
