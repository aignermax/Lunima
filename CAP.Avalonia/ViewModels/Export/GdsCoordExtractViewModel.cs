using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Export.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export;

/// <summary>
/// ViewModel for GDS coordinate extraction debugging tool.
/// Allows users to run the Python gdspy extraction script on a GDS file
/// and view the resulting coordinate summary for diagnosing pin/waveguide
/// alignment issues (issue #329).
/// </summary>
public partial class GdsCoordExtractViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ShellWorkScope _work = new();
    private int _revision;
    private AsyncCancellationScope? _request;

    private void InvalidateRequest()
    {
        _revision++;
        _request?.Cancel();
    }
    private bool IsCurrent(int revision) => !_work.IsStopping && revision == _revision;
    partial void OnGdsFilePathChanging(string value) => InvalidateRequest();
    public Task StopAsync() => _work.StopAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
    private readonly IGdsCoordinateExtractor _extractor;

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
    public GdsCoordExtractViewModel(IGdsCoordinateExtractor extractor, ApplicationWorkCoordinator? applicationWork = null)
    {
        _extractor = extractor;
        _work.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>
    /// Runs the coordinate extraction script on the configured GDS file.
    /// Outputs a JSON file with all polygon and path coordinates for debugging.
    /// </summary>
    [RelayCommand]
    public Task ExtractCoordinatesAsync() => _work.RunAsync(ExtractCoordinatesCoreAsync);

    private async Task ExtractCoordinatesCoreAsync(CancellationToken cancellationToken)
    {
        InvalidateRequest();
        var revision = _revision;
        var path = GdsFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = LocalizationService.Instance.Translate("Export.CoordExtract.SpecifyPath");
            return;
        }

        await using var request = new AsyncCancellationScope(cancellationToken);
        _request = request;
        try
        {
            IsExtracting = true;
            if (!IsCurrent(revision)) return;
            HasResult = false;
            if (!IsCurrent(revision)) return;
            StatusText = LocalizationService.Instance.Translate("Export.CoordExtract.Extracting");
            if (!IsCurrent(revision)) return;
            ResultSummary = string.Empty;
            if (!IsCurrent(revision)) return;
            OutputJsonPath = string.Empty;
            if (!IsCurrent(revision)) return;

            var result = await _extractor.ExtractAsync(path, cancellationToken: request.Token);
            if (!IsCurrent(revision)) return;

            if (result.Success)
            {
                OutputJsonPath = result.JsonOutputPath ?? string.Empty;
                if (!IsCurrent(revision)) return;
                StatusText = result.Status;
                if (!IsCurrent(revision)) return;
                ResultSummary = BuildSummary(result.JsonContent);
                if (!IsCurrent(revision)) return;
                HasResult = true;
            }
            else
            {
                StatusText = string.Format(
                    LocalizationService.Instance.Translate("Export.CoordExtract.Failed"), result.ErrorMessage);
            }
        }
        catch (OperationCanceledException) when (!IsCurrent(revision)) { }
        finally
        {
            if (ReferenceEquals(_request, request))
            {
                _request = null;
                IsExtracting = false;
            }
        }
    }

    /// <summary>
    /// Sets the GDS file path and resets result state.
    /// Called when the user browses for a GDS file.
    /// </summary>
    /// <param name="path">Absolute path to the GDS file.</param>
    public void SetGdsFilePath(string path)
    {
        if (_work.IsStopping) return;
        var revision = _revision + 1;
        if (GdsFilePath == path) InvalidateRequest();
        else GdsFilePath = path; // OnChanging revokes the old request before observers run.
        if (!IsCurrent(revision)) return;
        HasResult = false;
        if (!IsCurrent(revision)) return;
        StatusText = string.Empty;
        if (!IsCurrent(revision)) return;
        ResultSummary = string.Empty;
        if (!IsCurrent(revision)) return;
        OutputJsonPath = string.Empty;
    }

    /// <summary>
    /// Sets a custom Python executable path for the extractor.
    /// </summary>
    /// <param name="pythonPath">Path to Python executable, or null for system default.</param>
    public void SetCustomPythonPath(string? pythonPath)
    {
        if (_work.IsStopping) return;
        InvalidateRequest();
        _extractor.SetCustomPythonPath(pythonPath);
    }

    private static string BuildSummary(string? jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            return LocalizationService.Instance.Translate("Export.CoordExtract.NoData");

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
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

            return string.Format(
                LocalizationService.Instance.Translate("Export.CoordExtract.Summary"),
                cells, totalPolygons, totalPaths, dbUnit.ToString("e1"));
        }
        catch
        {
            return LocalizationService.Instance.Translate("Export.CoordExtract.ParseError");
        }
    }
}
