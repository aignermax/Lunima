using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CAP.Avalonia.Controls;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The app icon and the wordmark: the window icon resource must ship inside CAP.Avalonia
/// (Linux and macOS title bars have no exe icon to fall back on) and the home-screen
/// wordmark must lay out at its requested height.
/// </summary>
public sealed class AppBrandingTests
{
    private static readonly Uri WindowIconUri = new("avares://CAP.Avalonia/Assets/LunimaIcon.png");

    [AvaloniaFact]
    public void WindowIconResource_IsBundledAndSquare()
    {
        using var stream = AssetLoader.Open(WindowIconUri);
        using var bitmap = new Bitmap(stream);
        bitmap.PixelSize.Width.ShouldBe(256);
        bitmap.PixelSize.Height.ShouldBe(256);
    }

    [AvaloniaFact]
    public void Wordmark_KeepsItsAspectRatioAtTheRequestedHeight()
    {
        var wordmark = new LunimaWordmark { Height = 34 };
        var window = new Window { Content = wordmark, Width = 400, Height = 100 };
        window.Show();

        wordmark.Bounds.Height.ShouldBe(34, 0.5);
        wordmark.Bounds.Width.ShouldBeGreaterThan(wordmark.Bounds.Height * 3, "the L mark plus 'unima' is wide");
        window.Close();
    }
}
