using CAP_Core.Export.Contracts;
using System.Collections.ObjectModel;
using System.Globalization;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.OpenEblCheck;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Export;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.OpenEbl;

/// <summary>
/// ViewModel behind the "Check for openEBL…" dialog: exports the current design via the
/// Nazca/EBeam path to a temp GDS named after the openEBL convention
/// (<see cref="OpenEblFileNameHelper"/>) and runs the headless
/// <see cref="IOpenEblSubmissionChecker"/> against it. The run is asynchronous with a busy
/// state and stays cancellable (cancellation kills the Python process inside the service);
/// every failure mode — export error, missing toolchain, crashed script — lands as a typed
/// result state, never as an exception escaping the command.
/// </summary>
public partial class OpenEblCheckViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ShellWorkScope _work = new();
    private bool _cancelRequested;
    private Task? _activeRun;
    private Task? _windowRetirement;
    /// <summary>Retires accepted export and checking workflows.</summary>
    public Task StopAsync() => _work.StopAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
    private void CheckCurrent(CancellationToken token = default)
    {
        _work.ThrowIfStopping();
        if (_cancelRequested || _windowRetirement != null)
            throw new OperationCanceledException(new CancellationToken(true));
        token.ThrowIfCancellationRequested();
    }
    private void Publish(Action action, CancellationToken token = default)
    {
        CheckCurrent(token);
        action();
        CheckCurrent(token);
    }
    private const string GdsExtension = ".gds";
    private const string PassColor = "#7ee787";
    private const string FailColor = "#ff8888";
    private const string WarnColor = "#e8c872";

    private static readonly string TempDirectory =
        Path.Combine(Path.GetTempPath(), "Lunima", "openEBL");

    private readonly DesignCanvasViewModel _canvas;
    private readonly SimpleNazcaExporter _nazcaExporter;
    private readonly CAP_Core.Export.Contracts.IGdsScriptRunner _gdsExport;
    private readonly IOpenEblSubmissionChecker _checker;
    private readonly ErrorConsoleService? _errorConsole;

    private AsyncCancellationScope? _runCts;
    private bool _fileNameEdited;
    private bool _suppressFileNameTracking;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _designName = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _headline = string.Empty;

    [ObservableProperty]
    private string _headlineColor = PassColor;

    [ObservableProperty]
    private bool _showCheckOutcomes;

    [ObservableProperty]
    private bool _submissionPassed;

    [ObservableProperty]
    private bool _verificationPassed;

    [ObservableProperty]
    private string _dieSizeText = string.Empty;

    [ObservableProperty]
    private bool _isToolchainMissing;

    [ObservableProperty]
    private string _toolchainMessage = string.Empty;

    [ObservableProperty]
    private string _detailMessage = string.Empty;

    [ObservableProperty]
    private string _gdsPathText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<OpenEblCheckErrorItemViewModel> _errors = new();

    [ObservableProperty]
    private ObservableCollection<OpenEblPreflightIssueViewModel> _preflightIssues = new();

    [ObservableProperty]
    private bool _hasPreflightErrors;

    [ObservableProperty]
    private bool _hasPreflightWarnings;

    /// <summary>Initializes a new instance of <see cref="OpenEblCheckViewModel"/>.</summary>
    public OpenEblCheckViewModel(
        DesignCanvasViewModel canvas,
        SimpleNazcaExporter nazcaExporter,
        CAP_Core.Export.Contracts.IGdsScriptRunner gdsExport,
        IOpenEblSubmissionChecker checker,
        ErrorConsoleService? errorConsole = null,
        ApplicationWorkCoordinator? applicationWork = null)
    {
        _canvas = canvas;
        _nazcaExporter = nazcaExporter;
        _gdsExport = gdsExport;
        _checker = checker;
        _errorConsole = errorConsole;
        ReproposeFileName();
        _work.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>The pip command installing the check toolchain; the copy button puts this on the clipboard.</summary>
    public string PipInstallCommand => OpenEblToolchainRequirements.PipInstallCommand;

    /// <summary>Localized "passed"/"failed" word for the submission-check row.</summary>
    public string SubmissionOutcomeText => Translate(SubmissionPassed ? "OpenEblCheck.Passed" : "OpenEblCheck.Failed");

    /// <summary>Localized "passed"/"failed" word for the verification row.</summary>
    public string VerificationOutcomeText => Translate(VerificationPassed ? "OpenEblCheck.Passed" : "OpenEblCheck.Failed");

    /// <summary>Clipboard bridge, wired by the dialog code-behind (views own the clipboard).</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>
    /// Prepares the dialog for opening: pre-fills the design name from the current design
    /// file, re-proposes the file name (unless the user edited it) and clears the stale result.
    /// </summary>
    /// <param name="currentDesignName">Name of the currently open design file, if any.</param>
    public void PrepareForOpen(string? currentDesignName)
    {
        if (_work.IsStopping || _activeRun != null || _windowRetirement is { IsCompleted: false }) return;
        _windowRetirement = null;
        _cancelRequested = false;
        try
        {
            if (DesignName.Length == 0 && !string.IsNullOrWhiteSpace(currentDesignName))
                Publish(() => DesignName = currentDesignName);
            ReproposeFileName();
            CheckCurrent();
            ResetResult();
            ClearPreflight();
            Publish(() => StatusText = string.Empty);
        }
        catch (OperationCanceledException) when (_work.IsStopping || _windowRetirement != null) { }
    }

    /// <summary>Exports the design to a temp GDS and runs the openEBL checks on it.</summary>
    [RelayCommand]
    private Task ExportAndCheckAsync() => RunExportAndCheckAsync(skipPreflight: false);

    /// <summary>
    /// Runs the external check despite pre-flight errors (issue #1375 — no hard block; the
    /// student decides). The pre-flight list stays visible above the external result.
    /// </summary>
    [RelayCommand]
    private Task CheckAnywayAsync() => RunExportAndCheckAsync(skipPreflight: true);

    private Task RunExportAndCheckAsync(bool skipPreflight)
    {
        if (_work.IsStopping || _windowRetirement != null || _activeRun != null) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _activeRun = completion.Task;
        _ = CompleteRunAsync(skipPreflight, completion);
        return completion.Task;
    }

    private async Task CompleteRunAsync(bool skipPreflight, TaskCompletionSource completion)
    {
        Exception? failure = null;
        try { await _work.RunAsync(token => RunExportAndCheckCoreAsync(skipPreflight, token)); }
        catch (Exception error) { failure = error; }
        finally { _activeRun = null; }
        if (failure is OperationCanceledException canceled) completion.TrySetCanceled(canceled.CancellationToken);
        else if (failure != null) completion.TrySetException(failure);
        else completion.TrySetResult();
    }

    /// <summary>Seals this window session and awaits its accepted work; PrepareForOpen admits a later session.</summary>
    public Task CancelAndDrainAsync()
    {
        if (_windowRetirement != null) return _windowRetirement;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _windowRetirement = completion.Task;
        Cancel();
        _ = DrainWindowAsync(_activeRun ?? Task.CompletedTask, completion);
        return completion.Task;
    }

    private static async Task DrainWindowAsync(Task running, TaskCompletionSource completion)
    {
        try { await running; completion.TrySetResult(); }
        catch (OperationCanceledException) { completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private async Task RunExportAndCheckCoreAsync(bool skipPreflight, CancellationToken stop)
    {
        if (IsChecking || _runCts != null || _work.IsStopping)
            return;
        if (_canvas.Components.Count == 0)
        {
            StatusText = Translate("OpenEblCheck.NothingToExport");
            return;
        }
        var stem = ValidateFileStem();
        if (stem == null)
        {
            StatusText = Translate("OpenEblCheck.InvalidFileName");
            return;
        }

        var operation = new AsyncCancellationScope(stop);
        _runCts = operation;
        _cancelRequested = false;
        var cancellationToken = operation.Token;
        try
        {
            Publish(() => IsChecking = true, cancellationToken);
            if (!skipPreflight && !await RunPreflightAsync(cancellationToken))
                return;   // pre-flight errors hold the run until "Check anyway"

            CheckCurrent(cancellationToken);
            ResetResult();
            Publish(() => StatusText = Translate("OpenEblCheck.Exporting"), cancellationToken);
            var gdsPath = await ExportGdsAsync(stem, cancellationToken);
            CheckCurrent(cancellationToken);
            if (gdsPath == null)
                return;   // the export failure state is already applied

            Publish(() => GdsPathText = string.Format(CultureInfo.CurrentCulture, Translate("OpenEblCheck.GdsLocation"), gdsPath), cancellationToken);

            Publish(() => StatusText = Translate("OpenEblCheck.RunningChecks"), cancellationToken);
            var report = await _checker.CheckAsync(gdsPath, cancellationToken);
            CheckCurrent(cancellationToken);
            ApplyReport(report);
        }
        catch (OperationCanceledException)
        {
            if (!_work.IsStopping && _windowRetirement == null) StatusText = Translate("OpenEblCheck.Cancelled");
        }
        catch (Exception ex)
        {
            if (_work.IsStopping || _cancelRequested || _windowRetirement != null) return;
            _errorConsole?.LogError($"openEBL check failed: {ex.Message}", ex);
            if (_work.IsStopping || _cancelRequested || _windowRetirement != null) return;
            try
            {
                Publish(() => HasResult = true);
                Publish(() => Headline = Translate("OpenEblCheck.ResultCheckCrashed"));
                Publish(() => HeadlineColor = FailColor);
                Publish(() => DetailMessage = ex.Message);
                Publish(() => StatusText = string.Empty);
            }
            catch (OperationCanceledException) when (_work.IsStopping || _cancelRequested || _windowRetirement != null) { }
        }
        finally
        {
            try { await operation.DisposeAsync(); }
            finally { _runCts = null; IsChecking = false; }
        }
    }

    /// <summary>Cancels the running export/check; the checker kills its Python process tree.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (_runCts == null) return;
        _cancelRequested = true;
        _runCts.Cancel();
    }

    /// <summary>Copies the pip install command for the check toolchain to the clipboard.</summary>
    [RelayCommand]
    private Task CopyPipHintAsync() =>
        CopyToClipboard?.Invoke(PipInstallCommand) ?? Task.CompletedTask;

    partial void OnUsernameChanged(string value) => ReproposeFileName();

    partial void OnDesignNameChanged(string value) => ReproposeFileName();

    partial void OnFileNameChanged(string value)
    {
        if (!_suppressFileNameTracking)
            _fileNameEdited = true;
    }

    private void ReproposeFileName()
    {
        if (_fileNameEdited)
            return;
        _suppressFileNameTracking = true;
        FileName = OpenEblFileNameHelper.ProposeFileName(Username, DesignName);
        _suppressFileNameTracking = false;
    }

    private string? ValidateFileStem()
    {
        var name = FileName.Trim();
        if (name.EndsWith(GdsExtension, StringComparison.OrdinalIgnoreCase))
            name = name[..^GdsExtension.Length];
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        return name;
    }

    private async Task<string?> ExportGdsAsync(string stem, CancellationToken cancellationToken)
    {
        CheckCurrent(cancellationToken);
        Directory.CreateDirectory(TempDirectory);
        var scriptPath = Path.Combine(TempDirectory, stem + ".py");

        var nazcaCode = BuildNazcaScript();
        CheckCurrent(cancellationToken);
        await File.WriteAllTextAsync(scriptPath, nazcaCode, cancellationToken);
        CheckCurrent(cancellationToken);

        var result = await _gdsExport.ExportToGdsAsync(scriptPath, generateGds: true, cancellationToken);
        CheckCurrent(cancellationToken);
        if (result.Success && result.GdsPath != null)
            return result.GdsPath;

        Publish(() => HasResult = true, cancellationToken);
        Publish(() => Headline = Translate("OpenEblCheck.ResultExportFailed"), cancellationToken);
        Publish(() => HeadlineColor = FailColor, cancellationToken);
        Publish(() => DetailMessage = result.ErrorMessage ?? result.Status, cancellationToken);
        Publish(() => StatusText = string.Empty, cancellationToken);
        return null;
    }

    /// <summary>
    /// Builds the Nazca/EBeam export script for the current design. Synchronous string
    /// building mirrors the regular Nazca export; virtual so tests can substitute the
    /// exporter (the same seam pattern as <c>GdsExportViewModel.DiscoverPythonsAsync</c>).
    /// </summary>
    protected virtual string BuildNazcaScript() =>
        _nazcaExporter.Export(_canvas, designName: DesignName.Length > 0 ? DesignName : null);

    private void ApplyReport(OpenEblCheckReport report)
    {
        Publish(() => HasResult = true);
        Publish(() => StatusText = string.Empty);
        Publish(() => SubmissionPassed = report.SubmissionChecksPassed);
        Publish(() => VerificationPassed = report.VerificationPassed);
        Publish(() => IsToolchainMissing = report.Status == OpenEblCheckStatus.ToolchainMissing);
        Publish(() => ToolchainMessage = report.ToolchainMessage ?? string.Empty);
        Publish(() => DieSizeText = report.DieBoundingBox is { } box
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.###} × {1:0.###} µm",
                box.WidthMicrometers, box.HeightMicrometers)
            : string.Empty);
        Publish(() => Errors = new ObservableCollection<OpenEblCheckErrorItemViewModel>(
            report.Errors.Select(error => new OpenEblCheckErrorItemViewModel(
                CategoryLabelFor(error.Category), error.Message, FormatLocation(error)))));

        var (headline, color, outcomes) = report.Status switch
        {
            OpenEblCheckStatus.Passed => (Translate("OpenEblCheck.ResultPass"), PassColor, true),
            OpenEblCheckStatus.Failed => (Translate("OpenEblCheck.ResultFail"), FailColor, true),
            OpenEblCheckStatus.ToolchainMissing => (Translate("OpenEblCheck.ResultToolchainMissing"), WarnColor, false),
            OpenEblCheckStatus.ScriptsMissing => (Translate("OpenEblCheck.ResultScriptsMissing"), FailColor, false),
            _ => (Translate("OpenEblCheck.ResultCheckCrashed"), FailColor, true),
        };
        Publish(() => Headline = headline);
        Publish(() => HeadlineColor = color);
        Publish(() => ShowCheckOutcomes = outcomes);
        Publish(() => OnPropertyChanged(nameof(SubmissionOutcomeText)));
        Publish(() => OnPropertyChanged(nameof(VerificationOutcomeText)));
    }

    /// <summary>
    /// Lunima-side pre-flight (issue #1375): runs the <see cref="OpenEblPreflightChecker"/> on
    /// the canvas connections before the external check. Errors hold the run (the "Check
    /// anyway" command bypasses); warnings only let it through and stay visible above the
    /// external result. Returns false when errors were found and the caller must not export yet.
    /// </summary>
    private async Task<bool> RunPreflightAsync(CancellationToken token)
    {
        Publish(() => StatusText = Translate("OpenEblCheck.PreflightRunning"), token);
        var snapshot = _canvas.ConnectionManager.Connections.ToList();
        var findings = await Task.Run(() => OpenEblPreflightChecker.Collect(snapshot), token);
        Publish(() => StatusText = string.Empty, token);

        Publish(() => PreflightIssues = new ObservableCollection<OpenEblPreflightIssueViewModel>(
            findings.Select(finding => new OpenEblPreflightIssueViewModel(finding.Message, finding.IsError))), token);
        Publish(() => HasPreflightErrors = findings.Any(finding => finding.IsError), token);
        Publish(() => HasPreflightWarnings = findings.Any(finding => !finding.IsError), token);
        return !HasPreflightErrors;
    }

    private void ClearPreflight()
    {
        Publish(PreflightIssues.Clear);
        Publish(() => HasPreflightErrors = false);
        Publish(() => HasPreflightWarnings = false);
    }

    private void ResetResult()
    {
        Publish(() => HasResult = false);
        Publish(() => Headline = string.Empty);
        Publish(() => ShowCheckOutcomes = false);
        Publish(() => SubmissionPassed = false);
        Publish(() => VerificationPassed = false);
        Publish(() => DieSizeText = string.Empty);
        Publish(() => IsToolchainMissing = false);
        Publish(() => ToolchainMessage = string.Empty);
        Publish(() => DetailMessage = string.Empty);
        Publish(() => GdsPathText = string.Empty);
        Publish(Errors.Clear);
    }

    /// <summary>
    /// Maps a stable error category to its localized label; unknown categories (verification
    /// rule names) fall back to the generic "Check" label.
    /// </summary>
    internal static string CategoryLabelFor(string category)
    {
        var key = "OpenEblCheck.Category." + category;
        var translated = LocalizationService.Instance.Translate(key);
        return translated == key ? Translate("OpenEblCheck.Category.Other") : translated;
    }

    private static string FormatLocation(OpenEblCheckError error) =>
        error.XMicrometers is { } x && error.YMicrometers is { } y
            ? string.Format(CultureInfo.InvariantCulture, "({0:0.###} µm, {1:0.###} µm)", x, y)
            : string.Empty;

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);
}
