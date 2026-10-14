using System.Collections.ObjectModel;
using System.ComponentModel;
using CAP.Avalonia.Services.Localization;
using CAP_Core.LightCalculation.BendLoss;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Canvas.ConnectionLoss;

/// <summary>
/// ViewModel of the "Insertion loss" section of the Properties panel: for the selected
/// waveguide connection it itemizes where light is lost — propagation over the path length,
/// radiation in every bend (radius dependent) and mode mismatch at curvature junctions — so a
/// designer sees that a 5 µm bend costs more than a 50 µm one and can give the router room.
/// Read-only; it refreshes whenever the connection's route (and therefore its loss) changes.
/// </summary>
public partial class ConnectionLossViewModel : ObservableObject
{
    /// <summary>Losses below this (dB) are shown as "&lt; 0.001 dB" instead of a misleading 0.000.</summary>
    public const double DisplayResolutionDb = 0.001;

    /// <summary>At most this many bends are itemized; longer routes keep the summary line only.</summary>
    public const int MaxListedBends = 12;

    private WaveguideConnectionViewModel? _selectedConnection;

    /// <summary>The selected connection (single-click selection), fed by MainViewModel.</summary>
    public WaveguideConnectionViewModel? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (_selectedConnection == value)
                return;
            if (_selectedConnection != null)
                _selectedConnection.PropertyChanged -= OnConnectionChanged;
            _selectedConnection = value;
            if (_selectedConnection != null)
                _selectedConnection.PropertyChanged += OnConnectionChanged;
            OnPropertyChanged();
            Refresh();
        }
    }

    /// <summary>True while an optical connection is selected (the section is visible).</summary>
    [ObservableProperty]
    private bool _hasConnection;

    /// <summary>Formatted total insertion loss.</summary>
    [ObservableProperty]
    private string _totalText = "";

    /// <summary>Formatted propagation loss.</summary>
    [ObservableProperty]
    private string _propagationText = "";

    /// <summary>Path length detail next to the propagation loss.</summary>
    [ObservableProperty]
    private string _propagationDetailText = "";

    /// <summary>Formatted radiation loss of all bends.</summary>
    [ObservableProperty]
    private string _bendsText = "";

    /// <summary>Bend count and radius range next to the bend loss.</summary>
    [ObservableProperty]
    private string _bendsDetailText = "";

    /// <summary>Formatted junction (mode-mismatch) loss.</summary>
    [ObservableProperty]
    private string _junctionsText = "";

    /// <summary>Junction count next to the junction loss.</summary>
    [ObservableProperty]
    private string _junctionsDetailText = "";

    /// <summary>Localized waveguide family the bend model was fitted for.</summary>
    [ObservableProperty]
    private string _modelText = "";

    /// <summary>True when the route bends tighter than the fabrication process allows.</summary>
    [ObservableProperty]
    private bool _isBelowProcessMinimum;

    /// <summary>One row per bend: radius, sweep and its loss.</summary>
    public ObservableCollection<string> BendRows { get; } = new();

    /// <summary>Re-reads the breakdown from the selected connection.</summary>
    public void Refresh()
    {
        var connection = _selectedConnection?.Connection;
        HasConnection = connection is { IsElectrical: false };
        BendRows.Clear();
        if (connection == null || !HasConnection)
            return;

        var loss = connection.LossBreakdown;
        TotalText = FormatDb(loss.TotalLossDb);
        PropagationText = FormatDb(loss.PropagationLossDb);
        PropagationDetailText = Format("Routing.Loss.PathLengthFormat", loss.PathLengthMicrometers);
        BendsText = FormatDb(loss.BendLossDb);
        BendsDetailText = DescribeBends(loss);
        JunctionsText = FormatDb(loss.JunctionLossDb);
        JunctionsDetailText = Format("Routing.Loss.JunctionCountFormat", loss.JunctionCount);
        ModelText = Format("Routing.Loss.ModelFormat", Translate($"Routing.Loss.Family.{loss.Family}"));
        IsBelowProcessMinimum = connection.RoutedPath?.ViolatesProcessMinBendRadius ?? false;

        foreach (var bend in loss.Bends.Take(MaxListedBends))
            BendRows.Add(DescribeBend(bend));
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WaveguideConnectionViewModel.LossDb) or null or "")
            Refresh();
    }

    private static string DescribeBends(ConnectionLossBreakdown loss)
    {
        if (loss.BendCount == 0)
            return Translate("Routing.Loss.NoBends");
        double min = loss.MinBendRadiusMicrometers!.Value;
        double max = loss.MaxBendRadiusMicrometers!.Value;
        return Math.Abs(max - min) < 0.05
            ? Format("Routing.Loss.BendSummarySingleFormat", loss.BendCount, min)
            : Format("Routing.Loss.BendSummaryRangeFormat", loss.BendCount, min, max);
    }

    private static string DescribeBend(BendLossEntry bend) =>
        Format(bend.IsSampledCurve ? "Routing.Loss.CurveRowFormat" : "Routing.Loss.BendRowFormat",
            bend.RadiusMicrometers, bend.SweepDegrees, FormatDb(bend.LossDb));

    /// <summary>Formats a loss in dB; positive values below the display resolution read "&lt; 0.001 dB".</summary>
    public static string FormatDb(double lossDb) =>
        lossDb > 0 && lossDb < DisplayResolutionDb
            ? Translate("Routing.Loss.BelowResolution")
            : Format("Routing.Loss.DbFormat", lossDb);

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);

    private static string Format(string key, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Translate(key), args);
}
