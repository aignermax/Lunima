using CAP_DataAccess.Updates;
using System.Net;
using System.Net.Http;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.ViewModels.Update;
using Moq;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class MainStartupLifetimeTests
{
    [Fact]
    public async Task ConstructingMain_DoesNotStartNetworkWork()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var preferences = Preferences();
        using var main = MainViewModelTestHelper.CreateMainViewModel(preferencesService: preferences,
            updateViewModel: CreateUpdate(http, preferences));

        // The old constructor starts a 2s delayed network job. Cross that boundary without
        // activating the shell; this exercises the real constructor rather than a fake owner.
        await Task.WhenAny(handler.Started.Task, Task.Delay(TimeSpan.FromSeconds(2.6)));

        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task DisposingMainBeforeStartupDelay_DoesNotStartNetworkWork()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var preferences = Preferences();
        var main = MainViewModelTestHelper.CreateMainViewModel(preferencesService: preferences,
            updateViewModel: CreateUpdate(http, preferences));

        main.Dispose();
        await Task.WhenAny(handler.Started.Task, Task.Delay(TimeSpan.FromSeconds(2.6)));

        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task StartupUpdateCancellation_ReachesTheActiveHttpRequest()
    {
        using var handler = new RecordingHandler(block: true);
        using var http = new HttpClient(handler);
        using var stop = new CancellationTokenSource();
        var update = CreateUpdate(http, Preferences());
        var pending = update.CheckForUpdatesOnStartupAsync(stop.Token);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel();
            await Task.WhenAny(handler.Cancelled.Task, Task.Delay(TimeSpan.FromMilliseconds(250)));
            handler.Cancelled.Task.IsCompleted.ShouldBeTrue("retirement must cancel the actual request");
        }
        finally
        {
            handler.Release.TrySetResult();
            try { await pending; } catch (OperationCanceledException) { }
        }
        update.UpdateAvailable.ShouldBeFalse();
        update.IsChecking.ShouldBeFalse();
    }

    private static UserPreferencesService Preferences() =>
        new(Path.Combine(Path.GetTempPath(), "startup-lifetime-" + Guid.NewGuid() + ".json"));

    private static UpdateViewModel CreateUpdate(HttpClient http, UserPreferencesService preferences) => new(
        new UpdateChecker(http, "test", "local-handler"), new UpdateDownloader(http), preferences,
        Mock.Of<IUrlLauncher>(), Mock.Of<IInstaller>());

    private sealed class RecordingHandler(bool block = false) : HttpMessageHandler
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Started.TrySetResult();
            if (block) await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
