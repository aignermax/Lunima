using System.ComponentModel;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Components.Process;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>Explains the active process and waits for a real GDS export.</summary>
public partial class ManufacturingTourViewModel : ObservableObject
{
    public const string ExampleFileName = "Two Chiplets - Edge-Coupler Link.lun";
    private readonly GdsExportViewModel _export;
    private long _exportBaseline;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private int _currentStepIndex;

    public Func<ActiveProcessSelection?>? ActiveProcessProvider { get; set; }
    public Action? ShowProcessDetails { get; set; }
    public bool IsIntroduction => CurrentStepIndex == 0;
    public bool IsClosing => CurrentStepIndex == 2;
    public bool CanContinue => IsIntroduction || IsClosing;
    public string? CurrentTargetName => CurrentStepIndex switch
    {
        0 => "ActiveProcessIndicator", 1 => "ExportMenuButton", _ => null
    };
    public string CurrentTitle => Text($"ManufacturingTour.Step{CurrentStepIndex + 1}Title");
    public string CurrentBody => Text($"ManufacturingTour.Step{CurrentStepIndex + 1}Body");
    public string CurrentCue => Text($"ManufacturingTour.Step{CurrentStepIndex + 1}Cue");
    public string ContinueText => Text(IsClosing ? "ManufacturingTour.Finish" : "ManufacturingTour.Continue");
    public string ProgressText => $"{Text("Tutorial.Step")} {CurrentStepIndex + 1}/3";
    public string ExportStatus => IsIntroduction ? string.Empty : _export.LastExportStatus;
    public string ProcessSummary
    {
        get
        {
            var process = ActiveProcessProvider?.Invoke();
            if (process == null || process.IsPlayground || process.Fingerprint == null)
                return Text("ManufacturingTour.NoProcess");
            var fp = process.Fingerprint;
            return string.Format(CultureInfo.CurrentCulture, Text("ManufacturingTour.ProcessSummary"),
                process.DisplayName, fp.CoreMaterial ?? "?", fp.CoreThicknessNm?.ToString(CultureInfo.CurrentCulture) ?? "?",
                fp.Cladding ?? "?", fp.DesignWavelengthNm);
        }
    }

    public ManufacturingTourViewModel(GdsExportViewModel export)
    {
        _export = export;
        LocalizationWeakSubscription.Subscribe(this, static (self, _) => self.Refresh());
    }

    public void Start()
    {
        Detach();
        _exportBaseline = _export.LatestStartedGdsExportRequestId;
        CurrentStepIndex = 0;
        IsCompleted = false;
        IsActive = true;
        _export.PropertyChanged += OnExportChanged;
        Refresh();
    }

    [RelayCommand]
    public void InspectProcess()
    {
        if (!IsActive) return;
        ShowProcessDetails?.Invoke();
        OnPropertyChanged(nameof(ProcessSummary));
    }

    [RelayCommand]
    public void Next()
    {
        if (!IsActive) return;
        if (IsIntroduction)
        {
            // Only exports requested during the export stage can complete it.
            _exportBaseline = _export.LatestStartedGdsExportRequestId;
            CurrentStepIndex = 1;
        }
        else if (IsClosing)
        {
            IsCompleted = true;
            IsActive = false;
            Detach();
        }
    }

    [RelayCommand]
    public void Skip()
    {
        IsActive = false;
        Detach();
    }

    private void OnExportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsActive) return;
        if (e.PropertyName == nameof(GdsExportViewModel.LastExportStatus))
            OnPropertyChanged(nameof(ExportStatus));
        if (CurrentStepIndex == 1 && e.PropertyName == nameof(GdsExportViewModel.LastSuccessfulGdsExportRequestId)
            && _export.LastSuccessfulGdsExportRequestId > _exportBaseline)
            CurrentStepIndex = 2;
    }

    private void Detach() => _export.PropertyChanged -= OnExportChanged;
    private static string Text(string key) => LocalizationService.Instance.Translate(key);
    partial void OnCurrentStepIndexChanged(int value) => Refresh();
    private void Refresh()
    {
        foreach (var property in new[] { nameof(IsIntroduction), nameof(IsClosing), nameof(CanContinue),
            nameof(CurrentTitle), nameof(CurrentBody), nameof(CurrentCue), nameof(CurrentTargetName),
            nameof(ContinueText), nameof(ProgressText), nameof(ProcessSummary), nameof(ExportStatus) })
            OnPropertyChanged(property);
    }
}
