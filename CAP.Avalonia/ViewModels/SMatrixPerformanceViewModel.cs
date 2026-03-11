using CAP_Core.LightCalculation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// ViewModel for S-Matrix performance diagnostics panel.
/// Displays sparsity statistics and memory usage of the system S-Matrix.
/// </summary>
public partial class SMatrixPerformanceViewModel : ObservableObject
{
    private readonly SMatrixStatisticsAnalyzer _analyzer = new();

    [ObservableProperty]
    private string _matrixSizeText = "-";

    [ObservableProperty]
    private string _totalElementsText = "-";

    [ObservableProperty]
    private string _nonZeroElementsText = "-";

    [ObservableProperty]
    private string _sparsityText = "-";

    [ObservableProperty]
    private string _memoryUsageText = "-";

    [ObservableProperty]
    private string _memorySavingsText = "-";

    [ObservableProperty]
    private string _storageTypeText = "-";

    [ObservableProperty]
    private string _statusText = "No analysis run yet";

    [ObservableProperty]
    private bool _hasAnalysis;

    [ObservableProperty]
    private bool _isAnalyzing;

    private SMatrixStatistics? _lastStats;

    /// <summary>
    /// Analyzes the given S-Matrix and updates the displayed statistics.
    /// </summary>
    /// <param name="matrix">The S-Matrix to analyze</param>
    [RelayCommand]
    public void AnalyzeMatrix(SMatrix? matrix)
    {
        if (matrix == null)
        {
            ResetStatistics();
            StatusText = "No S-Matrix available";
            return;
        }

        IsAnalyzing = true;
        StatusText = "Analyzing...";

        try
        {
            _lastStats = _analyzer.AnalyzeMatrix(matrix);
            UpdateDisplayedStatistics(_lastStats);
            HasAnalysis = true;
            StatusText = "Analysis complete";
        }
        catch (Exception ex)
        {
            StatusText = $"Analysis failed: {ex.Message}";
            ResetStatistics();
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    /// <summary>
    /// Updates all displayed statistics from the analysis result.
    /// </summary>
    private void UpdateDisplayedStatistics(SMatrixStatistics stats)
    {
        MatrixSizeText = $"{stats.MatrixSize} × {stats.MatrixSize}";
        TotalElementsText = FormatNumber(stats.TotalElements);
        NonZeroElementsText = FormatNumber(stats.NonZeroElements);
        SparsityText = $"{stats.SparsityPercentage:F2}%";
        MemoryUsageText = stats.FormattedMemorySize;
        StorageTypeText = stats.IsSparse ? "Sparse (optimized)" : "Dense (unoptimized)";

        double savings = _analyzer.CalculateMemorySavings(stats);
        if (stats.IsSparse && savings > 1.0)
        {
            MemorySavingsText = $"{savings:F1}x savings vs dense";
        }
        else
        {
            MemorySavingsText = "N/A";
        }
    }

    /// <summary>
    /// Resets all statistics to default values.
    /// </summary>
    private void ResetStatistics()
    {
        MatrixSizeText = "-";
        TotalElementsText = "-";
        NonZeroElementsText = "-";
        SparsityText = "-";
        MemoryUsageText = "-";
        MemorySavingsText = "-";
        StorageTypeText = "-";
        HasAnalysis = false;
        _lastStats = null;
    }

    /// <summary>
    /// Formats large numbers with thousands separators.
    /// </summary>
    private string FormatNumber(int number)
    {
        return number.ToString("N0");
    }

    /// <summary>
    /// Clears the current analysis.
    /// </summary>
    [RelayCommand]
    private void ClearAnalysis()
    {
        ResetStatistics();
        StatusText = "Analysis cleared";
    }
}
