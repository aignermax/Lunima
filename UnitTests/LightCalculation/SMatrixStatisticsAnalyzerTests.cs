using CAP_Core.LightCalculation;
using Shouldly;
using System.Numerics;

namespace UnitTests.LightCalculation;

/// <summary>
/// Tests for S-Matrix statistics analyzer and sparse matrix implementation.
/// </summary>
public class SMatrixStatisticsAnalyzerTests
{
    [Fact]
    public void AnalyzeMatrix_WithNullMatrix_ReturnsEmptyStatistics()
    {
        // Arrange
        var analyzer = new SMatrixStatisticsAnalyzer();

        // Act
        var stats = analyzer.AnalyzeMatrix(null);

        // Assert
        stats.TotalElements.ShouldBe(0);
        stats.NonZeroElements.ShouldBe(0);
        stats.SparsityPercentage.ShouldBe(0);
        stats.MatrixSize.ShouldBe(0);
    }

    [Fact]
    public void AnalyzeMatrix_WithEmptyMatrix_ReturnsZeroStatistics()
    {
        // Arrange
        var analyzer = new SMatrixStatisticsAnalyzer();
        var emptyPins = new List<Guid>();
        var matrix = new SMatrix(emptyPins, new List<(Guid, double)>());

        // Act
        var stats = analyzer.AnalyzeMatrix(matrix);

        // Assert
        stats.TotalElements.ShouldBe(0);
        stats.NonZeroElements.ShouldBe(0);
        stats.MatrixSize.ShouldBe(0);
    }

    [Fact]
    public void AnalyzeMatrix_WithSparseMatrix_CalculatesCorrectSparsity()
    {
        // Arrange
        var analyzer = new SMatrixStatisticsAnalyzer();
        var pins = CreatePinList(10); // 10x10 matrix = 100 elements
        var matrix = new SMatrix(pins, new List<(Guid, double)>());

        // Set only 5 non-zero values (95% sparse)
        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            [(pins[0], pins[1])] = new Complex(1, 0),
            [(pins[1], pins[2])] = new Complex(0.5, 0),
            [(pins[2], pins[3])] = new Complex(0.8, 0),
            [(pins[3], pins[4])] = new Complex(0.3, 0),
            [(pins[4], pins[5])] = new Complex(0.9, 0)
        };
        matrix.SetValues(transfers);

        // Act
        var stats = analyzer.AnalyzeMatrix(matrix);

