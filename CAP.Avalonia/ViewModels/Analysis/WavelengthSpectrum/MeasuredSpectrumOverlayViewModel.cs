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
public partial class MeasuredSpectrumOverlayViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Retires accepted file work before the overlay owner is released.</summary>
    public Task StopAsync() => _retirement ??= RetireAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
    private readonly Services.Lifetime.ShellWorkScope _work = new();
    private Task? _retirement;
    private long _loadVersion;
    private int _loadingCount;
    private const string FileDialogFilters = "Spectrum CSV|*.csv;*.txt|All Files|*.*";

    private async Task RetireAsync()
    {
        try { await _work.StopAsync(); }
        finally
        {
            FileDialogService = null;
            _spectrum = null;
            OverlayChanged = null;
            HasOverlay = false;
        }
    }

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
    private Task LoadMeasuredAsync() => _work.RunAsync(async stop =>
    {
        if (FileDialogService == null) return;
        var version = ++_loadVersion;
        var path = await FileDialogService.ShowOpenFileDialogAsync(
            LocalizationService.Instance.Translate("Analysis.Spectrum.Measured.LoadDialogTitle"),
            FileDialogFilters);
        if (!CanPublish(version, stop) || string.IsNullOrEmpty(path)) return;
        await LoadCoreAsync(path, version, stop);
    });

    /// <summary>
    /// Loads and analyzes a measured spectrum CSV. Runs the parse on a
    /// background thread so the UI stays responsive on multi-MB lab exports.
    /// Errors are surfaced inline in <see cref="ErrorText"/>, never thrown.
    /// </summary>
    public Task LoadFromFileAsync(string path) =>
        _work.RunAsync(stop => LoadCoreAsync(path, ++_loadVersion, stop));

    private bool CanPublish(long version, CancellationToken stop) =>
        !_work.IsStopping && !stop.IsCancellationRequested && version == _loadVersion;

    private async Task LoadCoreAsync(string path, long version, CancellationToken stop)
    {
        _loadingCount++;
        IsLoading = true;
        ErrorText = "";
        try
        {
            var spectrum = await ReadSpectrumAsync(path, stop);
            if (!CanPublish(version, stop)) return;
            Spectrum = spectrum;
            HasOverlay = true;
            OverlayLabel = spectrum.Label;
            Reanalyze();
        }
        catch (OperationCanceledException) when (_work.IsStopping || stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            if (!CanPublish(version, stop)) return;
            _errorConsole?.LogWarning($"Measured spectrum load failed: {ex.Message}");
            ErrorText = string.Format(
                CultureInfo.InvariantCulture,
                LocalizationService.Instance.Translate("Analysis.Spectrum.Measured.LoadFailed"),
                ex.Message);
        }
        finally
        {
            IsLoading = --_loadingCount != 0;
        }
    }

    /// <summary>Removes the overlay and clears any result/error text.</summary>
    [RelayCommand]
    private void ClearMeasured()
    {
        if (_work.IsStopping) return;
        _loadVersion++;
        Spectrum = null;
        HasOverlay = false;
        OverlayLabel = "";
        ResultText = "";
        ErrorText = "";
    }

    /// <summary>Reads and parses the selected measurement off the UI thread.</summary>
    protected virtual async Task<MeasuredSpectrum> ReadSpectrumAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        // Parsing has no cancellable API. Keep ownership until that CPU work actually
        // returns; cancellation revokes publication rather than abandoning the task.
        return await Task.Run(() => ParseSpectrum(text, path), cancellationToken).ConfigureAwait(false);
    }

    private static MeasuredSpectrum ParseSpectrum(string text, string path)
    {
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
        if (_work.IsStopping) return;
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
