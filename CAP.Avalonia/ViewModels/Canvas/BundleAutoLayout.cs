using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// What switching a bundle to Auto lays out: the route of every bundle connection (the others are
/// left to the general router) and, when the bundle shares a length lock that no layout could
/// meet, why — so the routes never fall off the lock without the user being told.
/// </summary>
/// <param name="Routes">The laid-out route per connection.</param>
/// <param name="LockMiss">Set when some routes miss the bundle's shared length lock.</param>
internal sealed record BundleAutoLayout(
    IReadOnlyDictionary<WaveguideConnection, RoutedPath> Routes,
    BundleLockMiss? LockMiss = null);

/// <summary>
/// A shared length lock Auto could not keep for every route of a bundle. The lock itself stays:
/// the missed routes show a violated padlock until the group is re-equalized or released.
/// </summary>
/// <param name="TargetMicrometers">The locked length.</param>
/// <param name="ArchMinimumMicrometers">The shortest common length nested arches allow (the outer arch).</param>
/// <param name="MissedCount">Routes off the lock.</param>
/// <param name="BundleCount">Routes in the bundle.</param>
internal sealed record BundleLockMiss(double TargetMicrometers, double ArchMinimumMicrometers, int MissedCount, int BundleCount)
{
    /// <summary>The localized status line: the lock, how many routes miss it, the reason and the way out.</summary>
    public string Describe()
    {
        string key = TargetMicrometers < ArchMinimumMicrometers
            ? "Routing.Connection.AutoLockMissed"
            : "Routing.Connection.AutoLockMissedNoRoom";
        return string.Format(CultureInfo.CurrentCulture, LocalizationService.Instance.Translate(key),
            TargetMicrometers, MissedCount, BundleCount, ArchMinimumMicrometers);
    }
}
