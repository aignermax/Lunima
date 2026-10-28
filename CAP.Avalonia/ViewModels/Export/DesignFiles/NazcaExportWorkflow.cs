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

namespace CAP.Avalonia.ViewModels.Export.DesignFiles;

/// <summary>Writes Nazca designs and coordinates optional GDS generation and result presentation.</summary>
public sealed class NazcaExportWorkflow
{
    private readonly SimpleNazcaExporter _nazcaExporter;
    private readonly DesignCanvasViewModel _canvas;
    private readonly IEnumerable<ComponentTemplate> _componentLibrary;
    private readonly ErrorConsoleService? _errorConsole;
    private readonly IUrlLauncher _urlLauncher;
    private readonly GdsExportViewModel GdsExport;
    /// <summary>Creates the script and GDS export workflow.</summary>
    public NazcaExportWorkflow(SimpleNazcaExporter exporter, DesignCanvasViewModel canvas,
        IEnumerable<ComponentTemplate> library, GdsExportViewModel gdsExport,
        ErrorConsoleService? errors, IUrlLauncher urlLauncher)
    {
        _nazcaExporter = exporter;
        _canvas = canvas;
        _componentLibrary = library;
        GdsExport = gdsExport;
        _errorConsole = errors;
        _urlLauncher = urlLauncher;
    }
    /// <summary>Writes a selected script and presents the optional GDS generation result.</summary>
    public async Task ExportAsync(string filePath, long exportRequestId,
        Func<CAP_Core.Routing.MetalRouting.MetalRoutingSpec>? metalSpecProvider, string? designPath,
        IMessageBoxService? messageBox, Func<Type?, Task>? showSettingsWindow, Action<string>? updateStatus)
    {
        try
        {
            // Collected by the exporter as a side effect of writing the script below —
            // connections/frozen paths whose route is a placeholder or invalid never render
            // as GDS geometry (a self-crossing fallback has no optical model; invalid
            // geometry violates the bend radius); connections whose sibling-crossing flag no
            // bridge marker resolves still render but deserve a second look. Reading both
            // AFTER the write (rather than recomputing from a live canvas snapshot
            // beforehand) guarantees the report matches exactly what landed in the script,
            // even while background routing is still in flight.
            var skippedConnectionsList = new List<string>();
            var unresolvedCrossingsList = new List<string>();
            var exportWarningsList = new List<string>();
            var nazcaCode = _nazcaExporter.Export(
                _canvas, metalSpec: metalSpecProvider?.Invoke(),
                skippedConnections: skippedConnectionsList, unresolvedCrossings: unresolvedCrossingsList,
                library: _componentLibrary, exportWarnings: exportWarningsList,
                designName: designPath != null ? Path.GetFileNameWithoutExtension(designPath) : null);
            await File.WriteAllTextAsync(filePath, nazcaCode);

            // Raw-code components whose geometry source vanished (a deleted .gds) exported
            // as placeholder boxes — say so plainly instead of silently shipping the stub:
            // each description in full to the (copyable) Error Console, the aggregated
            // count additionally prefixed onto the final status via WithWarnings below,
            // so it is visible without watching the console.
            foreach (var exportWarning in exportWarningsList)
                _errorConsole?.LogWarning(exportWarning);

            var skippedConnectionsWarning = ExportWarningMessages.BuildSkipped(skippedConnectionsList);
            var unresolvedCrossingsWarning = ExportWarningMessages.BuildUnresolvedCrossings(unresolvedCrossingsList);
            var missingSourcesWarning = ExportWarningMessages.BuildMissingGdsSources(exportWarningsList);
            if (skippedConnectionsWarning != null)
                _errorConsole?.LogWarning(skippedConnectionsWarning);
            if (unresolvedCrossingsWarning != null)
                _errorConsole?.LogWarning(unresolvedCrossingsWarning);

            // GDS pre-flight: refresh a stale "not ready" verdict once, then ask the
            // user how to proceed when Nazca is genuinely unavailable.
            if (GdsExport.GenerateGdsEnabled && !GdsExport.IsEnvironmentReady)
                await GdsExport.CheckEnvironmentAsync();

            var decision = await GdsExport.PreflightGdsAsync(messageBox);
            if (decision != GdsPreflightDecision.Proceed)
            {
                await HandleSkippedGdsAsync(
                    decision, filePath, showSettingsWindow, updateStatus, skippedConnectionsWarning, unresolvedCrossingsWarning,
                    missingSourcesWarning);
                return;
            }

            // Attempt GDS generation if enabled
            var result = await GdsExport.ExportScriptToGdsAsync(filePath, exportRequestId);

            if (result.Success && result.GdsPath != null)
            {
                updateStatus?.Invoke(WithWarnings(
                    $"Exported {Path.GetFileName(filePath)} and {Path.GetFileName(result.GdsPath)}",
                    skippedConnectionsWarning, unresolvedCrossingsWarning, missingSourcesWarning));

                // Try to open the generated GDS file in the default viewer (KLayout etc.) —
                // this is a content launch, not a file-manager open, so it stays useful even
                // when the user runs many exports back-to-back.
                TryOpenFileWithDefaultApp(result.GdsPath);
            }
            else if (result.Success)
            {
                updateStatus?.Invoke(WithWarnings(
                    $"Exported to {Path.GetFileName(filePath)}",
                    skippedConnectionsWarning, unresolvedCrossingsWarning, missingSourcesWarning));
            }
            else
            {
                // Log full Python error to Error Console for visibility
                _errorConsole?.LogError($"GDS generation failed: {result.ErrorMessage}");
                updateStatus?.Invoke(WithWarnings(
                    $"Exported {Path.GetFileName(filePath)} (GDS generation failed: {result.ErrorMessage})",
                    skippedConnectionsWarning, unresolvedCrossingsWarning, missingSourcesWarning));
            }
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Failed to export Nazca design: {ex.Message}", ex);
            updateStatus?.Invoke($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the non-Proceed pre-flight outcomes: the Nazca script is already on disk,
    /// only the GDS step is skipped. Install/settings choices open the Settings window on
    /// the Python-Environments page (the install progress is visible there).
    /// </summary>
    public async Task HandleSkippedGdsAsync(
        GdsPreflightDecision decision, string scriptPath,
        Func<Type?, Task>? showSettingsWindow, Action<string>? updateStatus,
        string? skippedConnectionsWarning = null, string? unresolvedCrossingsWarning = null,
        string? missingSourcesWarning = null)
    {
        if (decision == GdsPreflightDecision.InstallRequested)
        {
            GdsExport.InstallNazcaCommand.Execute(null);
            if (showSettingsWindow != null)
                await showSettingsWindow(typeof(Settings.PythonEnvironmentsSettingsPage));
            updateStatus?.Invoke(WithWarnings(
                $"Exported {Path.GetFileName(scriptPath)} — GDS skipped (installing Nazca)",
                skippedConnectionsWarning, unresolvedCrossingsWarning, missingSourcesWarning));
            return;
        }

        if (decision == GdsPreflightDecision.OpenSettingsRequested
            && showSettingsWindow != null)
            await showSettingsWindow(typeof(Settings.PythonEnvironmentsSettingsPage));

        updateStatus?.Invoke(WithWarnings(
            $"Exported {Path.GetFileName(scriptPath)} — GDS skipped (Nazca not available)",
            skippedConnectionsWarning, unresolvedCrossingsWarning, missingSourcesWarning));
    }

    /// <summary>Prefixes a status line with any non-null warnings, in order, so they survive
    /// next to the final result instead of being scrolled away.</summary>
    public static string WithWarnings(string status, params string?[] warnings)
    {
        var lines = warnings.Where(w => w != null).Append(status);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Attempts to open a file with the system's default application.
    /// If no default app exists, opens the file explorer and selects the file.
    /// </summary>
    /// <param name="filePath">Path to the file to open.</param>
    private void TryOpenFileWithDefaultApp(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return;

            // Open with the default application via the platform launcher.
            // Falls back to revealing in the file manager if no handler is registered.
            try
            {
                _urlLauncher.OpenFileOrDirectory(filePath);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _errorConsole?.LogWarning($"No default app for {Path.GetFileName(filePath)} ({ex.Message}). Falling back to file explorer.");
                OpenFileExplorer(filePath);
            }
        }
        catch (Exception ex)
        {
            _errorConsole?.LogWarning($"Could not open GDS file: {ex.Message}");
        }
    }

    /// <summary>
    /// Reveals the specified file in the system file manager.
    /// Works cross-platform via <see cref="IUrlLauncher.RevealInFileManager"/>.
    /// </summary>
    /// <param name="filePath">Path to the file to reveal.</param>
    private void OpenFileExplorer(string filePath)
    {
        try
        {
            var absolutePath = Path.GetFullPath(filePath);
            _urlLauncher.RevealInFileManager(absolutePath);
        }
        catch (Exception ex)
        {
            _errorConsole?.LogWarning($"Could not open file explorer: {ex.Message}");
        }
    }
    /// <summary>
    /// When the design contains gdsfactory-native components (a Nazca script can't express
    /// them), asks the user whether to export to Nazca anyway (omitting them) or switch to the
    /// gdsfactory export. Returns true to proceed with the Nazca export, false to cancel.
    /// A pure Nazca design (or a headless run with no message box) proceeds without prompting.
    /// </summary>
    public async Task<bool> ConfirmBackendAsync(IMessageBoxService? messageBox,
        Func<Task>? requestGdsFactoryExport, Action<string>? updateStatus)
    {
        var gdsFactory = CAP.Avalonia.Services.GdsFactoryExport.NazcaExportGuard
            .CollectGdsFactoryNativeComponents(_canvas);
        if (gdsFactory.Count == 0 || messageBox == null)
            return true;

        var message =
            $"{gdsFactory.Count} component(s) in this design are gdsfactory-native (e.g. CornerStone "
            + "SiN) and cannot be written to a Nazca script — they would be omitted from the export. "
            + "Use the gdsfactory export to include them.\n\nExport to Nazca anyway?";
        const int switchToGdsFactoryIndex = 0;
        const int exportAnywayIndex = 1;
        var choice = await messageBox.ShowChoicePromptAsync(
            message, "gdsfactory components will be omitted",
            new[] { "Use gdsfactory export instead", "Export to Nazca anyway" });

        if (choice == exportAnywayIndex)
            return true;

        // "Use gdsfactory export instead" — cancel this Nazca export and open the gdsfactory
        // export flow so the user isn't left with nothing happening. A dismissed dialog just cancels.
        if (choice == switchToGdsFactoryIndex && requestGdsFactoryExport != null)
            await requestGdsFactoryExport();
        else
            updateStatus?.Invoke("Nazca export cancelled — use the gdsfactory export for this design.");
        return false;
    }
}
