using CAP_DataAccess.Updates;
using System.Net;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.ViewModels.Update;
using CAP_Core.Update;
using Moq;
using Shouldly;

namespace UnitTests.Update;

public sealed class UpdateRetirementTests
{
    [Fact]
    public async Task Stop_CancelsAndDrainsManualReleaseCheck()
    {
        using var handler = new ReleaseHandler(block: true);
        using var fixture = new Fixture(handler);
        var check = fixture.ViewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = fixture.ViewModel.StopAsync();
        try
        {
            await handler.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            handler.Token.IsCancellationRequested.ShouldBeTrue();
            stop.IsCompleted.ShouldBeFalse("a canceled request still owns work until it exits");
        }
        finally
        {
            handler.Release.TrySetResult();
            await Task.WhenAll(check, stop).WaitAsync(TimeSpan.FromSeconds(2));
        }
        fixture.ViewModel.UpdateAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Stop_CancelsAndDrainsPreparationBeforeDisposingLateArtifact()
    {
        using var fixture = new Fixture();
        fixture.Preparation.Block = true;
        await fixture.ViewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        var install = fixture.ViewModel.InstallUpdateCommand.ExecuteAsync(null);
        await fixture.Preparation.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = fixture.ViewModel.StopAsync();
        try
        {
            await fixture.Preparation.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            fixture.Preparation.Token.IsCancellationRequested.ShouldBeTrue();
            stop.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            fixture.Preparation.Release.TrySetResult();
            await Task.WhenAll(install, stop).WaitAsync(TimeSpan.FromSeconds(2));
        }
        fixture.Preparation.Package.Disposed.ShouldBeTrue();
        fixture.Installer.Launches.ShouldBe(0);
    }

    [Fact]
    public async Task Stop_DuringCloseConfirmation_PreventsLateInstallerLaunch()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approve = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ViewModel.ConfirmShutdownAsync = () => { entered.TrySetResult(); return approve.Task; };
        await fixture.ViewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        var install = fixture.ViewModel.InstallUpdateCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try { await fixture.ViewModel.StopAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        finally
        {
            approve.TrySetResult(true);
            await install.WaitAsync(TimeSpan.FromSeconds(2));
        }
        fixture.Installer.Launches.ShouldBe(0);
        fixture.Preparation.Package.Disposed.ShouldBeTrue();
        fixture.Preparation.Package.Transferred.ShouldBeFalse();
        await fixture.ViewModel.InstallUpdateCommand.ExecuteAsync(null);
        fixture.Preparation.Calls.ShouldBe(1, "a retired view cannot start a new download");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _preferences = Path.GetTempFileName();
        private readonly HttpClient _client;
        public Preparation Preparation { get; } = new();
        public Installer Installer { get; } = new();
        public UpdateViewModel ViewModel { get; }
        public Fixture(HttpMessageHandler? handler = null)
        {
            _client = new HttpClient(handler ?? new ReleaseHandler(false));
            ViewModel = new UpdateViewModel(new UpdateChecker(_client, "fixture", "fixture"), Preparation,
                new UserPreferencesService(_preferences), Mock.Of<IUrlLauncher>(), Installer);
            ViewModel.ShutdownAfterConfirmation = () => { };
        }
        public void Dispose() { _client.Dispose(); File.Delete(_preferences); }
    }

    private sealed class Preparation : IUpdatePreparation
    {
        public bool Block;
        public int Calls;
        public CancellationToken Token;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Package Package { get; } = new();
        public async Task<IUpdateDownload> DownloadInstallerAsync(GitHubReleaseInfo release, GitHubReleaseAsset asset,
            SemanticVersion currentVersion, IProgress<double> progress, CancellationToken cancellationToken = default)
        {
            Calls++;
            Token = cancellationToken;
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Started.TrySetResult();
            if (Block) await Release.Task;
            return Package; // A late success must still be retired even if this provider ignored cancellation.
        }
    }

    private sealed class Package : IUpdateDownload
    {
        public string Path => "fixture-installer";
        public bool Disposed;
        public bool Transferred;
        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void HandOffToInstaller() => Transferred = true;
        public void Dispose() => Disposed = true;
    }

    private sealed class Installer : IInstaller
    {
        public int Launches;
        public bool CanInstallInPlace(out string reason) { reason = ""; return true; }
        public void LaunchUpdater(string downloadedArchivePath) => Launches++;
    }

    private sealed class ReleaseHandler(bool block) : HttpMessageHandler
    {
        public CancellationToken Token;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Token = token;
            using var registration = token.Register(() => Canceled.TrySetResult());
            Started.TrySetResult();
            if (block) await Release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"tag_name":"v99.0.0","name":"Fixture","body":"Fixture","prerelease":false,
                "published_at":"2099-01-01T00:00:00Z","assets":[
                  {"name":"Lunima.msi","browser_download_url":"https://fixture.test/win","size":4,"content_type":"x"},
                  {"name":"Lunima-osx-arm64.zip","browser_download_url":"https://fixture.test/arm","size":4,"content_type":"x"},
                  {"name":"Lunima-osx-x64.zip","browser_download_url":"https://fixture.test/mac","size":4,"content_type":"x"},
                  {"name":"Lunima-linux-x64.tar.gz","browser_download_url":"https://fixture.test/linux","size":4,"content_type":"x"}]}
                """) };
        }
    }
}
