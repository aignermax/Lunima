using Avalonia.Controls.ApplicationLifetimes;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP_Core.Update;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;

namespace CAP.Avalonia.ViewModels.Update;

/// <summary>
/// ViewModel for the software update panel.
/// Handles checking GitHub releases for newer versions, downloading the MSI,
/// and installing it with graceful application shutdown.
/// </summary>
public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateChecker _updateChecker;
    private readonly UpdateDownloader _downloader;
    private readonly UserPreferencesService _preferences;
    private readonly IUrlLauncher _urlLauncher;
    private readonly IInstaller _installer;
    private readonly SemanticVersion _currentVersion;

    private GitHubReleaseInfo? _availableRelease;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _updateAvailable;

    /// <summary>
    /// Headline shown in the update banner, contrasting both versions once each —
    /// "Update available: v{current} → v{latest}". Avoids the previous redundancy
    /// where the new version was restated in a second status line.
    /// </summary>
    [ObservableProperty]
    private string _latestVersionText = "";

    [ObservableProperty]
    private string _releaseNotes = "";

    /// <summary>Gets the current application version as a display string.</summary>
    public string CurrentVersionText => $"Current: v{_currentVersion}";

    /// <summary>Initializes a new instance of <see cref="UpdateViewModel"/>.</summary>
    public UpdateViewModel(
        UpdateChecker updateChecker,
        UpdateDownloader downloader,
        UserPreferencesService preferences,
        IUrlLauncher urlLauncher,
        IInstaller installer)
    {
        _updateChecker = updateChecker;
        _downloader = downloader;
        _preferences = preferences;
        _urlLauncher = urlLauncher;
        _installer = installer;
        _currentVersion = ResolveCurrentVersion();
    }

    /// <summary>
    /// Checks GitHub for a newer release. Updates state to reflect whether
    /// an update is available. Skips versions the user has already dismissed.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (IsChecking || IsDownloading) return;

        IsChecking = true;
        UpdateAvailable = false;
        StatusText = "Checking for updates...";

        try
        {
            var release = await _updateChecker.GetLatestReleaseAsync();
            if (release == null)
            {
                StatusText = "Could not reach update server. Check your internet connection.";
                return;
            }

            var releaseVersion = release.ParsedVersion;

            if (!UpdateChecker.IsNewerThan(release, _currentVersion))
            {
                StatusText = $"You are up to date! (v{releaseVersion ?? _currentVersion})";
                return;
            }

            // Manual check: always show updates, even if previously skipped
            // (User explicitly wants to check, so honor that intent)
            _availableRelease = release;
            LatestVersionText = $"Update available: v{_currentVersion} → v{releaseVersion}";
            ReleaseNotes = TruncateReleaseNotes(release.Body);
            UpdateAvailable = true;
            // The headline already states the version transition; keep the live status line
            // empty here so it isn't a redundant echo (it carries download progress later).
            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = $"Update check failed: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Downloads the update and applies it. When the app can update in place (installed from a
    /// writable location and the release ships an auto-update archive) it downloads that archive,
    /// launches a detached updater that swaps the installation and relaunches the new version, then
    /// quits. Otherwise it falls back to the manual installer / releases page so the user is never
    /// left without a path forward.
    /// </summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_availableRelease == null || IsDownloading) return;

        var canSelfUpdate = _installer.CanInstallInPlace(out _);
        var autoUpdateAsset = canSelfUpdate ? UpdateChecker.FindAutoUpdateAsset(_availableRelease) : null;

        // Fall back to the manual installer (macOS .dmg, Windows .msi, …) when in-place update
        // isn't possible or the release carries no auto-update archive.
        var asset = autoUpdateAsset ?? UpdateChecker.FindPlatformAsset(_availableRelease);
        var selfUpdate = autoUpdateAsset != null;

        if (asset == null)
        {
            OpenReleasesPageInBrowser();
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        StatusText = selfUpdate ? "Downloading update..." : "Downloading installer...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusText = $"Downloading... {p:P0}";
            });

            var downloadedPath = await _downloader.DownloadInstallerAsync(
                asset.BrowserDownloadUrl, asset.Size, progress);

            if (selfUpdate)
            {
                // Swap the installation in place and relaunch. The detached updater waits for this
                // process to exit before replacing files, so we shut down immediately after.
                StatusText = "Installing update and restarting...";
                _installer.LaunchUpdater(downloadedPath);
                ShutdownApplication();
                return;
            }

            ApplyManualInstaller(downloadedPath);
        }
        catch (Exception ex)
        {
            StatusText = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Manual fallback when an in-place update isn't possible: on Windows launch the MSI (which
    /// needs the app to close), otherwise open the downloaded installer and guide the user through
    /// the unsigned-build Gatekeeper bypass.
    /// </summary>
    private void ApplyManualInstaller(string installerPath)
    {
        if (OperatingSystem.IsWindows())
        {
            StatusText = "Download complete. Launching installer...";
            _urlLauncher.OpenFileOrDirectory(installerPath);
            ShutdownApplication();
            return;
        }

        _urlLauncher.OpenFileOrDirectory(installerPath);
        StatusText = "Update downloaded — the installer is opening. Quit Lunima and drag the new "
            + "version into Applications. It's unsigned, so on first launch right-click it and "
            + "choose Open to get past macOS's \"developer cannot be verified\" warning.";
    }

    /// <summary>Opens the GitHub releases page for the available release in the default browser.</summary>
    private void OpenReleasesPageInBrowser()
    {
        StatusText = "Opening GitHub releases page in browser...";
        try
        {
            var releaseUrl = $"https://github.com/aignermax/Lunima/releases/tag/{_availableRelease!.TagName}";
            _urlLauncher.Open(releaseUrl);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open browser: {ex.Message}";
        }
    }

    /// <summary>
    /// Persists this version as skipped so the user is not prompted again.
    /// </summary>
    [RelayCommand]
    private void SkipThisVersion()
    {
        if (_availableRelease?.ParsedVersion == null) return;

        _preferences.SetSkippedUpdateVersion(_availableRelease.ParsedVersion);
        UpdateAvailable = false;
        StatusText = $"Version {_availableRelease.ParsedVersion} will not be shown again.";
        _availableRelease = null;
    }

    /// <summary>
    /// Hides the update panel without skipping — the user will be prompted again next time.
    /// </summary>
    [RelayCommand]
    private void RemindLater()
    {
        UpdateAvailable = false;
        StatusText = "Update available — will remind again next check.";
    }

    /// <summary>
    /// Marks today as skipped so the startup notification is suppressed until tomorrow.
    /// </summary>
    [RelayCommand]
    private void SkipForToday()
    {
        _preferences.SkipToday();
        UpdateAvailable = false;
        StatusText = "Update notification suppressed until tomorrow.";
        _availableRelease = null;
    }

    /// <summary>
    /// Runs on app startup: checks for updates non-blockingly and shows the notification
    /// banner only when an update is available and the user has not skipped today or
    /// permanently skipped this version.
    /// </summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (!_preferences.ShouldCheckToday()) return;
        if (IsChecking || IsDownloading) return;

        IsChecking = true;
        try
        {
            var release = await _updateChecker.GetLatestReleaseAsync();
            if (release == null) return;
            if (!UpdateChecker.IsNewerThan(release, _currentVersion)) return;

            var skipped = _preferences.GetSkippedUpdateVersion();
            if (skipped != null && release.ParsedVersion != null && skipped >= release.ParsedVersion) return;

            _availableRelease = release;
            LatestVersionText = $"Update available: v{_currentVersion} → v{release.ParsedVersion}";
            ReleaseNotes = TruncateReleaseNotes(release.Body);
            UpdateAvailable = true;
            StatusText = string.Empty;
        }
        catch
        {
            // Startup check failures are silent — don't disturb the user
        }
        finally
        {
            IsChecking = false;
        }
    }

    private static SemanticVersion ResolveCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
                      ?? Assembly.GetExecutingAssembly().GetName().Version;

        if (version == null) return new SemanticVersion(0, 1, 0);
        return new SemanticVersion(version.Major, version.Minor, version.Build);
    }

    private static string TruncateReleaseNotes(string notes)
    {
        const int MaxLength = 800;
        if (notes.Length <= MaxLength) return notes;
        return notes[..MaxLength] + "\n\n[... see full release notes on GitHub]";
    }

    private static void ShutdownApplication()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
