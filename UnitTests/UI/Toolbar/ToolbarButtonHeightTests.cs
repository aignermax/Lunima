using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Shouldly;
using UnitTests.UI.Flows;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.UI.Toolbar;

/// <summary>
/// Every main-toolbar button has the same height: text buttons (Import, Export, Tools)
/// used to come out taller than the icon buttons next to them.
/// </summary>
[Collection("LocalizationSingleton")]
public sealed class ToolbarButtonHeightTests
{
    private const double HeightToleranceDip = 0.5;
    private readonly ITestOutputHelper _output;

    public ToolbarButtonHeightTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void AllToolbarButtons_HaveTheSameHeight()
    {
        using var host = new UiFlowTestHost();
        Dispatcher.UIThread.RunJobs();
        var toolbar = host.Window.FindControl<StackPanel>("MainToolbar").ShouldNotBeNull();

        var buttons = toolbar.Children.OfType<Button>().Where(b => b.IsVisible).ToList();
        foreach (var button in buttons)
            _output.WriteLine($"{ToolTip.GetTip(button) ?? button.Content}: {button.Bounds.Height:F1}");

        buttons.Count.ShouldBeGreaterThan(5);
        double reference = buttons[0].Bounds.Height;
        buttons.ShouldAllBe(b => Math.Abs(b.Bounds.Height - reference) <= HeightToleranceDip,
            "text buttons (Import/Export/Tools) must match the icon buttons");
        SaveShotIfRequested(host.Window);
    }

    /// <summary>Writes the main window to <c>UI_SHOT_DIR/toolbar</c> for PR media.</summary>
    private static void SaveShotIfRequested(Window window)
    {
        var root = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(root))
            return;
        var dir = Path.Combine(root, "toolbar");
        Directory.CreateDirectory(dir);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        ScreenshotArtifacts.SavePng(frame!, Path.Combine(dir, "main-window-toolbar.png"));
    }
}
