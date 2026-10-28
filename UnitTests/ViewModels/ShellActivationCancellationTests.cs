using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Update;
using CAP_Core;
using CAP_Core.Export.Contracts;
using CAP_DataAccess.Updates;
using Moq;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class ShellActivationCancellationTests
{
    [AvaloniaFact]
    public async Task CallerCancellation_DrainsBackendCallbacksWithoutBlockingDispatcher()
    {
        using var caller = new CancellationTokenSource();
        using var releaseCallback = new ManualResetEventSlim();
        var entered = Signal();
        var checkedEnvironment = Signal();
        var environment = new TaskCompletionSource<PythonEnvironmentInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration callback = default;
        var backend = new Mock<IGdsExportSession>();
        backend.Setup(value => value.CheckPythonEnvironmentAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                callback = token.Register(() => { entered.TrySetResult(); releaseCallback.Wait(TimeSpan.FromSeconds(10)); });
                return environment.Task;
            });
        var gds = new GdsExportViewModel(backend.Object, Mock.Of<IPythonDiscovery>());
        gds.Initialize("controlled-python");
        gds.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(gds.IsChecking) && !gds.IsChecking) checkedEnvironment.TrySetResult();
        };
        using var http = new HttpClient();
        var preferencesPath = Path.Combine(Path.GetTempPath(), $"activation-callback-{Guid.NewGuid():N}.json");
        var preferences = new UserPreferencesService(preferencesPath);
        var updates = new UpdateViewModel(new UpdateChecker(http, "test", "unused"),
            new UpdateDownloader(http), preferences, Mock.Of<IUrlLauncher>(), Mock.Of<IInstaller>());
        await using var activation = new ShellActivationCoordinator(updates, gds, null, null,
            new ErrorConsoleService(), (_, token) => Task.Delay(Timeout.Infinite, token));
        var pending = activation.ActivateAsync(caller.Token);
        var cancelled = caller.CancelAsync();
        // This observer must run independently of the dispatcher: the defect blocks that dispatcher.
        var observed = Task.Run(async () =>
        {
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                environment.TrySetResult(new PythonEnvironmentInfo());
                await checkedEnvironment.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var heartbeat = Signal();
                Dispatcher.UIThread.Post(() => heartbeat.TrySetResult(), DispatcherPriority.Background);
                await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(1));
                pending.IsCompleted.ShouldBeFalse("the backend callback still owns its cleanup");
            }
            finally { releaseCallback.Set(); }
        });
        try { await observed; }
        finally
        {
            releaseCallback.Set();
            environment.TrySetResult(new PythonEnvironmentInfo());
            await cancelled;
            await pending;
            await callback.DisposeAsync();
            File.Delete(preferencesPath);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
