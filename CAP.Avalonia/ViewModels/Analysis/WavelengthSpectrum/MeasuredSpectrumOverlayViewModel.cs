using System.Globalization;
using CAP_Core.Analysis.MeasuredSpectrum;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;

/// <summary>
/// Measured-spectrum overlay for the Wavelength Spectrum tab (#1335): loads a
/// two-column lab CSV (wavelength, power) via the shared file dialog, exposes
/// it to the plot as an extra "Measured" series and extracts FSR / n_g from
/// the fringes via <see cref="FringeAnalyzer"/>. Not persisted in .lun — the
/// measurement lives outside the design file.
/// </summary>
public partial class MeasuredSpectrumOverlayViewModel : ObservableObject
{
    private const string FileDialogFilters = "Spectrum CSV|*.csv;*.txt|All Files|*.*";

    private readonly CAP_Core.ErrorConsoleService? _errorConsole;
    private MeasuredSpectrum? _spectrum;

    /// <summary>Optional file-dialog service; set by the main window once the storage provider is ready.</summary>
    public IFileDialogService? FileDialogService { get; set; }

    /// <summary>Raised whenever <see cref="Spectrum"/> changes (load/clear) so the host can re-render the plot.</summary>
    public event EventHandler? OverlayChanged;

    /// <summary>The currently loaded measured spectrum, or null when none is overlayed.</summary>
    public MeasuredSpectrum? Spectrum
    {
        get => _spectrum;
        private set
        {
            if (ReferenceEquals(_spectrum, value)) return;
            _spectrum = value;
            OnPropertyChanged();
            OverlayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [ObservableProperty] private bool _hasOverlay;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _overlayLabel = "";
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private string _errorText = "";

    /// <summary>MZI arm imbalance ΔL in µm; n_g is only shown when &gt; 0.</summary>
    [ObservableProperty] private double _armImbalanceUm;

    /// <summary>Initializes a new instance of <see cref="MeasuredSpectrumOverlayViewModel"/>.</summary>
    public MeasuredSpectrumOverlayViewModel(CAP_Core.ErrorConsoleService? errorConsole = null)
    {
        _errorConsole = errorConsole;
    }

    partial void OnArmImbalanceUmChanged(double value) => Reanalyze();

    /// <summary>Opens the CSV file picker and loads the chosen spectrum.</summary>
    [RelayCommand]
    private async Task LoadMeasuredAsync()
    {
        if (FileDialogService == null) return;
        var path = await FileDialogService.ShowOpenFileDialogAsync(
            LocalizationService.Instance.Translate("Analysis.Spectrum.Measured.LoadDialogTitle"),
            FileDialogFilters);
        if (string.IsNullOrEmpty(path)) return;
        await LoadFromFileAsync(path);
    }

    /// <summary>
    /// Loads and analyzes a measured spectrum CSV. Runs the parse on a
    /// background thread so the UI stays responsive on multi-MB lab exports.
    /// Errors are surfaced inline in <see cref="ErrorText"/>, never thrown.
    /// </summary>
    public async Task LoadFromFileAsync(string path)
    {
        IsLoading = true;
        ErrorText = "";
        try
        {
            var spectrum = await Task.Run(() => ReadSpectrumFile(path));
            Spectrum = spectrum;
            HasOverlay = true;
            OverlayLabel = spectrum.Label;
            Reanalyze();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _errorConsole?.LogWarning($"Measured spectrum load failed: {ex.Message}");
            ErrorText = string.Format(
                CultureInfo.InvariantCulture,
                LocalizationService.Instance.Translate("Analysis.Spectrum.Measured.LoadFailed"),
                ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Removes the overlay and clears any result/error text.</summary>
    [RelayCommand]
    private void ClearMeasured()
    {
        Spectrum = null;
        HasOverlay = false;
        OverlayLabel = "";
        ResultText = "";
        ErrorText = "";
    }

    private static MeasuredSpectrum ReadSpectrumFile(string path)
    {
        string text = System.IO.File.ReadAllText(path);
        // Auto-detect power unit: lab dB exports contain negative values or
        // large positive ones; linear transmission is always in [0, ~1].
        var probe = MeasuredSpectrumCsvReader.Parse(text, PowerUnit.Linear, Path.GetFileName(path));
        bool looksDecibel = probe.PowerLinear.Any(p => p < 0) || probe.PowerLinear.Max() > 1.5;
        return looksDecibel
            ? MeasuredSpectrumCsvReader.Parse(text, PowerUnit.Decibel, Path.GetFileName(path))
            : probe;
    }

    private void Reanalyze()
    {
        if (Spectrum == null)
        {
            ResultText = "";
            return;
        }

        var result = FringeAnalyzer.Analyze(Spectrum, ArmImbalanceUm);
        var i18n = LocalizationService.Instance;
        if (!result.HasFringes)
        {
            ResultText = i18n.Translate("Analysis.Spectrum.Measured.NoFringes");
            return;
        }

        string fsr = result.MeanFsrNm.ToString("0.0", CultureInfo.InvariantCulture);
        string fsrStd = result.FsrStdDevNm.ToString("0.0", CultureInfo.InvariantCulture);
        if (result.GroupIndex is double ng)
        {
            ResultText = string.Format(
                CultureInfo.InvariantCulture,
                i18n.Translate("Analysis.Spectrum.Measured.ResultWithNg"),
                fsr, fsrStd, ng.ToString("0.00", CultureInfo.InvariantCulture),
                ArmImbalanceUm.ToString("0.##", CultureInfo.InvariantCulture));
        }
        else
        {
            ResultText = string.Format(
                CultureInfo.InvariantCulture,
                i18n.Translate("Analysis.Spectrum.Measured.ResultNoNg"),
                fsr, fsrStd);
        }
    }
}
