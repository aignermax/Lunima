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
            LatestVersionText = $"New version: v{releaseVersion}";
            ReleaseNotes = TruncateReleaseNotes(release.Body);
            UpdateAvailable = true;
            StatusText = $"Update available: v{releaseVersion}";
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
    /// Downloads the MSI from the available release and launches the installer,
    /// then shuts down the application.
    /// </summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_availableRelease == null || IsDownloading) return;

        var msiAsset = UpdateChecker.FindMsiAsset(_availableRelease);
        if (msiAsset == null)
        {
            // No MSI found - open GitHub releases page in browser
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

            var msiPath = await _downloader.DownloadMsiAsync(
                msiAsset.BrowserDownloadUrl, msiAsset.Size, progress);

            StatusText = "Download complete. Launching installer...";
            UpdateDownloader.LaunchInstaller(msiPath);

            ShutdownApplication();
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
