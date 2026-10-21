using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using Shouldly;

namespace UnitTests.Canvas.ComponentPreview;

public class PreviewBitmapLodCacheTests
{
    [AvaloniaFact]
    public void Overview_UsesSmallerCachedImage_AndCloseZoomRestoresSource()
    {
        using var source = new RenderTargetBitmap(new PixelSize(1024, 256));
        var cache = new PreviewBitmapLodCache();
        var coarse = cache.Get(source, 100, 25, 0.1);
        coarse.PixelSize.Width.ShouldBeInRange(10, 20);
        coarse.PixelSize.Height.ShouldBeInRange(3, 5);
        cache.Get(source, 100, 25, 0.1).ShouldBeSameAs(coarse);
        cache.Get(source, 100, 25, 16).ShouldBeSameAs(source);
        source.PixelSize.Width.ShouldBe(1024);
    }
}
