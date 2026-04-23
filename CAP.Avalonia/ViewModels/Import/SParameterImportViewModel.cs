using CAP_DataAccess.Import;
using CAP_DataAccess.Persistence.PIR;
using CAP.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Import;

/// <summary>
/// ViewModel for the S-parameter import panel.
/// Lets users pick a Lumerical (.sparam/.dat/.txt) or Touchstone (.sNp) file,
/// preview the parsed data, assign it a component identifier, and store it in
/// the active design's PIR S-matrix section.
/// </summary>
public partial class SParameterImportViewModel : ObservableObject
{
    private readonly IReadOnlyList<ISParameterImporter> _importers;
    private Dictionary<string, ComponentSMatrixData>? _storedSMatrices;

    /// <summary>Path of the selected source file.</summary>
    [ObservableProperty]
    private string _filePath = string.Empty;

    /// <summary>Auto-detected format description (e.g. "Lumerical SiEPIC", "Touchstone S2P").</summary>
    [ObservableProperty]
    private string _detectedFormat = string.Empty;

    /// <summary>
    /// Identifier key under which the S-matrix will be stored (e.g. "mmi_2x2_custom").
    /// Must be unique within the design; overwrites any existing entry with the same key.
    /// </summary>
    [ObservableProperty]
    private string _componentIdentifier = string.Empty;

    /// <summary>Human-readable preview info (port count, wavelength range).</summary>
    [ObservableProperty]
    private string _previewInfo = string.Empty;

    /// <summary>Status message shown after an import attempt.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>True while an import is in progress.</summary>
    [ObservableProperty]
    private bool _isImporting;

    /// <summary>True after the last import completed successfully.</summary>
    [ObservableProperty]
    private bool _lastImportSucceeded;

    /// <summary>File dialog service for the Browse button.</summary>
    public IFileDialogService? FileDialogService { get; set; }

    /// <summary>
    /// The design's S-matrix store (from <c>FileOperationsViewModel.StoredSMatrices</c>).
    /// Must be set by <c>MainViewModel</c> after construction to share the same dictionary
    /// that <c>FileOperationsViewModel</c> persists on save.
    /// </summary>
    public Dictionary<string, ComponentSMatrixData>? StoredSMatrices
    {
        get => _storedSMatrices;
        set => _storedSMatrices = value;
    }

    /// <summary>Initializes a new instance of <see cref="SParameterImportViewModel"/>.</summary>
    public SParameterImportViewModel()
    {
        _importers = new ISParameterImporter[]
        {
            new LumericalSParameterImporter(),
            new TouchstoneImporter()
        };
    }

    /// <summary>Opens a file dialog to select an S-parameter file.</summary>
    [RelayCommand]
    private async Task BrowseFile()
    {
        if (FileDialogService == null) return;

        var path = await FileDialogService.ShowOpenFileDialogAsync(
            "Select S-Parameter File",
            "S-Parameter Files|*.sparam;*.dat;*.txt;*.s1p;*.s2p;*.s3p;*.s4p;*.sNp|All Files|*.*");

        if (path == null) return;

        FilePath = path;
        PreviewInfo = string.Empty;
        DetectedFormat = string.Empty;
        StatusText = string.Empty;
        LastImportSucceeded = false;

        DetectedFormat = DetectFormat(path);

        // Auto-fill component identifier from filename if empty
        if (string.IsNullOrWhiteSpace(ComponentIdentifier))
            ComponentIdentifier = Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>Parses the selected file and stores the result in the design.</summary>
    [RelayCommand]
    private async Task Import()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusText = "No file selected.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ComponentIdentifier))
        {
            StatusText = "Component identifier cannot be empty.";
            return;
        }

        if (_storedSMatrices == null)
        {
            StatusText = "Import store not initialized. Open or create a design first.";
            return;
        }

        var importer = FindImporter(FilePath);
        if (importer == null)
        {
            StatusText = $"Unsupported file type: {Path.GetExtension(FilePath)}";
            return;
        }

        IsImporting = true;
        LastImportSucceeded = false;
        StatusText = "Importing…";

        try
        {
            var imported = await importer.ImportAsync(FilePath);

            var smatrixData = SParameterConverter.ToComponentSMatrixData(imported);
            _storedSMatrices![ComponentIdentifier] = smatrixData;

            PreviewInfo = BuildPreviewInfo(imported);
            StatusText = $"Imported {imported.PortCount}-port S-matrix ({imported.SMatricesByWavelengthNm.Count} wavelengths) → '{ComponentIdentifier}'.";
            LastImportSucceeded = true;
        }
        catch (SParameterImportException ex)
        {
            StatusText = $"Parse error: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private ISParameterImporter? FindImporter(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return _importers.FirstOrDefault(i => i.SupportedExtensions.Contains(ext));
    }

    private string DetectFormat(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".sparam" or ".dat" => "Lumerical SiEPIC",
            ".txt" => "Lumerical GC TXT",
            var e when e.StartsWith(".s") && e.EndsWith("p") => $"Touchstone {ext.ToUpperInvariant()}",
            _ => $"Unknown ({ext})"
        };
    }

    private static string BuildPreviewInfo(ImportedSParameters imported)
    {
        if (imported.SMatricesByWavelengthNm.Count == 0)
            return "No wavelength data.";

        var wls = imported.SMatricesByWavelengthNm.Keys.OrderBy(k => k).ToArray();
        return $"{imported.PortCount} ports | {wls.Length} wavelengths ({wls[0]}–{wls[^1]} nm) | {imported.SourceFormat}";
    }
}
