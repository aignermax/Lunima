using System.Collections.ObjectModel;
using CAP_Core;
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
    private readonly IFileDialogService _fileDialogService;
    private readonly ErrorConsoleService? _errorConsole;
    private readonly IReadOnlyList<ISParameterImporter> _importers;

    private Dictionary<string, ComponentSMatrixData>? _storedSMatrices;
    private Component? _liveComponent;
    private string _entityKey = string.Empty;
    private Action? _onChanged;
    private bool _isUserGlobalScope;

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

    /// <summary>
    /// Initialises a new instance with constructor-injected dependencies.
    /// </summary>
    /// <param name="fileDialogService">Service used to open the file picker for imports.</param>
    /// <param name="errorConsole">Optional error console for surfacing import failures and partial overrides.</param>
    /// <param name="importers">Optional importer set; defaults to Lumerical + Touchstone.</param>
    public ComponentSettingsDialogViewModel(
        IFileDialogService fileDialogService,
        ErrorConsoleService? errorConsole = null,
        IReadOnlyList<ISParameterImporter>? importers = null)
    {
        _fileDialogService = fileDialogService;
        _errorConsole = errorConsole;
        _importers = importers ?? new ISParameterImporter[]
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
    /// <param name="onChanged">
    /// Optional callback invoked after every successful import or delete so observers
    /// (e.g. the hierarchy panel) can refresh derived state such as override badges.
    /// </param>
    /// <param name="isUserGlobalScope">
    /// When true, the dialog title flags that the override applies to all projects
    /// — used when <paramref name="storedSMatrices"/> is the user-global store
    /// rather than the project's <c>.lun</c>-backed store. Purely a UX hint;
    /// persistence behaviour is determined by the store the caller passes in.
    /// </param>
    public void Configure(
        string entityKey,
        string displayName,
        Dictionary<string, ComponentSMatrixData> storedSMatrices,
        Component? liveComponent = null,
        Action? onChanged = null,
        bool isUserGlobalScope = false)
    {
        _entityKey = entityKey;
        _storedSMatrices = storedSMatrices;
        _liveComponent = liveComponent;
        _onChanged = onChanged;
        _isUserGlobalScope = isUserGlobalScope;
        Title = isUserGlobalScope
            ? $"Component Settings: {displayName} (applies to all projects)"
            : $"Component Settings: {displayName}";
        StatusText = string.Empty;
        RefreshEntries(notifyChanged: false);
    }

    /// <summary>
    /// Opens a file dialog and imports an S-matrix from a Lumerical or Touchstone file.
    /// The imported data is stored under the entity key and, if a live component is
    /// configured, applied immediately to its wavelength map.
    /// </summary>
    [RelayCommand]
    private async Task LoadFromFile()
    {
        if (_storedSMatrices == null)
            return;

        var path = await _fileDialogService.ShowOpenFileDialogAsync(
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

            ApplyResult? applyResult = null;
            if (_liveComponent != null)
                applyResult = SMatrixOverrideApplicator.Apply(_liveComponent, smatrixData, _errorConsole);

            StatusText = BuildImportStatus(path, imported, applyResult);
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"S-parameter import failed for '{path}'", ex);
            StatusText = $"Import failed: {ex.Message}" + (_errorConsole != null ? " (see Error Console)" : "");
        }
        finally
        {
            IsImporting = false;
            RefreshEntries(notifyChanged: true);
        }
    }

    private static string BuildImportStatus(
        string path,
        ImportedSParameters imported,
        ApplyResult? applyResult)
    {
        var fileName = Path.GetFileName(path);
        var portInfo = $"{imported.PortCount} ports, {imported.SMatricesByWavelengthNm.Count} wavelengths";

        if (applyResult == null)
            return $"Imported {portInfo} from '{fileName}'.";

        if (applyResult.IsTotalFailure)
            return $"Imported {portInfo} from '{fileName}', but no wavelength could be applied to the live component (see Error Console).";

        if (applyResult.IsPartial)
            return $"Imported {portInfo}; applied {applyResult.Applied} of {applyResult.Applied + applyResult.Skipped.Count} wavelength(s) — {applyResult.Skipped.Count} skipped (see Error Console).";

        var replacedNote = applyResult.Replaced > 0 ? $" ({applyResult.Replaced} replaced)" : "";
        return $"Imported {portInfo} from '{fileName}'; applied {applyResult.Applied} wavelength(s){replacedNote}.";
    }

    /// <summary>
    /// Removes a single wavelength entry from the stored S-matrix data and from the live
    /// component's wavelength map (if a live component is configured), so the next
    /// simulation run reflects the deletion immediately.
    /// </summary>
    [RelayCommand]
    private void DeleteEntry(SMatrixEntryViewModel entry)
    {
        if (_storedSMatrices == null || !_storedSMatrices.TryGetValue(_entityKey, out var data))
            return;

        data.Wavelengths.Remove(entry.WavelengthKey);
        if (data.Wavelengths.Count == 0)
            _storedSMatrices.Remove(_entityKey);

        if (_liveComponent != null && int.TryParse(entry.WavelengthKey, out int wavelengthNm))
            _liveComponent.WaveLengthToSMatrixMap.Remove(wavelengthNm);

        StatusText = $"Removed wavelength {entry.WavelengthKey} nm. Reload design to restore PDK default.";
        RefreshEntries(notifyChanged: true);
    }

    private void RefreshEntries(bool notifyChanged)
    {
        SMatrixEntries.Clear();

        if (_storedSMatrices == null || !_storedSMatrices.TryGetValue(_entityKey, out var data))
        {
            HasSMatrices = false;
            if (notifyChanged) _onChanged?.Invoke();
            return;
        }

        foreach (var kvp in data.Wavelengths.OrderBy(k => k.Key))
            SMatrixEntries.Add(new SMatrixEntryViewModel(kvp.Key, kvp.Value, data.SourceNote));

        HasSMatrices = SMatrixEntries.Count > 0;
        if (notifyChanged) _onChanged?.Invoke();
    }

    private ISParameterImporter? FindImporter(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".txt")
            return LooksLikeLumericalTxt(path) ? _importers.First(i => i is LumericalSParameterImporter) : null;
        return _importers.FirstOrDefault(i => i.SupportedExtensions.Contains(ext));
    }

    private bool LooksLikeLumericalTxt(string path)
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
        catch (IOException ex)
        {
            _errorConsole?.LogWarning($"Could not probe '{path}' for Lumerical .txt format: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _errorConsole?.LogWarning($"Could not probe '{path}' for Lumerical .txt format: {ex.Message}");
        }
        return false;
    }
}
