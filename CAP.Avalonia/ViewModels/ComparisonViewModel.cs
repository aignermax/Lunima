using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Comparison;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the side-by-side design comparison view.
/// Loads two .cappro files and displays metrics, differences, and component lists.
/// </summary>
public partial class ComparisonViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Load two designs to compare.";

    [ObservableProperty]
    private DesignSnapshot? _designA;

    [ObservableProperty]
    private DesignSnapshot? _designB;

    [ObservableProperty]
    private ComparisonReport? _report;

    [ObservableProperty]
    private bool _hasReport;

    /// <summary>
    /// Components in Design A for display.
    /// </summary>
    public ObservableCollection<SnapshotComponent> ComponentsA { get; } = new();

    /// <summary>
    /// Components in Design B for display.
    /// </summary>
    public ObservableCollection<SnapshotComponent> ComponentsB { get; } = new();

    /// <summary>
    /// Topology differences between the two designs.
    /// </summary>
    public ObservableCollection<TopologyDifference> Differences { get; } = new();

    /// <summary>
    /// Metrics comparison rows for display.
    /// </summary>
    public ObservableCollection<MetricsRow> MetricsRows { get; } = new();

    /// <summary>
    /// File dialog service for open/save dialogs.
    /// </summary>
    public IFileDialogService? FileDialogService { get; set; }

    [RelayCommand]
    private async Task LoadDesignA()
    {
        var snapshot = await LoadDesignFile("Load Design A");
        if (snapshot == null) return;

        DesignA = snapshot;
        RefreshComponentList(ComponentsA, snapshot);
        StatusText = $"Design A: {snapshot.Name} ({snapshot.Components.Count} components)";
        TryRunComparison();
    }

    [RelayCommand]
    private async Task LoadDesignB()
    {
        var snapshot = await LoadDesignFile("Load Design B");
        if (snapshot == null) return;

        DesignB = snapshot;
        RefreshComponentList(ComponentsB, snapshot);
        StatusText = $"Design B: {snapshot.Name} ({snapshot.Components.Count} components)";
        TryRunComparison();
    }

    [RelayCommand]
    private async Task ExportReport()
    {
        if (Report == null || FileDialogService == null) return;

        var path = await FileDialogService.ShowSaveFileDialogAsync(
            "Export Comparison Report", "txt",
            "Text Files|*.txt|All Files|*.*");

        if (path == null) return;

        await ComparisonReportExporter.ExportToFileAsync(Report, path);
        StatusText = $"Report exported to {Path.GetFileName(path)}";
    }

    private async Task<DesignSnapshot?> LoadDesignFile(string title)
    {
        if (FileDialogService == null)
        {
            StatusText = "File dialog not available.";
            return null;
        }

        var path = await FileDialogService.ShowOpenFileDialogAsync(
            title, "Connect-A-PIC Pro Files|*.cappro|All Files|*.*");

        if (string.IsNullOrEmpty(path)) return null;

        try
        {
            return await DesignLoader.LoadAsync(path);
        }
        catch (Exception ex)
        {
            StatusText = $"Load failed: {ex.Message}";
            return null;
        }
    }

    private void TryRunComparison()
    {
        if (DesignA == null || DesignB == null)
        {
            HasReport = false;
            return;
        }

        Report = ComparisonReport.Create(DesignA, DesignB);
        HasReport = true;

        RefreshMetricsRows();
        RefreshDifferences();

        StatusText = $"Comparing {DesignA.Name} vs {DesignB.Name} " +
                     $"— {Report.Differences.Count} difference(s)";
    }

    private void RefreshMetricsRows()
    {
        MetricsRows.Clear();
        if (Report == null) return;

        var a = Report.MetricsA;
        var b = Report.MetricsB;

        MetricsRows.Add(new MetricsRow("Components", a.ComponentCount, b.ComponentCount));
        MetricsRows.Add(new MetricsRow("Connections", a.ConnectionCount, b.ConnectionCount));
        MetricsRows.Add(new MetricsRow("Unique Types", a.UniqueComponentTypes, b.UniqueComponentTypes));
        MetricsRows.Add(new MetricsRow("Avg Conn/Comp", a.AverageConnectionsPerComponent, b.AverageConnectionsPerComponent));
        MetricsRows.Add(new MetricsRow("Est. Loss (dB)", a.EstimatedTotalLossDb, b.EstimatedTotalLossDb));
        MetricsRows.Add(new MetricsRow("Complexity", a.ComplexityScore, b.ComplexityScore));
    }

    private void RefreshDifferences()
    {
        Differences.Clear();
        if (Report == null) return;

        foreach (var diff in Report.Differences)
        {
            Differences.Add(diff);
        }
    }

    private static void RefreshComponentList(
        ObservableCollection<SnapshotComponent> target,
        DesignSnapshot snapshot)
    {
        target.Clear();
        foreach (var comp in snapshot.Components)
        {
            target.Add(comp);
        }
    }
}

/// <summary>
/// A single row in the metrics comparison table.
/// </summary>
public class MetricsRow
{
    public string Label { get; }
    public double ValueA { get; }
    public double ValueB { get; }
    public double Delta => ValueB - ValueA;
    public string DeltaFormatted => Delta >= 0 ? $"+{Delta:F2}" : $"{Delta:F2}";

    public MetricsRow(string label, double valueA, double valueB)
    {
        Label = label;
        ValueA = valueA;
        ValueB = valueB;
    }
}
