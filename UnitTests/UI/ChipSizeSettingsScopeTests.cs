using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Settings;
using CAP.Avalonia.Views;
using Shouldly;
using UnitTests.Dies;

namespace UnitTests.UI;

/// <summary>Verifies the real chip settings controls follow the design's die scope.</summary>
[Collection("LocalizationSingleton")]
public class ChipSizeSettingsScopeTests
{
    [AvaloniaFact]
    public void ExplicitDie_DisablesEditorsAndApply_AndShowsScopeExplanation()
    {
        var localization = LocalizationService.Instance;
        var previousLanguage = localization.ActiveLanguageCode;
        var preferencesPath = Path.Combine(Path.GetTempPath(), $"chip-settings-{Guid.NewGuid()}.json");
        Window? window = null;
        localization.SetLanguage(SupportedLanguage.English.Code);
        try
        {
            var canvas = new DesignCanvasViewModel();
            var settings = new ChipSizeViewModel(new UserPreferencesService(preferencesPath), canvas);
            using var page = new ChipSizeSettingsPage(settings, localization);
            window = new SettingsWindow
            {
                DataContext = new SettingsWindowViewModel([page])
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var controls = window.GetVisualDescendants().ToArray();
            var editors = controls.OfType<NumericUpDown>().Cast<Control>()
                .Concat(controls.OfType<ComboBox>()).ToArray();
            editors.Length.ShouldBe(3);
            var apply = controls.OfType<Button>().Single(button => ReferenceEquals(button.Command, settings.ApplyCommand));
            var explanation = controls.OfType<TextBlock>().Single(text =>
                text.Text == localization.Translate("Settings.ChipSize.DieScopeDescription"));
            editors.ShouldAllBe(control => control.IsEffectivelyEnabled);
            apply.IsEffectivelyEnabled.ShouldBeTrue();
            explanation.IsEffectivelyVisible.ShouldBeFalse();

            var die = new ComponentViewModel(DieTestFixtures.Die("Logic die", 0, 0, 2000, 2000));
            canvas.Components.Add(die);
            Dispatcher.UIThread.RunJobs();

            editors.ShouldAllBe(control => !control.IsEffectivelyEnabled);
            apply.IsEffectivelyEnabled.ShouldBeFalse();
            explanation.IsEffectivelyVisible.ShouldBeTrue();
            explanation.Bounds.Height.ShouldBeGreaterThan(0);
            if (Environment.GetEnvironmentVariable("UI_SHOT_DIR") is { Length: > 0 } directory)
            {
                var outputDirectory = Path.Combine(directory, "chip-size-scope");
                Directory.CreateDirectory(outputDirectory);
                ScreenshotArtifacts.SettleAnimations();
                using var frame = window.CaptureRenderedFrame();
                frame.ShouldNotBeNull();
                ScreenshotArtifacts.SavePng(frame!, Path.Combine(outputDirectory, "explicit-die-settings.png"));
            }

            canvas.Components.Remove(die);
            Dispatcher.UIThread.RunJobs();
            editors.ShouldAllBe(control => control.IsEffectivelyEnabled);
            apply.IsEffectivelyEnabled.ShouldBeTrue();
            explanation.IsEffectivelyVisible.ShouldBeFalse();
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            localization.SetLanguage(previousLanguage);
            if (File.Exists(preferencesPath))
                File.Delete(preferencesPath);
        }
    }
}
