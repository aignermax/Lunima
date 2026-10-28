using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Process;
using CAP_Core.Routing;
using CAP_DataAccess.Persistence;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Export;

namespace CAP.Avalonia.ViewModels.Panels.NativeDesign;

/// <summary>Reconciles die, crossing, analysis and routing state after native topology restoration.</summary>
public sealed class NativeDesignPostLoad
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ErrorConsoleService? _errorConsole;
    /// <summary>Creates post-load canvas reconciliation.</summary>
    public NativeDesignPostLoad(DesignCanvasViewModel canvas, ErrorConsoleService? errorConsole)
    {
        _canvas = canvas;
        _errorConsole = errorConsole;
    }

    /// <summary>
    /// Restores the analysis-output designation (#754) from a loaded design file,
    /// re-anchoring the stored component Identifier to the freshly created component's
    /// runtime id. A stale reference (component renamed/removed outside the app) is
    /// cleared with a warning instead of silently pinning a wrong output.
    /// </summary>
    public void RestoreAnalysisOutput(DesignFileData designData)
    {
        if (designData.AnalysisOutputCoupler == null)
        {
            _canvas.AnalysisOutput.Clear();
            return;
        }

        var coupler = _canvas.Components
            .FirstOrDefault(c => c.Component.Identifier == designData.AnalysisOutputCoupler);
        if (coupler != null)
        {
            _canvas.AnalysisOutput.Designate(coupler.Component.Id);
            return;
        }

        _canvas.AnalysisOutput.Clear();
        _errorConsole?.LogWarning(
            $"Designated analysis output '{designData.AnalysisOutputCoupler}' was not found in the design — designation cleared.");
    }

    /// <summary>
    /// Upgrades legacy multi-chiplet files to dies (linked or multi-process chiplet groups
    /// become dies sized to their content) and bounds placement and routing by the die
    /// assembly when the design has dies. Single-die designs are left untouched.
    /// </summary>
    public void RestoreDieScopeAfterLoad()
    {
        var topLevel = _canvas.Components.Select(c => c.Component).ToList();
        var migrated = CAP_Core.Components.Dies.LegacyDieMigration.Migrate(
            topLevel, _canvas.ConnectionManager.Connections);
        foreach (var vm in _canvas.Components.Where(c => migrated.Contains(c.Component)))
        {
            vm.X = vm.Component.PhysicalX;
            vm.Y = vm.Component.PhysicalY;
            vm.NotifyDimensionsChanged();
        }
        // Every wire sits with the group that owns it; a die's wires live on the assembly.
        _canvas.ApplyWireOwnership();
        if (!_canvas.Dies.IsAssemblyRoot)
            return;
        _canvas.Dies.ApplyScopeBounds();
        _canvas.RefreshFacetLinkPaths();
    }

    /// <summary>
    /// Rebuilds crossing dissolution records for auto-inserted crossings restored
    /// from a saved design (#705). No-op when no crossing service is attached;
    /// enabling adaptive insertion later triggers the same rebuild via the canvas binder.
    /// </summary>
    public void RebuildCrossingRecords()
    {
        // Designs saved before chain crossings carried their flag: adopt them whether or not
        // adaptive insertion is on, so their pieces are no longer frozen.
        CAP_Core.Routing.CrossingInsertion.ChainCrossingMaintenance.AdoptLegacyChainCrossings(
            _canvas.Components.Select(vm => vm.Component), _canvas.ConnectionManager);
        var crossing = _canvas.ConnectionManager.CrossingService;
        if (crossing == null)
            return;

        CAP_Core.Routing.CrossingInsertion.CrossingRecordRebuilder.Rebuild(
            crossing,
            _canvas.ConnectionManager,
            _canvas.Components.Select(vm => vm.Component));
    }

    /// <summary>
    /// Routes every connection the file carried without geometry: hand-written or generated
    /// designs, and routes the pin-calibration migration discarded. The pass runs in the
    /// background like every routing pass, so the design shows at once and the routes fill
    /// in; the incremental router leaves cached routes untouched.
    /// </summary>
    public Task StartPostLoadRouting(int maxConnections)
    {
        int unrouted = _canvas.Connections.Count(c => c.Connection.RoutedPath == null);
        if (unrouted == 0)
        {
            return Task.CompletedTask;
        }
        if (unrouted > maxConnections)
        {
            _errorConsole?.LogInfo(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                Services.Localization.LocalizationService.Instance.Translate("Load.RoutingSkippedLargeDesign"),
                unrouted));
            return Task.CompletedTask;
        }
        return _canvas.RecalculateRoutesAsync();
    }
}
