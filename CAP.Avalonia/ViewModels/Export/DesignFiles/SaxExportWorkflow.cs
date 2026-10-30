using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Export;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Services.Localization;
using System.Globalization;

namespace CAP.Avalonia.ViewModels.Export.DesignFiles;

/// <summary>Coordinates SAX script selection, writing, and outcome presentation.</summary>
public sealed class SaxExportWorkflow
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly SaxExporter _saxExporter;
    private readonly ErrorConsoleService? _errorConsole;
    /// <summary>Creates SAX script export from the current canvas.</summary>
    public SaxExportWorkflow(DesignCanvasViewModel canvas, SaxExporter exporter, ErrorConsoleService? errors)
    {
        _canvas = canvas;
        _saxExporter = exporter;
        _errorConsole = errors;
    }

    /// <summary>
    /// Exports the current design to a SAX/Simphony-compatible Python
    /// simulation script. Historically labelled "PICWave" because issue #474
    /// requested that target — the implementation always emitted sax-based
    /// Python (see <c>SaxScriptWriter</c>). Renamed so the UI label, file
    /// header and status messages all describe the actual output.
    /// </summary>
    public async Task ExecuteAsync(IFileDialogService? fileDialogs, Action<string>? updateStatus)
    {
        if (fileDialogs == null)
        {
            updateStatus?.Invoke("Export not available");
            return;
        }

        if (_canvas.Components.Count == 0)
        {
            updateStatus?.Invoke("Nothing to export - add some components first");
            return;
        }

        var filePath = await fileDialogs.ShowSaveFileDialogAsync(
            "Export to SAX (Simphony) Python",
            "py",
            "Python Files|*.py|All Files|*.*");

        if (filePath == null)
            return;

        try
        {
            var components = _canvas.Components.Select(vm => vm.Component);
            var connections = _canvas.Connections.Select(vm => vm.Connection);
            var script = _saxExporter.Export(components, connections);
            await File.WriteAllTextAsync(filePath, script);
            updateStatus?.Invoke($"Exported SAX script: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Failed to export SAX script: {ex.Message}", ex);
            updateStatus?.Invoke($"SAX export failed: {ex.Message}");
        }
    }
}
