using CAP.Avalonia.Services;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// "One file per die" export: each die of an assembly as its own Nazca script (and GDS when
/// Nazca is available), in die-local coordinates — what goes to each die's foundry.
/// </summary>
public partial class FileOperationsViewModel
{
    /// <summary>
    /// Writes <c>&lt;name&gt;_&lt;die&gt;.py</c> next to the chosen file for every die and renders
    /// each to GDS. No-op with a status hint in a single-die design.
    /// </summary>
    [RelayCommand]
    private async Task ExportDieFiles()
    {
        var exportRequestId = GdsExport.BeginExportRequest();
        var dies = _canvas.Dies.RootDies;
        if (dies.Count == 0)
        {
            UpdateStatus?.Invoke(Services.Localization.LocalizationService.Instance.Translate("Dies.Export.NoDies"));
            return;
        }

        var basePath = await FileDialogService.ShowSaveFileDialogAsync(
            "Export each die to its own Nazca file", "py", "Python Files|*.py|All Files|*.*");
        if (basePath == null)
            return;

        var directory = Path.GetDirectoryName(basePath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(basePath);
        var written = new List<string>();
        foreach (var die in dies)
        {
            var path = Path.Combine(directory, $"{stem}_{SimpleNazcaExporter.DieCellName(die)}.py");
            var script = _nazcaExporter.ExportAssembly(_canvas, die, MetalRoutingSpecProvider?.Invoke(),
                library: _componentLibrary);
            await File.WriteAllTextAsync(path, script);
            written.Add(Path.GetFileName(path));
            if (GdsExport.GenerateGdsEnabled && GdsExport.IsEnvironmentReady)
            {
                var result = await GdsExport.ExportScriptToGdsAsync(path, exportRequestId);
                if (!result.Success)
                    _errorConsole?.LogError($"GDS generation failed for {Path.GetFileName(path)}: {result.ErrorMessage}");
            }
        }
        UpdateStatus?.Invoke($"Exported {written.Count} die file(s): {string.Join(", ", written)}");
    }
}
