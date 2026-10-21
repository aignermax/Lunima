using System.Globalization;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Export;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>Exports each fabrication die as its own Nazca script and optional GDS.</summary>
public partial class FileOperationsViewModel
{
    /// <summary>Writes separate, uniquely named files in die-local coordinates.</summary>
    [RelayCommand]
    private async Task ExportDieFiles()
    {
        var localization = LocalizationService.Instance;
        var exportRequestId = GdsExport.BeginExportRequest();
        var dies = _canvas.Dies.RootDies.ToArray();
        if (dies.Length == 0)
        {
            UpdateStatus?.Invoke(localization.Translate("Dies.Export.NoDies"));
            return;
        }
        if (FileDialogService == null || !await ConfirmNazcaExportDropsGdsFactoryComponentsAsync())
            return;

        var basePath = await FileDialogService.ShowSaveFileDialogAsync(
            localization.Translate("Dies.Export.DialogTitle"), "py", "Python Files|*.py|All Files|*.*");
        if (basePath == null) return;

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
                var script = _nazcaExporter.ExportAssembly(_canvas, die, MetalRoutingSpecProvider?.Invoke(),
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
                var decision = await GdsExport.PreflightGdsAsync(MessageBoxService);
                gdsSkipped |= decision != GdsPreflightDecision.Proceed;
                if (decision != GdsPreflightDecision.Proceed)
                    await HandleSkippedGdsAsync(decision, written[0]);
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
        UpdateStatus?.Invoke(WithWarnings(status, skippedWarning, sourceWarning));
    }
}
