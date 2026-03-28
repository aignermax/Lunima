using CAP_Core.Export;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export;

/// <summary>
/// ViewModel for GDS coordinate extraction debugging tool.
/// Allows users to run the Python gdspy extraction script on a GDS file
/// and view the resulting coordinate summary for diagnosing pin/waveguide
/// alignment issues (issue #329).
/// </summary>
public partial class GdsCoordExtractViewModel : ObservableObject
{
    private readonly GdsCoordinateExtractor _extractor;

    [ObservableProperty]
    private string _gdsFilePath = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isExtracting;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private string _outputJsonPath = string.Empty;

    /// <summary>Initializes a new instance of <see cref="GdsCoordExtractViewModel"/>.</summary>
    /// <param name="extractor">Core coordinate extraction service.</param>
    public GdsCoordExtractViewModel(GdsCoordinateExtractor extractor)
    {
        _extractor = extractor;
    }

    /// <summary>
    /// Runs the coordinate extraction script on the configured GDS file.
    /// Outputs a JSON file with all polygon and path coordinates for debugging.
    /// </summary>
    [RelayCommand]
    public async Task ExtractCoordinatesAsync()
    {
        if (string.IsNullOrWhiteSpace(GdsFilePath))
        {
            StatusText = "Please specify a GDS file path.";
            return;
        }

        IsExtracting = true;
        HasResult = false;
        StatusText = "Extracting coordinates...";
        ResultSummary = string.Empty;
        OutputJsonPath = string.Empty;

        try
        {
            var result = await _extractor.ExtractAsync(GdsFilePath);

            if (result.Success)
            {
                HasResult = true;
                OutputJsonPath = result.JsonOutputPath ?? string.Empty;
                StatusText = result.Status;
                ResultSummary = BuildSummary(result.JsonContent);
            }
            else
            {
                StatusText = $"Failed: {result.ErrorMessage}";
            }
        }
        finally
        {
            IsExtracting = false;
        }
    }

    /// <summary>
    /// Sets the GDS file path and resets result state.
    /// Called when the user browses for a GDS file.
    /// </summary>
    /// <param name="path">Absolute path to the GDS file.</param>
    public void SetGdsFilePath(string path)
    {
        GdsFilePath = path;
        HasResult = false;
        StatusText = string.Empty;
        ResultSummary = string.Empty;
        OutputJsonPath = string.Empty;
    }

    /// <summary>
    /// Sets a custom Python executable path for the extractor.
    /// </summary>
    /// <param name="pythonPath">Path to Python executable, or null for system default.</param>
    public void SetCustomPythonPath(string? pythonPath)
    {
        _extractor.SetCustomPythonPath(pythonPath);
    }

    private static string BuildSummary(string? jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            return "No data";

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            var dbUnit = root.TryGetProperty("database_unit", out var u) ? u.GetDouble() : 0;
            var cells = root.TryGetProperty("cells", out var c) ? c.EnumerateObject().Count() : 0;

            int totalPolygons = 0, totalPaths = 0;
            if (root.TryGetProperty("cells", out var cellsElem))
            {
                foreach (var cell in cellsElem.EnumerateObject())
                {
                    if (cell.Value.TryGetProperty("polygons", out var polys))
                        totalPolygons += polys.GetArrayLength();
                    if (cell.Value.TryGetProperty("paths", out var paths))
                        totalPaths += paths.GetArrayLength();
                }
            }

            return $"Cells: {cells}  |  Polygons: {totalPolygons}  |  Paths: {totalPaths}  |  DB unit: {dbUnit:e1} m";
        }
        catch
        {
            return "Extracted (could not parse summary)";
        }
    }
}
