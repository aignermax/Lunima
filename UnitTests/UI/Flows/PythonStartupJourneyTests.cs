using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export.PythonEnvironmentManager;
using CAP_Core.Export;
using Moq;
using UnitTests.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class PythonStartupJourneyTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task FreshProfile_RemainsInteractive_AndActualRetryActivatesHealthyEnvironment(string language)
    {
        var folder = Path.Combine(Path.GetTempPath(), "python-journey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var prefs = new UserPreferencesService(Path.Combine(folder, "prefs.json"));
            var registry = new PythonEnvironmentRegistry(Path.Combine(folder, "envs.json"));
            registry.OnActiveEnvironmentChanged = prefs.SetCustomPythonPath;
            var installer = new FakePythonProvisioner();
            var startup = new PythonStartupViewModel(registry, prefs, installer, folder);
            var backend = new Mock<NazcaComponentPreviewService>("python", "script", null, null);
            backend.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => installer.Ready
                    ? new NazcaPreviewResult { Success = true, Polygons = [new() { Vertices = [(0, 0), (4, 0), (4, 2)] }] }
                    : NazcaPreviewResult.Fail("Python setup is pending"));
            var previews = new GdsPreviewRenderService(backend.Object, new GdsPreviewDiskCache(Path.Combine(folder, "previews")));
            prefs.PythonInterpreterActivated += previews.RetryFailedPreviews;
            using var host = new UiFlowTestHost(previews, startup, prefs);
            LocalizationService.Instance.SetLanguage(language);
            var attempt = startup.StartOnceAsync();
            UiInput.RunJobs();
            var progress = host.Window.FindControl<StackPanel>("PythonStartupProgress");
            progress.ShouldNotBeNull();
            progress.IsEffectivelyVisible.ShouldBeTrue();
            // Real text input remains usable while provisioning is pending.
            var search = UiInput.Descendants<TextBox>(host.Window).First(t => t.IsEffectivelyVisible);
            UiInput.Click(host.Window, search);
            UiInput.TypeText(host.Window, "waveguide");
            search.Text.ShouldContain("waveguide");
            attempt.IsCompleted.ShouldBeFalse();
            registry.GetActive().ShouldBeNull();
            var key = new GdsPreviewKey("test", "first_start_preview", null);
            previews.TryGetGeometry(key);
            await previews.WaitForPendingAsync();
            previews.TryGetGeometry(key).ShouldBeNull();
            UiInput.RunJobs();
            host.Window.FindControl<Button>("PreviewPythonSettingsButton")!.IsEffectivelyVisible.ShouldBeFalse();
            installer.Completion.SetException(new IOException("Offline — retry when connected"));
            var oldProgress = installer.Progress!;
            await attempt;
            UiInput.RunJobs();
            var retry = host.Window.FindControl<Button>("PythonStartupRetryButton");
            retry.ShouldNotBeNull();
            retry.IsEffectivelyVisible.ShouldBeTrue();
            if (Environment.GetEnvironmentVariable("UI_SHOT_DIR") is { Length: > 0 } shots)
            {
                Directory.CreateDirectory(shots);
                ScreenshotArtifacts.SettleAnimations();
                using var frame = host.Window.CaptureRenderedFrame();
                ScreenshotArtifacts.SavePng(frame!, Path.Combine(shots, "python-startup-" + language + ".png"));
            }
            installer.Completion = new();
            UiInput.Click(host.Window, retry);
            installer.Installs.ShouldBe(2);
            oldProgress.Report("Stale failed download");
            UiInput.RunJobs();
            startup.ProgressText.ShouldNotBe("Stale failed download");
            installer.Finish();
            await startup.RetryCommand.ExecutionTask!;
            UiInput.RunJobs();
            registry.GetActive().ShouldNotBeNull();
            prefs.GetCustomPythonPath().ShouldBe(registry.GetActive()!.PythonExecutable);
            retry.IsEffectivelyVisible.ShouldBeFalse();
            progress.IsEffectivelyVisible.ShouldBeFalse();
            search.Text.ShouldContain("waveguide");
            previews.TryGetGeometry(key);
            await previews.WaitForPendingAsync();
            previews.TryGetGeometry(key).ShouldNotBeNull();
        }
        finally { Directory.Delete(folder, true); }
    }
}
