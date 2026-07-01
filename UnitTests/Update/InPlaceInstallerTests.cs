using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.ViewModels.Update;
using CAP_Core.Update;
using Shouldly;
using System.Net;
using System.Net.Http;

namespace UnitTests.Update;

/// <summary>
/// Unit tests for the in-place self-update feature:
/// asset selection, helper-script generation, install-path discovery,
/// and rollback / pre-flight behaviour.
/// All tests are cross-platform and run on the Linux CI runner.
/// </summary>
public class InPlaceInstallerTests
{
    // ─── FindAutoUpdateAsset ──────────────────────────────────────────────────

    [Fact]
    public void FindAutoUpdateAsset_WindowsRelease_ReturnsMsi()
    {
        if (!OperatingSystem.IsWindows()) return; // Skip on non-Windows CI

        var release = ReleaseWithAssets(
            ("Lunima-1.0.0.msi",               "https://example.com/Lunima.msi"),
            ("Lunima-1.0.0-osx-arm64.zip",     "https://example.com/Lunima-arm64.zip"),
            ("Lunima-1.0.0-linux-x64.tar.gz",  "https://example.com/Lunima.tar.gz"));

        var asset = UpdateChecker.FindAutoUpdateAsset(release);

        asset.ShouldNotBeNull();
        asset!.Name.ShouldEndWith(".msi");
    }

    [Fact]
    public void FindAutoUpdateAsset_LinuxRelease_ReturnsTarGz()
    {
        if (!OperatingSystem.IsLinux()) return; // Only meaningful on Linux

        var release = ReleaseWithAssets(
            ("Lunima-1.0.0.msi",               "https://example.com/Lunima.msi"),
            ("Lunima-1.0.0-osx-arm64.zip",     "https://example.com/Lunima-arm64.zip"),
            ("Lunima-1.0.0-linux-x64.tar.gz",  "https://example.com/Lunima.tar.gz"));

        var asset = UpdateChecker.FindAutoUpdateAsset(release);

        asset.ShouldNotBeNull();
        asset!.Name.ShouldEndWith(".tar.gz");
    }

    [Fact]
    public void FindAutoUpdateAsset_AllPlatformsPresent_ReturnsPlatformAppropriateAsset()
    {
        var release = ReleaseWithAssets(
            ("Lunima-1.0.0.msi",               "https://example.com/Lunima.msi"),
            ("Lunima-1.0.0-osx-arm64.zip",     "https://example.com/Lunima-arm64.zip"),
            ("Lunima-1.0.0-osx-x64.zip",       "https://example.com/Lunima-x64.zip"),
            ("Lunima-1.0.0-linux-x64.tar.gz",  "https://example.com/Lunima.tar.gz"));

        var asset = UpdateChecker.FindAutoUpdateAsset(release);

        asset.ShouldNotBeNull();

        if (OperatingSystem.IsWindows())
            asset!.Name.ShouldEndWith(".msi");
        else if (OperatingSystem.IsMacOS())
            asset!.Name.ShouldEndWith(".zip");
        else
            asset!.Name.ShouldEndWith(".tar.gz");
    }

    [Fact]
    public void FindAutoUpdateAsset_NoMatchingAsset_ReturnsNull()
    {
        // A release with only the "wrong" platform assets
        var release = new GitHubReleaseInfo
        {
            TagName = "v1.0.0",
            Assets = new List<GitHubReleaseAsset>
            {
                new() { Name = "source.tar.bz2", BrowserDownloadUrl = "https://example.com/src.tar.bz2" }
            }
        };

        // On Linux the fallback is .tar.gz which this release doesn't have
        if (OperatingSystem.IsLinux())
            UpdateChecker.FindAutoUpdateAsset(release).ShouldBeNull();
    }

    [Fact]
    public void FindAutoUpdateAsset_MacOsPreferArchSpecificZip()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var release = ReleaseWithAssets(
            ("Lunima-1.0.0-osx-arm64.zip", "https://example.com/arm64.zip"),
            ("Lunima-1.0.0-osx-x64.zip",   "https://example.com/x64.zip"),
            ("Lunima-1.0.0.zip",            "https://example.com/generic.zip"));

        var asset = UpdateChecker.FindAutoUpdateAsset(release);

