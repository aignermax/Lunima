using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Settings;
using CAP_Core.Export;
using Moq;
using Shouldly;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class PreviewRecoveryHintTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task FailedPreview_ShowsSettingsAction_AndRecoveryClearsHint(string language)
    {
        var folder = Path.Combine(Path.GetTempPath(), "preview-hint-" + Guid.NewGuid().ToString("N"));
        var backend = new Mock<NazcaComponentPreviewService>("python", "script", null, null);
        backend.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NazcaPreviewResult.Fail("Selected Python cannot import nazca"));
        backend.Setup(s => s.RenderRawCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NazcaPreviewResult.Fail("Selected Python cannot import gdsfactory"));
        var previews = new GdsPreviewRenderService(backend.Object, new GdsPreviewDiskCache(folder), backend.Object);
        using var host = new UiFlowTestHost(previews);
        LocalizationService.Instance.SetLanguage(language);
        Type? openedPage = null;
        host.Vm.ShowSettingsWindowAsync = page => { openedPage = page; return Task.CompletedTask; };
        var key = new GdsPreviewKey("test", "unique_" + Guid.NewGuid().ToString("N"), null);
        previews.TryGetGeometry(key);
        await previews.WaitForPendingAsync();
        UiInput.RunJobs();
        var button = host.Window.FindControl<Button>("PreviewPythonSettingsButton");
        button.ShouldNotBeNull();
        button.IsEffectivelyVisible.ShouldBeTrue();
        if (Environment.GetEnvironmentVariable("UI_SHOT_DIR") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            ScreenshotArtifacts.SettleAnimations();
            using var frame = host.Window.CaptureRenderedFrame();
            ScreenshotArtifacts.SavePng(frame!, Path.Combine(shots, "preview-recovery-" + language + ".png"));
        }
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        UiInput.RunJobs();
        openedPage.ShouldBe(typeof(PythonEnvironmentsSettingsPage));
        backend.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NazcaPreviewResult { Success = true, Polygons = [new() { Vertices = [(0, 0), (4, 0), (4, 2)] }] });
        backend.Setup(s => s.RenderRawCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NazcaPreviewResult { Success = true, Polygons = [new() { Vertices = [(0, 0), (4, 0), (4, 2)] }] });
        previews.RetryFailedPreviews();
        previews.TryGetGeometry(key);
        await previews.WaitForPendingAsync();
        UiInput.RunJobs();
        previews.TryGetGeometry(key).ShouldNotBeNull();
        button.IsEffectivelyVisible.ShouldBeFalse();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
