using System.Numerics;
using MathNet.Numerics.LinearAlgebra;

namespace CAP_Core.LightCalculation;

/// <summary>
/// Analyzes S-Matrix statistics including sparsity, memory usage, and performance metrics.
/// Provides diagnostic data for understanding matrix efficiency in photonic simulations.
/// </summary>
public class SMatrixStatisticsAnalyzer
{
    /// <summary>
    /// Analyzes the given S-Matrix and returns comprehensive statistics.
    /// </summary>
    /// <param name="matrix">The S-Matrix to analyze</param>
    /// <returns>Statistics about the matrix including sparsity and memory usage</returns>
    public SMatrixStatistics AnalyzeMatrix(SMatrix matrix)
    {
        if (matrix?.SMat == null)
        {
            return new SMatrixStatistics
            {
                TotalElements = 0,
                NonZeroElements = 0,
                SparsityPercentage = 0,
                MatrixSize = 0,
                EstimatedMemoryBytes = 0,
                IsSparse = false
            };
        }

        int totalElements = matrix.SMat.RowCount * matrix.SMat.ColumnCount;
        int nonZeroElements = CountNonZeroElements(matrix.SMat);
        int matrixSize = matrix.SMat.RowCount;
        bool isSparse = matrix.SMat.Storage.IsDense == false;

        double sparsity = totalElements > 0
            ? (1.0 - (double)nonZeroElements / totalElements) * 100.0
            : 0.0;

        long estimatedMemory = CalculateEstimatedMemory(matrixSize, nonZeroElements, isSparse);

        return new SMatrixStatistics
        {
            TotalElements = totalElements,
            NonZeroElements = nonZeroElements,
            SparsityPercentage = sparsity,
            MatrixSize = matrixSize,
            EstimatedMemoryBytes = estimatedMemory,
            IsSparse = isSparse
        };
    }

    /// <summary>
    /// Counts non-zero elements in the matrix.
    /// Uses a threshold for floating-point comparison.
    /// </summary>
    private int CountNonZeroElements(Matrix<Complex> matrix)
    {
        const double threshold = 1e-15;
        int count = 0;

        for (int i = 0; i < matrix.RowCount; i++)
        {
            for (int j = 0; j < matrix.ColumnCount; j++)
            {
                Complex value = matrix[i, j];
                if (value.Magnitude > threshold)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Estimates memory usage based on matrix size and storage type.
    /// </summary>
    /// <param name="size">Matrix dimension (rows = columns)</param>
    /// <param name="nonZeroCount">Number of non-zero elements</param>
    /// <param name="isSparse">Whether the matrix uses sparse storage</param>
    /// <returns>Estimated memory in bytes</returns>
    private long CalculateEstimatedMemory(int size, int nonZeroCount, bool isSparse)
    {
        const int complexSize = 16; // Complex = 2 doubles = 16 bytes
        const int intSize = 4;

        if (isSparse)
        {
            // Sparse: store value + row index + column index for each non-zero
            return nonZeroCount * (complexSize + intSize + intSize);
        }
        else
        {
            // Dense: store all elements
            return (long)size * size * complexSize;
        }
    }

    /// <summary>
    /// Compares memory efficiency between sparse and dense storage.
    /// </summary>
    /// <param name="stats">Current matrix statistics</param>
    /// <returns>Memory savings factor (e.g., 10.0 means 10x less memory with sparse)</returns>
    public double CalculateMemorySavings(SMatrixStatistics stats)
    {
        if (!stats.IsSparse || stats.MatrixSize == 0)
            return 1.0;

        const int complexSize = 16;
        long denseMemory = (long)stats.MatrixSize * stats.MatrixSize * complexSize;

        if (stats.EstimatedMemoryBytes == 0)
            return 1.0;

        return (double)denseMemory / stats.EstimatedMemoryBytes;
    }
}

/// <summary>
/// Statistics about an S-Matrix including sparsity and memory usage.
/// </summary>
public class SMatrixStatistics
{
    /// <summary>
    /// Total number of elements in the matrix (rows * columns).
    /// </summary>
    public int TotalElements { get; init; }

    /// <summary>
    /// Number of non-zero elements in the matrix.
    /// </summary>
    public int NonZeroElements { get; init; }

    /// <summary>
    /// Percentage of zero elements (0-100).
    /// </summary>
    public double SparsityPercentage { get; init; }

    /// <summary>
    /// Matrix dimension (number of rows/columns, since it's square).
    /// </summary>
    public int MatrixSize { get; init; }

    /// <summary>
    /// Estimated memory usage in bytes.
    /// </summary>
    public long EstimatedMemoryBytes { get; init; }

    /// <summary>
    /// Whether the matrix uses sparse storage.
    /// </summary>
    public bool IsSparse { get; init; }

    /// <summary>
    /// Returns formatted memory size (KB, MB, etc.).
    /// </summary>
    public string FormattedMemorySize
    {
        get
        {
            if (EstimatedMemoryBytes < 1024)
                return $"{EstimatedMemoryBytes} B";
            else if (EstimatedMemoryBytes < 1024 * 1024)
                return $"{EstimatedMemoryBytes / 1024.0:F1} KB";
            else
                return $"{EstimatedMemoryBytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}
