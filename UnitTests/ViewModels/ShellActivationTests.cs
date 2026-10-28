using CAP_Core.Export.Contracts;
using CAP_DataAccess.Updates;
using System.Net;
using System.Net.Http;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Update;
using CAP_Core;
using CAP_Core.Export;
using Moq;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class ShellActivationTests
{
    [Fact]
    public async Task RetirementDuringDelay_PreventsNetworkAndRejectsReactivation()
    {
        using var handler = new BlockingHandler();
        using var http = new HttpClient(handler);
        var delayStarted = Signal();
        var delayRelease = Signal();
        var gds = new EmptyDiscovery();
        await using var activation = Create(http, gds, async (_, token) =>
        {
            delayStarted.TrySetResult();
            await delayRelease.Task.WaitAsync(token);
        });
        var pending = activation.ActivateAsync();
        await delayStarted.Task;

        await activation.StopAsync();
        delayRelease.TrySetResult();
        await pending;
        await activation.ActivateAsync();

        handler.Calls.ShouldBe(0);
        gds.Searches.ShouldBe(1);
    }

    [Fact]
    public async Task RetirementCancelsAndDrainsTheActualHttpOperation()
    {
        using var handler = new BlockingHandler();
        using var http = new HttpClient(handler);
        await using var activation = Create(http, new EmptyDiscovery(), (_, _) => Task.CompletedTask);
        var pending = activation.ActivateAsync();
        await handler.Started.Task;

        var stopping = activation.StopAsync();
        try
        {
            await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping.IsCompleted.ShouldBeFalse("HTTP cleanup has not been released");
        }
        finally { handler.Release.TrySetResult(); }
        await stopping;
        await pending;
    }

    [Fact]
    public async Task RepeatedActivation_JoinsOneTrackedJob()
    {
        using var handler = new BlockingHandler();
        using var http = new HttpClient(handler);
        var gds = new EmptyDiscovery();
        await using var activation = Create(http, gds, (_, _) => Task.CompletedTask);
        var first = activation.ActivateAsync();
        var second = activation.ActivateAsync();
        try
        {
            first.ShouldBeSameAs(second);
            await handler.Started.Task;
            handler.Calls.ShouldBe(1);
            gds.Searches.ShouldBe(1);
        }
        finally { handler.Release.TrySetResult(); }
        await first;
    }

    private static ShellActivationCoordinator Create(HttpClient http, GdsExportViewModel gds,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        var preferences = new UserPreferencesService(Path.Combine(Path.GetTempPath(), "activation-" + Guid.NewGuid() + ".json"));
        var updates = new UpdateViewModel(new UpdateChecker(http, "test", "local-handler"),
            new UpdateDownloader(http), preferences, Mock.Of<IUrlLauncher>(), Mock.Of<IInstaller>());
        return new ShellActivationCoordinator(updates, gds, null, null, new ErrorConsoleService(), delay);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class EmptyDiscovery() : GdsExportViewModel(new GdsExportService(), new PythonDiscoveryService())
    {
        public int Searches;
        protected override Task<List<PythonInstallation>> DiscoverPythonsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.CanBeCanceled.ShouldBeTrue();
            Searches++;
            return Task.FromResult(new List<PythonInstallation>());
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource Cancelled { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Started.TrySetResult();
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
