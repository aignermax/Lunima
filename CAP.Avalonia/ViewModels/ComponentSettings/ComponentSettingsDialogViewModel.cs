using System.Collections.ObjectModel;
using System.Numerics;
using CAP_Core.Components.Core;
using CAP_DataAccess.Import;
using CAP_DataAccess.Persistence.PIR;
using CAP.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.ComponentSettings;

/// <summary>
/// ViewModel for the Component Settings dialog.
/// Displays per-wavelength S-matrices for a component (PDK template or canvas instance),
/// and allows importing new S-matrices from Lumerical / Touchstone files.
/// </summary>
public partial class ComponentSettingsDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<ISParameterImporter> _importers;
    private Dictionary<string, ComponentSMatrixData>? _storedSMatrices;
    private Component? _liveComponent;
    private string _entityKey = string.Empty;

    /// <summary>Dialog window title including the component name.</summary>
    [ObservableProperty]
    private string _title = "Component Settings";

    /// <summary>Status message shown after import attempts.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>True while an import is running.</summary>
    [ObservableProperty]
    private bool _isImporting;

    /// <summary>True when at least one wavelength entry is present.</summary>
    [ObservableProperty]
    private bool _hasSMatrices;

    /// <summary>Per-wavelength S-matrix entries shown in the dialog list.</summary>
    public ObservableCollection<SMatrixEntryViewModel> SMatrixEntries { get; } = new();

    /// <summary>File dialog service injected by the view layer.</summary>
    public IFileDialogService? FileDialogService { get; set; }

    /// <summary>
    /// Callback fired after every import or delete operation so the hierarchy panel
    /// can refresh its per-instance override markers.
    /// </summary>
    public Action? OnSMatrixStoreChanged { get; set; }

    /// <summary>
    /// Initialises a new instance with the available S-parameter importers.
    /// </summary>
    public ComponentSettingsDialogViewModel()
    {
        _importers = new ISParameterImporter[]
        {
            new LumericalSParameterImporter(),
            new TouchstoneImporter()
        };
    }

    /// <summary>
    /// Configures the dialog for a specific entity (PDK template or canvas instance).
    /// </summary>
    /// <param name="entityKey">
    /// Key used to store and retrieve S-matrix data in <paramref name="storedSMatrices"/>.
    /// For canvas instances this is <c>component.Identifier</c>;
    /// for PDK templates it is <c>"pdkSource::templateName"</c>.
    /// </param>
    /// <param name="displayName">Human-readable name shown in the title bar.</param>
    /// <param name="storedSMatrices">Shared dictionary from <c>FileOperationsViewModel</c>.</param>
    /// <param name="liveComponent">
    /// Optional live component instance. When set, imported S-matrices are applied
    /// immediately to the component's <see cref="Component.WaveLengthToSMatrixMap"/>
    /// so the next simulation run picks them up without reloading the design.
    /// </param>
    public void Configure(
        string entityKey,
        string displayName,
        Dictionary<string, ComponentSMatrixData> storedSMatrices,
        Component? liveComponent = null)
    {
        _entityKey = entityKey;
        _storedSMatrices = storedSMatrices;
        _liveComponent = liveComponent;
        Title = $"Component Settings: {displayName}";
        StatusText = string.Empty;
        RefreshEntries();
    }

    /// <summary>
    /// Opens a file dialog and imports an S-matrix from a Lumerical or Touchstone file.
    /// The imported data is stored under the entity key and, if a live component is
    /// configured, applied immediately to its wavelength map.
    /// </summary>
    [RelayCommand]
    private async Task LoadFromFile()
    {
        if (FileDialogService == null || _storedSMatrices == null)
            return;

        var path = await FileDialogService.ShowOpenFileDialogAsync(
            "Select S-Parameter File",
            "S-Parameter Files|*.sparam;*.dat;*.txt;*.s1p;*.s2p;*.s3p;*.s4p;*.sNp|All Files|*.*");

        if (path == null)
            return;

        var importer = FindImporter(path);
        if (importer == null)
        {
            StatusText = $"Unsupported file type: {Path.GetExtension(path)}";
            return;
        }

        IsImporting = true;
        StatusText = "Importing…";

        try
        {
            var imported = await importer.ImportAsync(path);
            var smatrixData = SParameterConverter.ToComponentSMatrixData(imported);
            _storedSMatrices[_entityKey] = smatrixData;

            if (_liveComponent != null)
                SMatrixOverrideApplicator.Apply(_liveComponent, smatrixData);

            var portInfo = $"{imported.PortCount} ports, {imported.SMatricesByWavelengthNm.Count} wavelengths";
            StatusText = $"Imported {portInfo} from '{Path.GetFileName(path)}'.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
            RefreshEntries();
        }
    }

    /// <summary>
    /// Removes a single wavelength entry from the stored S-matrix data.
    /// The deletion takes effect on the next simulation run after reloading the design.
    /// </summary>
    [RelayCommand]
    private void DeleteEntry(SMatrixEntryViewModel entry)
    {
        if (_storedSMatrices == null || !_storedSMatrices.TryGetValue(_entityKey, out var data))
            return;

        data.Wavelengths.Remove(entry.WavelengthKey);

        if (data.Wavelengths.Count == 0)
            _storedSMatrices.Remove(_entityKey);

        StatusText = $"Removed wavelength {entry.WavelengthKey} nm. Reload design to restore PDK default.";
        RefreshEntries();
    }

    private void RefreshEntries()
    {
        SMatrixEntries.Clear();

        if (_storedSMatrices == null || !_storedSMatrices.TryGetValue(_entityKey, out var data))
        {
            HasSMatrices = false;
            OnSMatrixStoreChanged?.Invoke();
            return;
        }

        foreach (var kvp in data.Wavelengths.OrderBy(k => k.Key))
            SMatrixEntries.Add(new SMatrixEntryViewModel(kvp.Key, kvp.Value, data.SourceNote));

        HasSMatrices = SMatrixEntries.Count > 0;
        OnSMatrixStoreChanged?.Invoke();
    }

    private ISParameterImporter? FindImporter(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".txt")
            return LooksLikeLumericalTxt(path) ? _importers[0] : null;
        return _importers.FirstOrDefault(i => i.SupportedExtensions.Contains(ext));
    }

    private static bool LooksLikeLumericalTxt(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith('!'))
                    continue;
                if (trimmed.StartsWith('('))
                    return true;
                var tokens = trimmed.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return tokens.Length >= 9 &&
                       double.TryParse(tokens[0], System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out _);
            }
        }
        catch { /* ignore read errors */ }
        return false;
    }
}
