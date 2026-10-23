using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Services.Localization;
using System.Globalization;

namespace CAP.Avalonia.ViewModels.Export.DesignFiles;

/// <summary>Exports each fabrication die and aggregates script/GDS outcomes.</summary>
public sealed class DieExportWorkflow
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly SimpleNazcaExporter _nazcaExporter;
    private readonly IEnumerable<ComponentTemplate> _componentLibrary;
    private readonly GdsExportViewModel GdsExport;
    private readonly ErrorConsoleService? _errorConsole;
    private readonly NazcaExportWorkflow _nazcaWorkflow;
    /// <summary>Creates per-die export with the shared Nazca result presentation.</summary>
    public DieExportWorkflow(DesignCanvasViewModel canvas, SimpleNazcaExporter exporter,
        IEnumerable<ComponentTemplate> library, GdsExportViewModel gdsExport,
        ErrorConsoleService? errors, NazcaExportWorkflow nazcaWorkflow)
    {
        _canvas = canvas;
        _nazcaExporter = exporter;
        _componentLibrary = library;
        GdsExport = gdsExport;
        _errorConsole = errors;
        _nazcaWorkflow = nazcaWorkflow;
    }
    /// <summary>Writes uniquely named die-local scripts and optional GDS files.</summary>
    public async Task ExecuteAsync(string basePath, ComponentGroup[] dies, long exportRequestId,
        Func<CAP_Core.Routing.MetalRouting.MetalRoutingSpec>? metalSpecProvider, IMessageBoxService? messageBox,
        Func<Type?, Task>? showSettingsWindow, Action<string>? updateStatus)
    {
        var localization = LocalizationService.Instance;
        var directory = Path.GetDirectoryName(basePath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(basePath);
        var written = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var skippedConnections = new List<string>();
        var failures = new List<string>();
        foreach (var die in dies)
        {
            var name = $"{stem}_{SimpleNazcaExporter.DieCellName(die)}";
            var uniqueName = name;
            for (var suffix = 2; !usedNames.Add(uniqueName); suffix++)
                uniqueName = $"{name}_{suffix}";
            var path = Path.Combine(directory, uniqueName + ".py");
            try
            {
                var script = _nazcaExporter.ExportAssembly(_canvas, die, metalSpecProvider?.Invoke(),
                    skippedConnections: skippedConnections, library: _componentLibrary, exportWarnings: warnings);
                await File.WriteAllTextAsync(path, script);
                written.Add(path);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                _errorConsole?.LogError($"Die export failed: {failures[^1]}", ex);
            }
        }
        foreach (var warning in warnings) _errorConsole?.LogWarning(warning);
        var skippedWarning = ExportWarningMessages.BuildSkipped(skippedConnections);
        if (skippedWarning != null) _errorConsole?.LogWarning(skippedWarning);
        var sourceWarning = ExportWarningMessages.BuildMissingGdsSources(warnings);
        var gdsCount = 0;
        var gdsSkipped = !GdsExport.GenerateGdsEnabled;
        try
        {
            if (written.Count > 0)
            {
                if (GdsExport.GenerateGdsEnabled && !GdsExport.IsEnvironmentReady)
                    await GdsExport.CheckEnvironmentAsync();
                var decision = await GdsExport.PreflightGdsAsync(messageBox);
                gdsSkipped |= decision != GdsPreflightDecision.Proceed;
                if (decision != GdsPreflightDecision.Proceed)
                    await _nazcaWorkflow.HandleSkippedGdsAsync(decision, written[0], showSettingsWindow, updateStatus);
                if (!gdsSkipped)
                {
                    foreach (var path in written)
                    {
                        try
                        {
                            var result = await GdsExport.ExportScriptToGdsAsync(path, exportRequestId);
                            if (result.Success && !string.IsNullOrWhiteSpace(result.GdsPath)) gdsCount++;
                            else
                            {
                                var failure = $"{Path.GetFileName(path)}: {result.ErrorMessage ?? result.Status}";
                                failures.Add(failure);
                                _errorConsole?.LogError($"GDS generation failed: {failure}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                            _errorConsole?.LogError($"GDS generation failed: {failures[^1]}", ex);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(ex.Message);
            _errorConsole?.LogError($"Die export failed: {ex.Message}", ex);
        }
        var status = string.Format(CultureInfo.CurrentCulture, localization.Translate("Dies.Export.Summary"), written.Count, gdsCount);
        if (gdsSkipped) status += localization.Translate("Dies.Export.GdsSkipped");
        if (failures.Count > 0) status += string.Format(CultureInfo.CurrentCulture, localization.Translate("Dies.Export.Failures"), failures.Count, string.Join("; ", failures));
        updateStatus?.Invoke(NazcaExportWorkflow.WithWarnings(status, skippedWarning, sourceWarning));
    }

}
