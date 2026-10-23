using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.DI;
using CAP.Avalonia.Services;
using CAP_Core.Export;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class PreviewEnvironmentRecoveryTests
{
    [Fact]
    public async Task DiskCacheHits_DoNotRetainCompletedFetchTasksAcrossDesigns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "preview-pending-" + Guid.NewGuid().ToString("N"));
        var disk = new GdsPreviewDiskCache(folder);
        var previews = new GdsPreviewRenderService(new NazcaComponentPreviewService("python", "unused"), disk);
        try
        {
            for (var design = 0; design < 100; design++)
            {
                var key = new GdsPreviewKey("test", "design_" + design, null);
                disk.Write(key, Geometry());
                previews.TryGetGeometry(key);
            }
            await previews.WaitForPendingAsync();
            // Inspect retained work, not timing: completed disk reads must leave no live slots.
            var pending = (System.Collections.IDictionary)typeof(GdsPreviewRenderService)
                .GetField("_pending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(previews)!;
            pending.Count.ShouldBe(0);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterpreterActivation_RetriesFailedGeometryWithoutRestart(bool switchWhilePending)
    {
        var folder = Path.Combine(Path.GetTempPath(), "preview-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var prefs = new UserPreferencesService(Path.Combine(folder, "prefs.json"));
            var completion = new TaskCompletionSource<NazcaPreviewResult>();
            var backend = new Mock<NazcaComponentPreviewService>("python", "script", null, null);
            var ready = false;
            backend.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(() => ready ? Task.FromResult(Geometry()) : completion.Task);
            var services = new ServiceCollection();
            services.AddSingleton(prefs);
            services.AddCanvasAndPanels();
            services.AddSingleton(backend.Object);
            services.AddSingleton(new GdsFactoryComponentPreviewService("python", "script"));
            await using var provider = services.BuildServiceProvider();
            var previews = provider.GetRequiredService<GdsPreviewRenderService>();
            previews.InlineNazcaCodeLookup = null;
            var key = new GdsPreviewKey("test", "unique_" + Guid.NewGuid().ToString("N"), null);
            previews.TryGetGeometry(key).ShouldBeNull();
            if (!switchWhilePending)
            {
                completion.SetResult(NazcaPreviewResult.Fail("Python not ready"));
                await previews.WaitForPendingAsync();
            }
            int repaints = 0;
            previews.OnPreviewLoaded += () => repaints++;
            ready = true;
            prefs.SetCustomPythonPath(Path.Combine(folder, "new-python"));
            if (switchWhilePending)
            {
                completion.SetResult(NazcaPreviewResult.Fail("Old Python failed late"));
                await previews.WaitForPendingAsync();
            }
            Dispatcher.UIThread.RunJobs();
            repaints.ShouldBeGreaterThan(0);
            previews.TryGetGeometry(key);
            await previews.WaitForPendingAsync();
            previews.TryGetGeometry(key).ShouldNotBeNull();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task PreviewBackends_UseNewPreferredInterpreterWithoutReconstruction()
    {
        var folder = Path.Combine(Path.GetTempPath(), "preview-python-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var first = Path.Combine(folder, "first-invalid.exe");
            var second = Path.Combine(folder, "second-invalid.exe");
            File.WriteAllText(first, "not executable");
            File.WriteAllText(second, "not executable");
            var prefs = new UserPreferencesService(Path.Combine(folder, "prefs.json"));
            prefs.SetCustomPythonPath(first);
            var services = new ServiceCollection();
            services.AddSingleton(prefs);
            services.AddSingleton(ProcessLaunchFactory.CreateDefault());
            services.AddPdkOffsetFeature();
            using var provider = services.BuildServiceProvider();
            var backends = new NazcaComponentPreviewService[] { provider.GetRequiredService<NazcaComponentPreviewService>(),
                provider.GetRequiredService<GdsFactoryComponentPreviewService>() };
            foreach (var backend in backends)
                (await backend.RenderRawCodeAsync("pass")).Error.ShouldContain(first);
            prefs.SetCustomPythonPath(second);
            foreach (var backend in backends)
                (await backend.RenderRawCodeAsync("pass")).Error.ShouldContain(second);
            prefs.SetCustomPythonPath(null);
            foreach (var backend in backends)
                backend.PythonExecutableResolver!().ShouldNotBe(first);
            var repaired = Path.Combine(folder, "repaired-python.exe");
            prefs.SetCustomPythonPath(repaired);
            backends[0].PythonExecutableResolver!().ShouldNotBe(repaired);
            File.WriteAllText(repaired, "not executable");
            prefs.SetCustomPythonPath(repaired);
            backends[0].PythonExecutableResolver!().ShouldBe(repaired);
        }
        finally { Directory.Delete(folder, true); }
    }

    private static NazcaPreviewResult Geometry() => new()
    {
        Success = true, XMax = 4, YMax = 2,
        Polygons = [new() { Layer = 1, Vertices = [(0, 0), (4, 0), (4, 2)] }]
    };
}
