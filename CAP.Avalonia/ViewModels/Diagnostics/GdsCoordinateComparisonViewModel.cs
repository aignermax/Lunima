using System.Globalization;
using CAP_Core.Export.Contracts;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.Lifetime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// ViewModel for the GDS Coordinate Comparison panel (Issue #333).
/// Runs scripts/compare_gds_coords.py on two JSON coordinate files and
/// displays exact per-element deviations in micrometres, enabling engineers
/// to confirm or refute fabrication-blocking alignment bugs (Issue #329).
/// </summary>
public partial class GdsCoordinateComparisonViewModel : ObservableObject, IAsyncDisposable
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
    partial void OnReferenceJsonPathChanging(string value) => InvalidateRequest();
    partial void OnSystemJsonPathChanging(string value) => InvalidateRequest();
    public Task StopAsync() => _work.StopAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
    private readonly IGdsCoordinateComparer _service;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunComparisonCommand))]
    private string _referenceJsonPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunComparisonCommand))]
    private string _systemJsonPath = string.Empty;

    [ObservableProperty]
    private string _comparisonStatus = LocalizationService.Instance.Translate("Diag.Gds.EnterPaths");

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _maxDeviationText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunComparisonCommand))]
    private bool _isComparing;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _passed;

    /// <summary>
    /// Callback invoked when the user requests a file picker for the Reference JSON.
    /// The callback should return the selected path, or null if cancelled.
    /// Assigned by the View code-behind.
    /// </summary>
    public Func<Task<string?>>? BrowseForReferenceJson { get; set; }

    /// <summary>
    /// Callback invoked when the user requests a file picker for the System JSON.
    /// The callback should return the selected path, or null if cancelled.
    /// Assigned by the View code-behind.
    /// </summary>
    public Func<Task<string?>>? BrowseForSystemJson { get; set; }

    /// <summary>Initializes a new instance of <see cref="GdsCoordinateComparisonViewModel"/>.</summary>
    /// <param name="service">Comparison operation supplied by the composition root.</param>
    public GdsCoordinateComparisonViewModel(IGdsCoordinateComparer service, ApplicationWorkCoordinator? applicationWork = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _work.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>
    /// Runs the Python comparison script on the two configured JSON paths.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunComparison))]
    public Task RunComparisonAsync() => _work.RunAsync(RunComparisonCoreAsync);

    private async Task RunComparisonCoreAsync(CancellationToken cancellationToken)
    {
        InvalidateRequest();
        var revision = _revision;
        await using var request = new AsyncCancellationScope(cancellationToken);
        _request = request;
        var reference = ReferenceJsonPath.Trim();
        var system = SystemJsonPath.Trim();
        try
        {
            IsComparing = true;
            if (!IsCurrent(revision)) return;
            HasResults = false;
            if (!IsCurrent(revision)) return;
            ComparisonStatus = LocalizationService.Instance.Translate("Diag.Gds.Running");
            if (!IsCurrent(revision)) return;
            ResultText = string.Empty;
            if (!IsCurrent(revision)) return;
            MaxDeviationText = string.Empty;
            if (!IsCurrent(revision)) return;
            var result = await _service.CompareAsync(reference, system, null, request.Token);
            if (!IsCurrent(revision)) return;

            Passed = result.Passed;
            if (!IsCurrent(revision)) return;
            MaxDeviationText = string.Format(
                CultureInfo.CurrentCulture,
                LocalizationService.Instance.Translate("Diag.Gds.DeviationSummary"),
                result.MaxDeviationUm,
                result.RmsDeviationUm);
            if (!IsCurrent(revision)) return;
            ComparisonStatus = result.StatusText;
            if (!IsCurrent(revision)) return;
            ResultText = string.IsNullOrWhiteSpace(result.ErrorOutput)
                ? result.RawOutput
                : $"STDERR:\n{result.ErrorOutput}\n\nSTDOUT:\n{result.RawOutput}";
        }
        catch (Exception ex)
        {
            if (!IsCurrent(revision)) return;
            Passed = false;
            if (!IsCurrent(revision)) return;
            ComparisonStatus = string.Format(LocalizationService.Instance.Translate("Diag.ErrorFormat"), ex.Message);
            if (!IsCurrent(revision)) return;
            ResultText = ex.ToString();
            if (!IsCurrent(revision)) return;
            MaxDeviationText = string.Empty;
        }
        finally
        {
            try { if (IsCurrent(revision)) HasResults = true; }
            finally
            {
                if (ReferenceEquals(_request, request))
                {
                    _request = null;
                    IsComparing = false;
                }
            }
        }
    }

    private bool CanRunComparison() =>
        !_work.IsStopping && !IsComparing
        && !string.IsNullOrWhiteSpace(ReferenceJsonPath)
        && !string.IsNullOrWhiteSpace(SystemJsonPath);

    /// <summary>Opens a file picker to select the reference JSON path.</summary>
    [RelayCommand]
    public Task BrowseReferenceAsync() => _work.RunAsync(async _ =>
    {
        if (BrowseForReferenceJson == null)
            return;
        var revision = _revision;
        var path = await BrowseForReferenceJson();
        if (IsCurrent(revision) && path != null)
            ReferenceJsonPath = path;
    });

    /// <summary>Opens a file picker to select the system JSON path.</summary>
    [RelayCommand]
    public Task BrowseSystemAsync() => _work.RunAsync(async _ =>
    {
        if (BrowseForSystemJson == null)
            return;
        var revision = _revision;
        var path = await BrowseForSystemJson();
        if (IsCurrent(revision) && path != null)
            SystemJsonPath = path;
    });

    /// <summary>Clears all results and resets the panel to its initial state.</summary>
    [RelayCommand]
    public void ClearResults()
    {
        if (_work.IsStopping) return;
        InvalidateRequest();
        var revision = _revision;
        HasResults = false;
        if (!IsCurrent(revision)) return;
        Passed = false;
        if (!IsCurrent(revision)) return;
        ResultText = string.Empty;
        if (!IsCurrent(revision)) return;
        MaxDeviationText = string.Empty;
        if (!IsCurrent(revision)) return;
        ComparisonStatus = LocalizationService.Instance.Translate("Diag.Gds.EnterPaths");
    }

}
