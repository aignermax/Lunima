using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace UnitTests.UI;

public class HeadlessDispatcherBootstrapTests
{
    private const string ProbeEnvironment = "LUNIMA_HEADLESS_DISPATCHER_PROBE";

    [Fact]
    public async Task SharedApplication_AfterOrdinaryFactTouchesDispatcher_SupportsAsyncDispatch()
        => await RunProbeAsync(false);

    [Fact]
    public async Task SharedApplication_WhenDispatcherIsTouchedDuringPlatformInitialization_SupportsAsyncDispatch()
        => await RunProbeAsync(true);

    private static async Task RunProbeAsync(bool duringInitialization)
    {
        if (Environment.GetEnvironmentVariable(ProbeEnvironment) == "1")
        {
            Assert.Null(Application.Current);
            var fallback = Dispatcher.UIThread; // Ordinary routing/view-model facts do this first.
            Assert.False(fallback.SupportsRunLoops);
            using var session = HeadlessUnitTestSession.StartNew(
                duringInitialization ? typeof(LateFallbackTestAppBuilder) : typeof(ScreenshotTestAppBuilder),
                AvaloniaTestIsolationLevel.PerAssembly);
            await session.Dispatch(async () =>
            {
                await Task.Yield(); // Requires a real nested dispatcher loop, like AvaloniaFact.
                Assert.True(Dispatcher.UIThread.SupportsRunLoops);
                Dispatcher.UIThread.VerifyAccess();
                var live = Dispatcher.UIThread;
                HeadlessDispatcherBootstrap.ResetPrePlatformFallback();
                Assert.Same(live, Dispatcher.UIThread);
                var button = new Button { Content = "Actual input" };
                var clicked = false;
                button.Click += (_, _) => clicked = true;
                var window = new Window { Width = 160, Height = 100, Content = button };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    window.MouseMove(new Point(80, 50));
                    Assert.NotNull(window.InputHitTest(new Point(80, 50)));
                    window.MouseDown(new Point(80, 50), MouseButton.Left);
                    window.MouseUp(new Point(80, 50), MouseButton.Left);
                    Assert.True(clicked, "the compositor and real input must use the active dispatcher");
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
            return;
        }

        // A fresh process makes the pre-platform access deterministic regardless
        // of which UI tests ran earlier in the parent suite. Reuse this built DLL.
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(HeadlessDispatcherBootstrapTests).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName=" +
            typeof(HeadlessDispatcherBootstrapTests).FullName + "." +
            (duringInitialization
                ? nameof(SharedApplication_WhenDispatcherIsTouchedDuringPlatformInitialization_SupportsAsyncDispatch)
                : nameof(SharedApplication_AfterOrdinaryFactTouchesDispatcher_SupportsAsyncDispatch)));
        start.Environment[ProbeEnvironment] = "1";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await stdout + Environment.NewLine + await stderr);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}

/// <summary>Reproduces a plain Fact timer touching UIThread in the platform setup gap.</summary>
public class LateFallbackTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<ScreenshotTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        var initialize = builder.WindowingSubsystemInitializer!;
        builder.UseWindowingSubsystem(() =>
        {
            Assert.Null(Application.Current);
            _ = Dispatcher.UIThread; // Simulates the competing lookup immediately before platform binding.
            initialize();
        }, builder.WindowingSubsystemName!);
        return builder.WithFallbackDispatcherReset();
    }
}