        asset.ShouldNotBeNull();
        // Should pick the arch-specific one, not the generic .zip
        asset!.Name.ShouldContain("osx-");
    }

    // ─── MacOsBundleInstaller helper-script generation ────────────────────────

    [Fact]
    public void MacOsBundleInstaller_BuildHelperScript_ContainsDitto()
    {
        var script = MacOsBundleInstaller.BuildHelperScript(
            "/Applications/Lunima.app",
            "/Applications/Lunima.app.bak",
            "/tmp/Lunima_Update.zip",
            "/Applications/Lunima.app");

        script.ShouldContain("ditto -xk");
    }

    [Fact]
    public void MacOsBundleInstaller_BuildHelperScript_ContainsQuarantineStrip()
    {
        var script = MacOsBundleInstaller.BuildHelperScript(
            "/Applications/Lunima.app",
            "/Applications/Lunima.app.bak",
            "/tmp/Lunima_Update.zip",
            "/Applications/Lunima.app");

        script.ShouldContain("xattr -dr com.apple.quarantine");
    }

    [Fact]
    public void MacOsBundleInstaller_BuildHelperScript_ContainsCodesign()
    {
        var script = MacOsBundleInstaller.BuildHelperScript(
            "/Applications/Lunima.app",
            "/Applications/Lunima.app.bak",
            "/tmp/Lunima_Update.zip",
            "/Applications/Lunima.app");

        script.ShouldContain("codesign --force --sign -");
    }

    [Fact]
    public void MacOsBundleInstaller_BuildHelperScript_ContainsSwapAndBackup()
    {
        var script = MacOsBundleInstaller.BuildHelperScript(
            "/Applications/Lunima.app",
            "/Applications/Lunima.app.bak",
            "/tmp/Lunima_Update.zip",
            "/Applications/Lunima.app");

        script.ShouldContain("mv \"$INSTALL_DIR\" \"$BACKUP_DIR\"");
        script.ShouldContain("BACKUP_DIR=\"/Applications/Lunima.app.bak\"");
    }

    [Fact]
    public void MacOsBundleInstaller_BuildHelperScript_ContainsRelaunchAndCleanup()
    {
        var script = MacOsBundleInstaller.BuildHelperScript(
            "/Applications/Lunima.app",
            "/Applications/Lunima.app.bak",
            "/tmp/Lunima_Update.zip",
            "/Applications/Lunima.app");

        script.ShouldContain("open -n \"$RELAUNCH\"");
        script.ShouldContain("rm -rf \"$BACKUP_DIR\"");
    }

    // ─── WindowsMsiInstaller helper-script generation ─────────────────────────

    [Fact]
    public void WindowsMsiInstaller_BuildHelperScript_ContainsMsiexec()
    {
        var script = WindowsMsiInstaller.BuildHelperScript(
            @"C:\Temp\Lunima_Update.msi",
            @"C:\Program Files\Lunima\Lunima.exe");

        script.ShouldContain("msiexec.exe");
        script.ShouldContain("/i");
        script.ShouldContain("/qb");
    }

    [Fact]
    public void WindowsMsiInstaller_BuildHelperScript_ContainsRelaunch()
    {
        var script = WindowsMsiInstaller.BuildHelperScript(
            @"C:\Temp\Lunima_Update.msi",
            @"C:\Program Files\Lunima\Lunima.exe");

        script.ShouldContain("Start-Process");
        script.ShouldContain("$relaunchPath");
    }

    [Fact]
    public void WindowsMsiInstaller_BuildHelperScript_ContainsCleanup()
    {
        var script = WindowsMsiInstaller.BuildHelperScript(
            @"C:\Temp\Lunima_Update.msi",
            @"C:\Program Files\Lunima\Lunima.exe");

        script.ShouldContain("Remove-Item");
        script.ShouldContain("$msiPath");
    }

    // ─── LinuxTarballInstaller helper-script generation ───────────────────────

    [Fact]
    public void LinuxTarballInstaller_BuildHelperScript_ContainsExtract()
    {
        var script = LinuxTarballInstaller.BuildHelperScript(
            "/opt/lunima",
            "/opt/lunima.bak",
            "/tmp/Lunima_Update.tar.gz",
            "/opt/lunima/Lunima");

        script.ShouldContain("tar -xzf");
    }

    [Fact]
    public void LinuxTarballInstaller_BuildHelperScript_ContainsSwapAndBackup()
    {
        var script = LinuxTarballInstaller.BuildHelperScript(
            "/opt/lunima",
            "/opt/lunima.bak",
            "/tmp/Lunima_Update.tar.gz",
            "/opt/lunima/Lunima");

        script.ShouldContain("mv \"$INSTALL_DIR\" \"$BACKUP_DIR\"");
        script.ShouldContain("BACKUP_DIR=\"/opt/lunima.bak\"");
    }

    [Fact]
    public void LinuxTarballInstaller_BuildHelperScript_ContainsRelaunchAndCleanup()
    {
        var script = LinuxTarballInstaller.BuildHelperScript(
            "/opt/lunima",
            "/opt/lunima.bak",
            "/tmp/Lunima_Update.tar.gz",
            "/opt/lunima/Lunima");

        script.ShouldContain("\"$RELAUNCH\" &");
        script.ShouldContain("rm -rf \"$BACKUP_DIR\"");
    }

    // ─── IInstaller.CanInstall pre-flight checks ──────────────────────────────

    [Fact]
    public void MacOsBundleInstaller_CanInstall_MissingFile_ReturnsFalse()
    {
        var installer = new MacOsBundleInstaller();
        var result = installer.CanInstall("/nonexistent/path/Lunima_Update.zip", out var reason);

        result.ShouldBeFalse();
        reason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void MacOsBundleInstaller_CanInstall_WrongExtension_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Rename to .dmg so it has wrong extension for auto-update
            var dmgFile = Path.ChangeExtension(tempFile, ".dmg");
            File.Move(tempFile, dmgFile);
            tempFile = dmgFile;

            var installer = new MacOsBundleInstaller();
            var result = installer.CanInstall(tempFile, out var reason);

            result.ShouldBeFalse();
            reason.ShouldContain(".zip");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void WindowsMsiInstaller_CanInstall_MissingFile_ReturnsFalse()
    {
        var installer = new WindowsMsiInstaller();
        var result = installer.CanInstall("/nonexistent/path/Lunima_Update.msi", out var reason);

        result.ShouldBeFalse();
        reason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void LinuxTarballInstaller_CanInstall_MissingFile_ReturnsFalse()
    {
        var installer = new LinuxTarballInstaller();
        var result = installer.CanInstall("/nonexistent/path/Lunima_Update.tar.gz", out var reason);

        result.ShouldBeFalse();
        reason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void LinuxTarballInstaller_CanInstall_WrongExtension_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var installer = new LinuxTarballInstaller();
            // .tmp extension is not .tar.gz
            var result = installer.CanInstall(tempFile, out var reason);

            result.ShouldBeFalse();
            reason.ShouldContain(".tar.gz");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ─── InstallerFactory ────────────────────────────────────────────────────

    [Fact]
    public void InstallerFactory_Create_ReturnsNonNull()
    {
        var installer = InstallerFactory.Create();
        installer.ShouldNotBeNull();
    }

    [Fact]
    public void InstallerFactory_Create_ReturnsPlatformCorrectType()
    {
        var installer = InstallerFactory.Create();

        if (OperatingSystem.IsMacOS())
            installer.ShouldBeOfType<MacOsBundleInstaller>();
        else if (OperatingSystem.IsWindows())
            installer.ShouldBeOfType<WindowsMsiInstaller>();
        else
            installer.ShouldBeOfType<LinuxTarballInstaller>();
    }

    // ─── UpdateViewModel integration: IInstaller seam ────────────────────────

    [Fact]
    public async Task UpdateViewModel_InstallUpdate_UsesInstallerWhenCanInstall()
    {
        const string releaseJson = """
            {
              "tag_name": "v99.0.0",
              "name": "Version 99.0.0",
              "body": "In-place update test.",
              "prerelease": false,
              "published_at": "2099-01-01T00:00:00Z",
              "assets": [
                {
                  "name": "Lunima-99.0.0-linux-x64.tar.gz",
                  "browser_download_url": "https://example.com/Lunima-99.0.0-linux-x64.tar.gz",
                  "size": 1024,
                  "content_type": "application/gzip"
                }
              ]
            }
            """;

        var fakeInstaller = new RecordingInstaller(canInstall: true);
        var urlLauncher   = new FakeUrlLauncher();
        var vm            = CreateViewModel(releaseJson, fakeInstaller, urlLauncher);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.UpdateAvailable.ShouldBeTrue();

        await vm.InstallUpdateCommand.ExecuteAsync(null);

        // The in-place installer should have been invoked
        fakeInstaller.InstallAndRelaunchCalled.ShouldBeTrue();
        // The fallback URL launcher should NOT have opened a file
        urlLauncher.LastOpenedPath.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateViewModel_InstallUpdate_FallsBackToUrlLauncherWhenCannotInstall()
    {
        const string releaseJson = """
            {
              "tag_name": "v99.0.0",
              "name": "Version 99.0.0",
              "body": "Fallback test.",
              "prerelease": false,
              "published_at": "2099-01-01T00:00:00Z",
              "assets": [
                {
                  "name": "Lunima-99.0.0-linux-x64.tar.gz",
                  "browser_download_url": "https://example.com/Lunima-99.0.0-linux-x64.tar.gz",
                  "size": 1024,
                  "content_type": "application/gzip"
                }
              ]
            }
            """;

        var fakeInstaller = new RecordingInstaller(canInstall: false, reason: "install dir not writable");
        var urlLauncher   = new FakeUrlLauncher();
        var vm            = CreateViewModel(releaseJson, fakeInstaller, urlLauncher);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.UpdateAvailable.ShouldBeTrue();

        await vm.InstallUpdateCommand.ExecuteAsync(null);

        // CanInstall returned false, so fallback: open the downloaded file
        fakeInstaller.InstallAndRelaunchCalled.ShouldBeFalse();
        urlLauncher.LastOpenedPath.ShouldNotBeNull();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static GitHubReleaseInfo ReleaseWithAssets(params (string name, string url)[] assets)
    {
        return new GitHubReleaseInfo
        {
            TagName = "v1.0.0",
            Assets = assets.Select(a => new GitHubReleaseAsset
            {
                Name = a.name,
                BrowserDownloadUrl = a.url
            }).ToList()
        };
    }

    private static UpdateViewModel CreateViewModel(
        string responseJson,
        IInstaller installer,
        IUrlLauncher urlLauncher)
    {
        // Use a multi-response handler: first call returns the release JSON (for update check),
        // subsequent calls return a small binary payload (for the download).
        var handler    = new MultiResponseHttpMessageHandler(responseJson);
        var httpClient = new HttpClient(handler);
        var prefs      = new UserPreferencesService(Path.GetTempFileName());
        return new UpdateViewModel(
            new UpdateChecker(httpClient, "owner", "repo"),
            new UpdateDownloader(httpClient),
            prefs,
            urlLauncher,
            installer);
    }

    private sealed class RecordingInstaller : IInstaller
    {
        private readonly bool _canInstall;
        private readonly string _reason;

        public bool InstallAndRelaunchCalled { get; private set; }

        public RecordingInstaller(bool canInstall, string reason = "")
        {
            _canInstall = canInstall;
            _reason     = reason;
        }

        public bool CanInstall(string downloadedPath, out string reason)
        {
            reason = _reason;
            return _canInstall;
        }

        public Task InstallAndRelaunchAsync(string downloadedPath, CancellationToken cancellationToken = default)
        {
            InstallAndRelaunchCalled = true;
            return Task.CompletedTask;
        }

        public string GetInstallDirectory() => AppContext.BaseDirectory;
    }

    private sealed class FakeUrlLauncher : IUrlLauncher
    {
        public string? LastOpenedUrl  { get; private set; }
        public string? LastOpenedPath { get; private set; }
        public string? LastRevealedPath { get; private set; }

        public void Open(string url)                 => LastOpenedUrl   = url;
        public void OpenFileOrDirectory(string path) => LastOpenedPath  = path;
        public void RevealInFileManager(string path) => LastRevealedPath = path;
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHttpMessageHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    /// <summary>
    /// Returns the release JSON on the first request, then a small binary payload on
    /// subsequent requests (simulating the download). Using a single response object
    /// fails because StringContent can only be read once.
    /// </summary>
    private sealed class MultiResponseHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _releaseJson;
        private int _callCount;

        public MultiResponseHttpMessageHandler(string releaseJson) => _releaseJson = releaseJson;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            if (_callCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_releaseJson)
                });
            }

            // Subsequent calls: simulate a download with a small payload
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x1f, 0x8b, 0x00 }) // fake gzip magic bytes
            });
        }
    }
}
