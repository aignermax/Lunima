using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class PreviewCacheCapabilityTests
{
    [Fact]
    public async Task InterpreterRetry_InvalidatesTheConfiguredPersistentCache()
    {
        var cache = new Mock<IGdsPreviewCache>();
        cache.SetupGet(value => value.ActivationIdentity).Returns("configured activation");
        var services = new ServiceCollection();
        CAP.Avalonia.App.ConfigureServices(services);
        services.AddSingleton(cache.Object);
        await using var provider = services.BuildServiceProvider();

        var previews = provider.GetRequiredService<GdsPreviewRenderService>();
        previews.RetryFailedPreviews();

        cache.Verify(value => value.InvalidateEnvironment(), Times.Once);
    }
}
