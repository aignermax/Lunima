using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Process;
using CAP_Core.Routing;
using CAP_DataAccess.Persistence;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Converters;
using CAP.Avalonia.ViewModels.Panels.DesignModels;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

/// <summary>Collects and presents recoverable native design load problems.</summary>
public sealed class NativeLoadDiagnostics
{
    private readonly ErrorConsoleService? _errorConsole;
    /// <summary>Creates the load diagnostic reporter.</summary>
    public NativeLoadDiagnostics(ErrorConsoleService? errorConsole) => _errorConsole = errorConsole;
    internal HashSet<string> PinCalibrationMigrations { get; } = new(StringComparer.Ordinal);
    internal List<string> DisplacedConnections { get; } = new();
    internal List<string> VerticalPortConnections { get; } = new();
    internal List<string> FailedGroups { get; } = new();
    /// <summary>Starts a new diagnostic scope for a design replacement.</summary>
    public void Clear()
    {
        PinCalibrationMigrations.Clear();
        DisplacedConnections.Clear();
        VerticalPortConnections.Clear();
        FailedGroups.Clear();
    }

    /// <summary>
    /// Logs one localized hint per component whose pin calibration changed since the
    /// design was saved; the post-load routing pass re-routes the now path-less connections.
    /// </summary>
    public void ReportPinCalibrationMigrations()
    {
        if (PinCalibrationMigrations.Count == 0)
            return;

        foreach (var name in PinCalibrationMigrations.OrderBy(n => n, StringComparer.Ordinal))
        {
            _errorConsole?.LogWarning(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                Services.Localization.LocalizationService.Instance.Translate("Load.PinCalibrationChanged"),
                name));
        }
        PinCalibrationMigrations.Clear();
    }

    public void ReportVerticalPortConnections(Action<string>? updateStatus)
    {
        if (VerticalPortConnections.Count == 0) return;
        var count = VerticalPortConnections.Count;
        updateStatus?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            Services.Localization.LocalizationService.Instance.Translate("Load.VerticalPortConnectionsDropped"), count));
        _errorConsole?.LogWarning(
            $"{count} connection(s) skipped while loading: vertical fiber ports cannot carry planar waveguides. "
            + "Reconnect using the waveguide port: " + string.Join("; ", VerticalPortConnections));
        VerticalPortConnections.Clear();
    }

    /// <summary>
    /// Surfaces connections that were displaced during the load: a .lun whose netlist
    /// wires one pin more than once loads only the last wire, simulating a different
    /// circuit than the file describes. The status bar carries the localized count and
    /// the error console lists the dropped pin pairs. No dialog, no behavior change —
    /// clean files report nothing.
    /// </summary>
    public void ReportDisplacedConnections(Action<string>? updateStatus)
    {
        if (DisplacedConnections.Count == 0)
            return;

        var count = DisplacedConnections.Count;
        updateStatus?.Invoke(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            Services.Localization.LocalizationService.Instance.Translate("Load.DuplicatePinConnectionsDropped"),
            count));
        _errorConsole?.LogWarning(
            $"{count} connection(s) were dropped while loading because a pin already had a wire "
            + "(the file may have been produced by a newer version or edited externally): "
            + string.Join("; ", DisplacedConnections));
        DisplacedConnections.Clear();
    }

    /// <summary>
    /// Surfaces groups that failed to reconstruct during the load (corrupted or
    /// externally edited references): the status bar carries the localized count and
    /// the error console names each group with the failure reason. Clean files
    /// report nothing.
    /// </summary>
    public void ReportFailedGroups(Action<string>? updateStatus)
    {
        if (FailedGroups.Count == 0)
            return;

        var count = FailedGroups.Count;
        updateStatus?.Invoke(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            Services.Localization.LocalizationService.Instance.Translate("Load.GroupsFailedToRestore"),
            count));
        _errorConsole?.LogWarning(
            $"{count} group(s) could not be restored while loading "
            + "(the file may be corrupted or was edited externally): "
            + string.Join("; ", FailedGroups));
        FailedGroups.Clear();
    }
}
