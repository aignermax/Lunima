using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(UnitTests.UI.ScreenshotTestAppBuilder))]

namespace UnitTests.UI;

/// <summary>
/// Minimal Avalonia application for headless screenshot tests.
/// Loads only Fluent theme — intentionally avoids production App.cs DI setup
/// so tests control all ViewModel construction themselves.
/// </summary>
internal class ScreenshotTestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// Configures Avalonia with Skia rendering so that <c>CaptureRenderedFrame()</c> produces
/// real pixel data. UseHeadlessDrawing = false is required — true throws NotSupportedException.
/// Skia 11.3.13 is pinned in UnitTests.csproj to resolve the version conflict with
/// CAP.Avalonia's Avalonia.Skia 11.2.1 reference.
/// </summary>
public class ScreenshotTestAppBuilder
{
    /// <summary>Entry point called by <see cref="AvaloniaTestApplicationAttribute"/>.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<ScreenshotTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
