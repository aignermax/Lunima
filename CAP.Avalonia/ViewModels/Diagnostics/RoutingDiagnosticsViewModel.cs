using System.Text;
using CAP_Core;
using CAP_Core.Routing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// ViewModel for the routing diagnostics UI panel.
/// Validates all routed connections and reports issues like bend radius violations,
/// segment gaps, and blocked fallback paths.
/// </summary>
public partial class RoutingDiagnosticsViewModel : ObservableObject
{
    private readonly ErrorConsoleService? _errorConsole;

    /// <summary>Initializes a new instance of <see cref="RoutingDiagnosticsViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public RoutingDiagnosticsViewModel(ErrorConsoleService? errorConsole = null)
    {
        _errorConsole = errorConsole;
    }
    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _resultText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _totalConnections;

    [ObservableProperty]
    private int _validConnections;

    [ObservableProperty]
    private int _issueCount;

    [ObservableProperty]
    private string _exportedJsonPath = "";

    private DesignCanvasViewModel? _canvas;

    /// <summary>
    /// File dialog service for JSON export.
    /// </summary>
    public Services.IFileDialogService? FileDialogService { get; set; }

    /// <summary>
    /// Configures the diagnostics panel with the current canvas.
    /// </summary>
    public void Configure(DesignCanvasViewModel? canvas)
    {
        _canvas = canvas;
    }

    /// <summary>
    /// Runs diagnostics on all routed connections.
    /// </summary>
    [RelayCommand]
    private void RunDiagnostics()
    {
        if (_canvas == null || IsAnalyzing) return;

        IsAnalyzing = true;
        StatusText = "Analyzing routes...";
        ResultText = "";
        ExportedJsonPath = "";

        try
        {
            var connections = _canvas.Connections;
            TotalConnections = connections.Count;

            if (TotalConnections == 0)
            {
                StatusText = "No connections to analyze";
                ValidConnections = 0;
                IssueCount = 0;
                return;
            }

            double minBendRadius = _canvas.Router?.MinBendRadiusMicrometers ?? 10.0;
            var diagnostics = new RoutingDiagnostics(minBendRadius);
            var sb = new StringBuilder();
            int valid = 0;
            int totalIssues = 0;

            foreach (var conn in connections)
            {
                var path = conn.Connection.RoutedPath;
                if (path == null)
                {
                    sb.AppendLine($"  {GetConnectionLabel(conn)}: No path");
                    totalIssues++;
                    continue;
                }

                var report = diagnostics.Validate(path);
                bool isOk = report.IsValid && !path.IsBlockedFallback;

                if (isOk) valid++;

                string status = isOk ? "OK" : "ISSUES";
                sb.AppendLine($"  {GetConnectionLabel(conn)}: {status}");
                sb.AppendLine($"    Length: {path.TotalLengthMicrometers:F1}µm");
                sb.AppendLine($"    Segments: {path.Segments.Count}");
                sb.AppendLine($"    Bends: {path.TotalEquivalent90DegreeBends:F1}×90°");

                if (path.IsBlockedFallback)
                    sb.AppendLine("    ⚠ Blocked fallback");
                if (path.IsInvalidGeometry)
                    sb.AppendLine("    ⚠ Invalid geometry");

                foreach (var issue in report.Issues)
                {
                    sb.AppendLine($"    [{issue.Severity}] {issue.Message}");
                    totalIssues++;
                }

                sb.AppendLine();
            }

            ValidConnections = valid;
            IssueCount = totalIssues;
            ResultText = sb.ToString();
            StatusText = $"Done: {valid}/{TotalConnections} valid, {totalIssues} issues";
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Routing diagnostics failed: {ex.Message}", ex);
            StatusText = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    /// <summary>
    /// Exports all routed paths to a JSON file for external visualization.
    /// </summary>
    [RelayCommand]
    private async Task ExportPathsJson()
    {
        if (_canvas == null || _canvas.Connections.Count == 0)
        {
            StatusText = "No connections to export";
            return;
        }

        try
        {
            var paths = new Dictionary<string, RoutedPath>();
            foreach (var conn in _canvas.Connections)
            {
                if (conn.Connection.RoutedPath != null)
                {
                    paths[GetConnectionLabel(conn)] = conn.Connection.RoutedPath;
                }
            }

            var json = RoutedPathSerializer.ToJson(paths);

            string? filePath = null;
            if (FileDialogService != null)
            {
                filePath = await FileDialogService.ShowSaveFileDialogAsync(
                    "Export Routed Paths",
                    "json",
                    "JSON Files|*.json|All Files|*.*");
            }

            if (filePath == null)
            {
                StatusText = "Export cancelled";
                return;
            }

            await File.WriteAllTextAsync(filePath, json);
            ExportedJsonPath = filePath;
            StatusText = $"Exported {paths.Count} paths to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Failed to export routing paths: {ex.Message}", ex);
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Callback to copy text to the system clipboard.
    /// Set by the View code-behind since clipboard access requires a TopLevel reference.
    /// </summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>
    /// Copies all routed paths as JSON to the clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyPathsJsonToClipboard()
    {
        if (_canvas == null || _canvas.Connections.Count == 0)
        {
            StatusText = "No connections to copy";
            return;
        }

        try
        {
            var paths = new Dictionary<string, RoutedPath>();
            foreach (var conn in _canvas.Connections)
            {
                if (conn.Connection.RoutedPath != null)
                {
                    paths[GetConnectionLabel(conn)] = conn.Connection.RoutedPath;
                }
            }

            var json = RoutedPathSerializer.ToJson(paths);

            // Copy to clipboard via callback
            if (CopyToClipboard != null)
            {
                await CopyToClipboard(json);
                StatusText = $"Copied {paths.Count} paths to clipboard";
            }
            else
            {
                StatusText = "Clipboard not available";
            }
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Failed to copy routing paths to clipboard: {ex.Message}", ex);
            StatusText = $"Copy failed: {ex.Message}";
        }
    }

    private static string GetConnectionLabel(WaveguideConnectionViewModel conn)
    {
        var start = conn.Connection.StartPin;
        var end = conn.Connection.EndPin;
        var startId = start.ParentComponent?.Identifier ?? "?";
        var endId = end.ParentComponent?.Identifier ?? "?";
        return $"{startId}.{start.Name} -> {endId}.{end.Name}";
    }
}
