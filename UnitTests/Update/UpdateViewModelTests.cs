using System.Net;
using System.Net.Http;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
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
        string? tempPrefsPath = null,
        IUrlLauncher? urlLauncher = null,
        IInstaller? installer = null)
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
        return new UpdateViewModel(checker, downloader, prefs, urlLauncher ?? new FakeUrlLauncher(), installer ?? new FakeInstaller());
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
    public async Task CheckForUpdates_Available_HeadlineShowsBothVersions_NoRedundantStatusEcho()
    {
        // Banner clarity fix: a single headline contrasts current → latest (each version once),
        // and the live status line is not a redundant restatement of the same version.
        var vm = CreateViewModel(NewerReleaseJson);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        vm.UpdateAvailable.ShouldBeTrue();
        vm.LatestVersionText.ShouldStartWith("Update available: v");
        vm.LatestVersionText.ShouldContain("→");      // both versions, contrasted
        vm.LatestVersionText.ShouldContain("99.0.0"); // the latest (online) version
        vm.StatusText.ShouldBeNullOrEmpty();          // no redundant version echo
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
            var vm = new UpdateViewModel(checker, downloader, prefs, new FakeUrlLauncher(), new FakeInstaller());

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
                new UpdateChecker(httpClient, "o", "r"), new UpdateDownloader(httpClient), prefs, new FakeUrlLauncher(), new FakeInstaller());

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
                new UpdateChecker(httpClient, "o", "r"), new UpdateDownloader(httpClient), prefs, new FakeUrlLauncher(), new FakeInstaller());

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

        var urlLauncher = new FakeUrlLauncher();
        var vm = CreateViewModel(noMsiJson, urlLauncher: urlLauncher);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.UpdateAvailable.ShouldBeTrue();

        await vm.InstallUpdateCommand.ExecuteAsync(null);

        vm.StatusText.ShouldContain("GitHub releases page");
        urlLauncher.LastOpenedUrl.ShouldBe("https://github.com/aignermax/Lunima/releases/tag/v99.0.0");
    }

    [Fact]
    public async Task InstallUpdate_OnMacOrLinux_OpensDownloadedInstallerViaLauncher()
    {
        // #610: on macOS/Linux the update downloads the platform installer (.dmg/.tar.gz) and
        // opens it through the PATH-safe launcher — it must not fall back to the browser, and
        // (unlike Windows) it must not auto-quit the app.
        if (OperatingSystem.IsWindows()) return; // the Windows branch runs msiexec; not exercised on the Linux CI runner

        var urlLauncher = new FakeUrlLauncher();
        var vm = CreateViewModelForDownload(MultiPlatformReleaseJson, urlLauncher);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.UpdateAvailable.ShouldBeTrue();

        await vm.InstallUpdateCommand.ExecuteAsync(null);

        urlLauncher.LastOpenedPath.ShouldNotBeNull();   // installer opened via the launcher
        urlLauncher.LastOpenedUrl.ShouldBeNull();       // not the browser fallback
        var expectedExtension = OperatingSystem.IsMacOS() ? ".dmg" : ".tar.gz";
        urlLauncher.LastOpenedPath!.EndsWith(expectedExtension).ShouldBeTrue();

        if (File.Exists(urlLauncher.LastOpenedPath!))
            File.Delete(urlLauncher.LastOpenedPath!);
    }

    [Fact]
    public async Task InstallUpdate_WhenSelfUpdatePossible_LaunchesUpdaterWithDownloadedArchive()
    {
        // With an installed, writable location AND an auto-update archive for this OS, Download must
        // perform an in-place update: hand the downloaded archive to the installer, not the browser.
        var installer = new FakeInstaller { CanUpdate = true };
        var urlLauncher = new FakeUrlLauncher();
        var vm = CreateViewModelForDownload(AutoUpdateReleaseJson, urlLauncher, installer);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.UpdateAvailable.ShouldBeTrue();

        await vm.InstallUpdateCommand.ExecuteAsync(null);

        installer.LaunchedArchivePath.ShouldNotBeNull();   // the in-place updater was launched
        urlLauncher.LastOpenedUrl.ShouldBeNull();          // not the browser fallback
        urlLauncher.LastOpenedPath.ShouldBeNull();         // not the manual open-installer path

        if (installer.LaunchedArchivePath is not null && File.Exists(installer.LaunchedArchivePath))
            File.Delete(installer.LaunchedArchivePath);
    }

    private const string AutoUpdateReleaseJson = """
        {
          "tag_name": "v99.0.0",
          "name": "Version 99.0.0",
          "body": "Auto-update release.",
          "prerelease": false,
          "published_at": "2099-01-01T00:00:00Z",
          "assets": [
            { "name": "Lunima-99.0.0-osx-arm64.zip", "browser_download_url": "https://example.com/Lunima-99.0.0-osx-arm64.zip", "size": 4, "content_type": "application/zip" },
            { "name": "Lunima-99.0.0-osx-x64.zip", "browser_download_url": "https://example.com/Lunima-99.0.0-osx-x64.zip", "size": 4, "content_type": "application/zip" },
            { "name": "Lunima-99.0.0-linux-x64.tar.gz", "browser_download_url": "https://example.com/Lunima-99.0.0-linux-x64.tar.gz", "size": 4, "content_type": "application/gzip" },
            { "name": "Lunima-Setup-99.0.0.msi", "browser_download_url": "https://example.com/Lunima-Setup-99.0.0.msi", "size": 4, "content_type": "application/x-msi" }
          ]
        }
        """;

    private const string MultiPlatformReleaseJson = """
        {
          "tag_name": "v99.0.0",
          "name": "Version 99.0.0",
          "body": "Major update.",
          "prerelease": false,
          "published_at": "2099-01-01T00:00:00Z",
          "assets": [
            { "name": "Lunima-99.0.0-osx-arm64.dmg", "browser_download_url": "https://example.com/Lunima-99.0.0-osx-arm64.dmg", "size": 4, "content_type": "application/octet-stream" },
            { "name": "Lunima-99.0.0-linux-x64.tar.gz", "browser_download_url": "https://example.com/Lunima-99.0.0-linux-x64.tar.gz", "size": 4, "content_type": "application/gzip" },
            { "name": "Lunima-Setup-99.0.0.msi", "browser_download_url": "https://example.com/Lunima-Setup-99.0.0.msi", "size": 4, "content_type": "application/x-msi" }
          ]
        }
        """;

    private static UpdateViewModel CreateViewModelForDownload(
        string releaseJson, IUrlLauncher urlLauncher, IInstaller? installer = null)
    {
        var httpClient = new HttpClient(new ReleaseThenBinaryHandler(releaseJson));
        return new UpdateViewModel(
            new UpdateChecker(httpClient, "owner", "repo"),
            new UpdateDownloader(httpClient),
            new UserPreferencesService(Path.GetTempFileName()),
            urlLauncher,
            installer ?? new FakeInstaller());
    }

    /// <summary>Returns the release JSON for the GitHub API call and a few bytes for any other
    /// (asset download) URL, with a fresh response each call so the download stream is readable.</summary>
    private sealed class ReleaseThenBinaryHandler : HttpMessageHandler
    {
        private readonly string _releaseJson;

        public ReleaseThenBinaryHandler(string releaseJson) => _releaseJson = releaseJson;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isApi = request.RequestUri!.Host.Contains("api.github.com");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = isApi
                    ? new StringContent(_releaseJson)
                    : new ByteArrayContent(new byte[] { 1, 2, 3, 4 }),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeUrlLauncher : IUrlLauncher
    {
        public string? LastOpenedUrl { get; private set; }
        public string? LastOpenedPath { get; private set; }
        public string? LastRevealedPath { get; private set; }

        public void Open(string url)
        {
            LastOpenedUrl = url;
        }

        public void OpenFileOrDirectory(string path)
        {
            LastOpenedPath = path;
        }

        public void RevealInFileManager(string path)
        {
            LastRevealedPath = path;
        }
    }

    /// <summary>Test double for <see cref="IInstaller"/>: records the launch and lets a test
    /// toggle whether in-place update is available.</summary>
    private sealed class FakeInstaller : IInstaller
    {
        public bool CanUpdate { get; set; }
        public string? LaunchedArchivePath { get; private set; }

        public bool CanInstallInPlace(out string reason)
        {
            reason = CanUpdate ? string.Empty : "test: in-place update disabled";
            return CanUpdate;
        }

        public void LaunchUpdater(string downloadedArchivePath) => LaunchedArchivePath = downloadedArchivePath;
    }

    // --- Skip for Today tests ---

    [Fact]
    public async Task StartupCheck_SkipForTodaySet_DoesNotShowNotification()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            prefs.SkipToday();

            var vm = CreateViewModelWithPrefs(NewerReleaseJson, prefs);

            await vm.CheckForUpdatesOnStartupAsync();

            vm.UpdateAvailable.ShouldBeFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task StartupCheck_SkipForTodayNotSet_ShowsNotification()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            // No skip set — should check today
            var vm = CreateViewModelWithPrefs(NewerReleaseJson, prefs);

            await vm.CheckForUpdatesOnStartupAsync();

            vm.UpdateAvailable.ShouldBeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task StartupCheck_SkippedVersionMatchesRelease_DoesNotShowNotification()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            prefs.SetSkippedUpdateVersion(new SemanticVersion(99, 0, 0));

            var vm = CreateViewModelWithPrefs(NewerReleaseJson, prefs);

            await vm.CheckForUpdatesOnStartupAsync();

            vm.UpdateAvailable.ShouldBeFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ManualCheck_AlwaysShowsUpdate_EvenWhenSkippedToday()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            prefs.SkipToday();

            var vm = CreateViewModelWithPrefs(NewerReleaseJson, prefs);

            // Manual check ignores skip-today
            await vm.CheckForUpdatesCommand.ExecuteAsync(null);

            vm.UpdateAvailable.ShouldBeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void SkipForToday_HidesPanelAndPersistsTodayDate()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            var vm = CreateViewModelWithPrefs(NewerReleaseJson, prefs);

            vm.SkipForTodayCommand.Execute(null);

            vm.UpdateAvailable.ShouldBeFalse();
            prefs.ShouldCheckToday().ShouldBeFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ShouldCheckToday_AfterSkipToday_ReturnsFalse()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            prefs.SkipToday();
            prefs.ShouldCheckToday().ShouldBeFalse();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ShouldCheckToday_WhenNeverSkipped_ReturnsTrue()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var prefs = new UserPreferencesService(tempPath);
            prefs.ShouldCheckToday().ShouldBeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static UpdateViewModel CreateViewModelWithPrefs(string responseJson, UserPreferencesService prefs)
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        var httpClient = new HttpClient(handler);
        return new UpdateViewModel(
            new UpdateChecker(httpClient, "owner", "repo"),
            new UpdateDownloader(httpClient),
            prefs,
            new FakeUrlLauncher(),
            new FakeInstaller());
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
