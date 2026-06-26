using Avalonia.Controls.ApplicationLifetimes;
using CAP.Avalonia.Services;
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
        IUrlLauncher urlLauncher)
    {
        _updateChecker = updateChecker;
        _downloader = downloader;
        _preferences = preferences;
        _urlLauncher = urlLauncher;
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
    /// Downloads the platform installer from the available release. On Windows it runs the MSI
    /// and shuts the app down so the installer can replace it; on macOS/Linux it opens the
    /// downloaded installer for the user to complete the install and leaves the app running.
    /// </summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_availableRelease == null || IsDownloading) return;

        // Platform-aware: .dmg on macOS, .msi on Windows, .tar.gz on Linux.
        // (FindMsiAsset is platform-blind and would hand macOS the Windows .msi — see #610.)
        var installerAsset = UpdateChecker.FindPlatformAsset(_availableRelease);
        if (installerAsset == null)
        {
            // No installer for this platform - open GitHub releases page in browser
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

            var installerPath = await _downloader.DownloadMsiAsync(
                installerAsset.BrowserDownloadUrl, installerAsset.Size, progress);

            if (OperatingSystem.IsWindows())
            {
                // Windows: run the MSI, which needs the running app to close so it can replace it.
                StatusText = "Download complete. Launching installer...";
                UpdateDownloader.LaunchInstaller(installerPath);
                ShutdownApplication();
            }
            else
            {
                // macOS/Linux: open the .dmg/.tar.gz via the PATH-safe launcher and leave the app
                // running — the user completes the install manually. Auto-quitting here (and the
                // bare `open` that has no shell PATH under a Finder/Dock launch) is what made the
                // previous flow read as a crash (#610).
                _urlLauncher.OpenFileOrDirectory(installerPath);
                // Builds are unsigned (no Apple Developer ID yet), so first launch trips Gatekeeper.
                // Guide the user through the right-click → Open bypass rather than leaving them at the wall.
                StatusText = "Update downloaded — the installer is opening. Quit Lunima and drag the new "
                    + "version into Applications. It's unsigned, so on first launch right-click it and "
                    + "choose Open to get past macOS's \"developer cannot be verified\" warning.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
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
