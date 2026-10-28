using CAP_DataAccess.Updates;
using System.Net;
using System.Text.Json;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.ViewModels.Update;
using Moq;
using Shouldly;

namespace UnitTests.Update;

public sealed class UnsignedUpdateFallbackTests
{
    [Fact]
    public async Task ReleaseWithoutPublisherManifest_OpensManualPageWithoutDownloadingOrExecuting()
    {
        using var handler = new UnsignedReleaseHandler();
        using var client = new HttpClient(handler);
        var launcher = new Mock<IUrlLauncher>();
        var installer = new RecordingInstaller();
        var preferences = new UserPreferencesService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var viewModel = new UpdateViewModel(new UpdateChecker(client, "fixture", "local"),
            new UpdateDownloader(client), preferences, launcher.Object, installer);
        viewModel.ShutdownAfterConfirmation = () => { };
        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        viewModel.UpdateAvailable.ShouldBeTrue();
        await viewModel.InstallUpdateCommand.ExecuteAsync(null);
        try
        {
            installer.Path.ShouldBeNull("an unsigned release must never auto-execute");
            handler.ArtifactRequests.ShouldBe(0);
            launcher.Verify(l => l.Open("https://github.com/aignermax/Lunima/releases/tag/v99.0.0"), Times.Once);
            launcher.Verify(l => l.OpenFileOrDirectory(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            if (installer.Path != null) File.Delete(installer.Path);
        }
    }

    private sealed class RecordingInstaller : IInstaller
    {
        public string? Path;
        public bool CanInstallInPlace(out string reason) { reason = ""; return true; }
        public void LaunchUpdater(string downloadedArchivePath) => Path = downloadedArchivePath;
    }

    private sealed class UnsignedReleaseHandler : HttpMessageHandler
    {
        public int ArtifactRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "api.github.com")
            {
                ArtifactRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[4]) });
            }
            var names = new[] { "Lunima-Setup-99.0.0.msi", "Lunima-99.0.0-linux-x64.tar.gz",
                "Lunima-99.0.0-osx-arm64.zip", "Lunima-99.0.0-osx-x64.zip" };
            var release = new
            {
                tag_name = "v99.0.0", name = "Unsigned fixture", body = "", prerelease = false,
                published_at = "2026-10-23T00:00:00Z", assets = names.Select(name => new
                {
                    name, browser_download_url = "https://example.test/" + name, size = 4,
                    content_type = "application/octet-stream"
                })
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release)) });
        }
    }
}