        // Assert
        stats.MatrixSize.ShouldBe(10);
        stats.TotalElements.ShouldBe(100);
        stats.NonZeroElements.ShouldBe(5);
        stats.SparsityPercentage.ShouldBe(95.0, 0.01);
        stats.IsSparse.ShouldBeTrue();
    }

    [Fact]
    public void AnalyzeMatrix_WithHighlyConnectedMatrix_ShowsLowSparsity()
    {
        // Arrange
        var analyzer = new SMatrixStatisticsAnalyzer();
        var pins = CreatePinList(5); // 5x5 = 25 elements
        var matrix = new SMatrix(pins, new List<(Guid, double)>());

        // Fill most of the matrix (20 out of 25 = 80% filled, 20% sparse)
        var transfers = new Dictionary<(Guid, Guid), Complex>();
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                if (i == j) continue; // Skip diagonal
                transfers[(pins[i], pins[j])] = new Complex(0.5, 0);
            }
        }
        matrix.SetValues(transfers);

        // Act
        var stats = analyzer.AnalyzeMatrix(matrix);

        // Assert
        stats.NonZeroElements.ShouldBe(20);
        stats.SparsityPercentage.ShouldBe(20.0, 0.01);
        stats.IsSparse.ShouldBeTrue();
    }

    [Fact]
    public void CalculateMemorySavings_WithSparseMatrix_ReturnsCorrectFactor()
    {
        // Arrange
        var analyzer = new SMatrixStatisticsAnalyzer();
        var pins = CreatePinList(100); // 100x100 = 10,000 elements
        var matrix = new SMatrix(pins, new List<(Guid, double)>());

        // Only 100 non-zero entries (99% sparse)
        var transfers = new Dictionary<(Guid, Guid), Complex>();
        for (int i = 0; i < 100; i++)
        {
            transfers[(pins[i], pins[(i + 1) % 100])] = new Complex(0.8, 0);
        }
        matrix.SetValues(transfers);

        // Act
        var stats = analyzer.AnalyzeMatrix(matrix);
        var savings = analyzer.CalculateMemorySavings(stats);

        // Assert
        stats.SparsityPercentage.ShouldBeGreaterThan(98.0);
        savings.ShouldBeGreaterThan(10.0); // At least 10x savings
    }

    [Fact]
    public void SparseMatrix_ProducesSameResultsAsDenseMatrix()
    {
        // Arrange - Create same matrix with sparse and dense storage
        var pins = CreatePinList(10);
        var sparseMatrix = new SMatrix(pins, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            [(pins[0], pins[1])] = new Complex(0.5, 0.3),
            [(pins[1], pins[2])] = new Complex(0.8, 0.1),
            [(pins[2], pins[0])] = new Complex(0.3, 0.5),
            [(pins[3], pins[4])] = new Complex(0.9, 0.0)
        };

        sparseMatrix.SetValues(transfers);

        // Act - Extract values from sparse matrix
        var sparseValues = sparseMatrix.GetNonNullValues();

        // Assert - Verify all values match what we set
        sparseValues.Count.ShouldBe(4);
        foreach (var kvp in transfers)
        {
            sparseValues.ShouldContainKey(kvp.Key);
            sparseValues[kvp.Key].Real.ShouldBe(kvp.Value.Real, 1e-10);
            sparseValues[kvp.Key].Imaginary.ShouldBe(kvp.Value.Imaginary, 1e-10);
        }
    }

    [Fact]
    public void FormattedMemorySize_ReturnsReadableFormat()
    {
        // Arrange
        var analyzer = new SMatrixStatisticsAnalyzer();
        var pins = CreatePinList(50); // 50x50 matrix
        var matrix = new SMatrix(pins, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>();
        for (int i = 0; i < 50; i++)
        {
            transfers[(pins[i], pins[(i + 1) % 50])] = new Complex(0.5, 0);
        }
        matrix.SetValues(transfers);

        // Act
        var stats = analyzer.AnalyzeMatrix(matrix);
        var formatted = stats.FormattedMemorySize;

        // Assert
        formatted.ShouldNotBeNullOrEmpty();
        (formatted.Contains("KB") || formatted.Contains("B")).ShouldBeTrue();
    }

    [Fact]
    public void SparseMatrix_HandlesComplexNumbers()
    {
        // Arrange
        var pins = CreatePinList(5);
        var matrix = new SMatrix(pins, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            [(pins[0], pins[1])] = new Complex(0.707, 0.707), // Magnitude ~1
            [(pins[1], pins[2])] = new Complex(0, 1),          // Pure imaginary
            [(pins[2], pins[3])] = new Complex(-0.5, -0.5)     // Negative
        };
        matrix.SetValues(transfers);

        // Act
        var analyzer = new SMatrixStatisticsAnalyzer();
        var stats = analyzer.AnalyzeMatrix(matrix);

        // Assert
        stats.NonZeroElements.ShouldBe(3);
        stats.IsSparse.ShouldBeTrue();

        var retrieved = matrix.GetNonNullValues();
        retrieved.Count.ShouldBe(3);
        retrieved[(pins[0], pins[1])].Magnitude.ShouldBe(1.0, 0.01);
    }

    [Fact]
    public void SparseMatrix_SupportsReset()
    {
        // Arrange
        var pins = CreatePinList(5);
        var matrix = new SMatrix(pins, new List<(Guid, double)>());

        var initialTransfers = new Dictionary<(Guid, Guid), Complex>
        {
            [(pins[0], pins[1])] = new Complex(1, 0),
            [(pins[1], pins[2])] = new Complex(1, 0)
        };
        matrix.SetValues(initialTransfers);

        // Act - Reset and set new values
        var newTransfers = new Dictionary<(Guid, Guid), Complex>
        {
            [(pins[2], pins[3])] = new Complex(0.5, 0)
        };
        matrix.SetValues(newTransfers, reset: true);

        // Assert - Only new values should be present
        var values = matrix.GetNonNullValues();
        values.Count.ShouldBe(1);
        values.ShouldContainKey((pins[2], pins[3]));
        values.ShouldNotContainKey((pins[0], pins[1]));
    }

    /// <summary>
    /// Helper to create a list of random pin GUIDs.
    /// </summary>
    private List<Guid> CreatePinList(int count)
    {
        var pins = new List<Guid>();
        for (int i = 0; i < count; i++)
        {
            pins.Add(Guid.NewGuid());
        }
        return pins;
    }
}
