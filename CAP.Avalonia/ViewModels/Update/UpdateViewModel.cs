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
/// Handles checking GitHub releases for newer versions, downloading the update artifact,
/// and performing an in-place self-update on all platforms.
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
    /// <param name="updateChecker">GitHub release checker.</param>
    /// <param name="downloader">Installer/artifact downloader.</param>
    /// <param name="preferences">User preferences (skip version, skip today).</param>
    /// <param name="urlLauncher">Fallback URL/file launcher used when in-place install is unavailable.</param>
    /// <param name="installer">In-place installer. Defaults to the platform-appropriate implementation.</param>
    public UpdateViewModel(
        UpdateChecker updateChecker,
        UpdateDownloader downloader,
        UserPreferencesService preferences,
        IUrlLauncher urlLauncher,
        IInstaller? installer = null)
    {
        _updateChecker = updateChecker;
        _downloader = downloader;
        _preferences = preferences;
        _urlLauncher = urlLauncher;
        _installer = installer ?? InstallerFactory.Create();
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
    /// Downloads the platform-appropriate auto-update artifact and performs an in-place
    /// self-update: the new version is installed over the current one and the application
    /// relaunches automatically. Falls back to opening the releases page when in-place
    /// installation is not possible (e.g. install location not writable).
    /// </summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_availableRelease == null || IsDownloading) return;

        // Prefer the auto-update artifact (zip/msi/tar.gz); fall back to manual-installer asset.
        var autoUpdateAsset = UpdateChecker.FindAutoUpdateAsset(_availableRelease)
                              ?? UpdateChecker.FindPlatformAsset(_availableRelease);

        if (autoUpdateAsset == null)
        {
            StatusText = "Opening GitHub releases page in browser...";
            try
            {
                var releaseUrl = $"https://github.com/aignermax/Lunima/releases/tag/{_availableRelease.TagName}";
                _urlLauncher.Open(releaseUrl);
            }
            catch (Exception ex)
            {
                StatusText = $"Could not open browser: {ex.Message}";
            }
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        StatusText = "Downloading update...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusText = $"Downloading... {p:P0}";
            });

            var downloadedPath = await _downloader.DownloadInstallerAsync(
                autoUpdateAsset.BrowserDownloadUrl, autoUpdateAsset.Size, progress);

            StatusText = "Download complete. Preparing in-place update...";

            if (_installer.CanInstall(downloadedPath, out var reason))
            {
                StatusText = "Installing update — relaunching shortly...";
                await _installer.InstallAndRelaunchAsync(downloadedPath);
                ShutdownApplication();
            }
            else
            {
                // Pre-flight failed — fall back to opening the file manually
                StatusText = $"Cannot auto-install ({reason}). Opening installer...";
                _urlLauncher.OpenFileOrDirectory(downloadedPath);

                if (OperatingSystem.IsWindows())
                    ShutdownApplication();
            }
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
