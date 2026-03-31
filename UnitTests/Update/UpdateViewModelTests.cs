using System.Net;
using System.Net.Http;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Update;
using CAP_Core.Update;
using Shouldly;

namespace UnitTests.Update;

/// <summary>
/// Integration tests for UpdateViewModel — combines UpdateChecker (with mocked HTTP)
/// and UpdateDownloader, exercising the full check → UI state flow.
/// </summary>
public class UpdateViewModelTests
{
    private const string NewerReleaseJson = """
        {
          "tag_name": "v99.0.0",
          "name": "Version 99.0.0",
          "body": "Major update with improvements.",
          "prerelease": false,
          "published_at": "2099-01-01T00:00:00Z",
          "assets": [
            {
              "name": "Lunima-99.0.0.msi",
              "browser_download_url": "https://example.com/Lunima-99.0.0.msi",
              "size": 1024,
              "content_type": "application/x-msi"
            }
          ]
        }
        """;

    private const string OlderReleaseJson = """
        {
          "tag_name": "v0.0.1",
          "name": "Version 0.0.1",
          "body": "Initial release.",
          "prerelease": false,
          "published_at": "2020-01-01T00:00:00Z",
          "assets": []
        }
        """;

    private static UpdateViewModel CreateViewModel(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? tempPrefsPath = null)
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(statusCode)
            {
                Content = statusCode == HttpStatusCode.OK
                    ? new StringContent(responseJson)
                    : new StringContent("")
            });
        var httpClient = new HttpClient(handler);
        var checker = new UpdateChecker(httpClient, "owner", "repo");
        var downloader = new UpdateDownloader(httpClient);
        var prefs = tempPrefsPath != null
            ? new UserPreferencesService(tempPrefsPath)
            : new UserPreferencesService(Path.GetTempFileName());
        return new UpdateViewModel(checker, downloader, prefs);
    }

    [Fact]
    public async Task CheckForUpdates_NewerVersionExists_SetsUpdateAvailableTrue()
    {
        var vm = CreateViewModel(NewerReleaseJson);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        vm.UpdateAvailable.ShouldBeTrue();
        vm.LatestVersionText.ShouldContain("99");
        vm.ReleaseNotes.ShouldContain("Major update");
    }

    [Fact]
    public async Task CheckForUpdates_CurrentIsLatest_UpdateAvailableIsFalse()
    {
        var vm = CreateViewModel(OlderReleaseJson);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        vm.UpdateAvailable.ShouldBeFalse();
        vm.StatusText.ShouldContain("up to date");
    }

    [Fact]
    public async Task CheckForUpdates_HttpFailure_ShowsErrorStatus()
    {
        var vm = CreateViewModel("", HttpStatusCode.ServiceUnavailable);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        vm.UpdateAvailable.ShouldBeFalse();
        vm.StatusText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckForUpdates_SkippedVersionMatches_StillShowsUpdateForManualCheck()
    {
        // Manual check always shows updates, even for previously skipped versions.
        // Only auto-startup checks should respect the skip preference.
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            prefs.SetSkippedUpdateVersion(new SemanticVersion(99, 0, 0));

            var handler = new FakeHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(NewerReleaseJson)
                });
            var httpClient = new HttpClient(handler);
            var checker = new UpdateChecker(httpClient, "owner", "repo");
            var downloader = new UpdateDownloader(httpClient);
            var vm = new UpdateViewModel(checker, downloader, prefs);

            await vm.CheckForUpdatesCommand.ExecuteAsync(null);

            vm.UpdateAvailable.ShouldBeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task SkipThisVersion_PersistsVersionAndHidesPanel()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            var handler = new FakeHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(NewerReleaseJson)
                });
            var httpClient = new HttpClient(handler);
            var vm = new UpdateViewModel(
                new UpdateChecker(httpClient, "o", "r"), new UpdateDownloader(httpClient), prefs);

            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            vm.UpdateAvailable.ShouldBeTrue();

            vm.SkipThisVersionCommand.Execute(null);

            vm.UpdateAvailable.ShouldBeFalse();
            var skipped = prefs.GetSkippedUpdateVersion();
            skipped.ShouldNotBeNull();
            skipped!.Major.ShouldBe(99);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task RemindLater_HidesPanelWithoutPersisting()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            var handler = new FakeHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(NewerReleaseJson)
                });
            var httpClient = new HttpClient(handler);
            var vm = new UpdateViewModel(
                new UpdateChecker(httpClient, "o", "r"), new UpdateDownloader(httpClient), prefs);

            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            vm.UpdateAvailable.ShouldBeTrue();

            vm.RemindLaterCommand.Execute(null);

            vm.UpdateAvailable.ShouldBeFalse();
            prefs.GetSkippedUpdateVersion().ShouldBeNull();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void CurrentVersionText_ContainsVersionPrefix()
    {
        var vm = CreateViewModel(OlderReleaseJson);
        vm.CurrentVersionText.ShouldStartWith("Current: v");
    }

    [Fact]
    public async Task InstallUpdate_NoMsiAsset_ShowsErrorMessage()
    {
        var noMsiJson = """
            {
              "tag_name": "v99.0.0",
              "name": "Version 99.0.0",
              "body": "No MSI this time.",
              "prerelease": false,
              "published_at": "2099-01-01T00:00:00Z",
              "assets": []
            }
            """;

        var vm = CreateViewModel(noMsiJson);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.UpdateAvailable.ShouldBeTrue();

        await vm.InstallUpdateCommand.ExecuteAsync(null);

        vm.StatusText.ShouldContain("GitHub releases page");
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}
