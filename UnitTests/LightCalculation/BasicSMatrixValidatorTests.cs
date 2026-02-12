using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Validation;
using MathNet.Numerics.LinearAlgebra;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.LightCalculation;

public class BasicSMatrixValidatorTests
{
    private readonly BasicSMatrixValidator _validator = new();

    [Fact]
    public void Validate_ValidUnitary2x2Matrix_ReturnsNoErrors()
    {
        // Arrange - a simple 50/50 beam splitter (unitary)
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());

        double s = 1.0 / Math.Sqrt(2);
        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[0]), new Complex(s, 0) },
            { (pinIds[0], pinIds[1]), new Complex(s, 0) },
            { (pinIds[1], pinIds[0]), new Complex(s, 0) },
            { (pinIds[1], pinIds[1]), new Complex(-s, 0) },
        };
        matrix.SetValues(transfers);

        // Act
        var result = _validator.Validate(matrix);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.HasWarnings.ShouldBeFalse();
        result.Entries.Count.ShouldBe(0);
    }

    [Fact]
    public void Validate_ValidPassiveMatrix_ReturnsNoErrors()
    {
        // Arrange - a passive element with loss (magnitude < 1)
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[1]), new Complex(0.8, 0) },
            { (pinIds[1], pinIds[0]), new Complex(0.8, 0) },
        };
        matrix.SetValues(transfers);

        // Act
        var result = _validator.Validate(matrix);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Entries.Count.ShouldBe(0);
    }

    [Fact]
    public void Validate_NonSquareMatrix_ReturnsError()
    {
        // Arrange - create a matrix and manually set a non-square underlying matrix
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());

        // Use reflection to set a non-square matrix for testing
        var nonSquare = Matrix<Complex>.Build.Dense(2, 3);
        var prop = typeof(SMatrix).GetProperty(nameof(SMatrix.SMat));
        prop!.SetValue(matrix, nonSquare);

        // Act
        var result = _validator.Validate(matrix);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("not square"));
    }

    [Fact]
    public void Validate_MagnitudeExceedsOne_ReturnsError()
    {
        // Arrange - element with magnitude > 1 (physically impossible for passive)
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[1]), new Complex(1.5, 0) },
        };
        matrix.SetValues(transfers);

        // Act
        var result = _validator.Validate(matrix);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("magnitude"));
    }

    [Fact]
    public void Validate_EnergyConservationViolation_ReturnsWarning()
    {
        // Arrange - column sum of |S_ij|^2 > 1
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());

        // Both elements in column 0 are 0.9, so sum of squares = 0.81 + 0.81 = 1.62 > 1
        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[0]), new Complex(0.9, 0) },
            { (pinIds[0], pinIds[1]), new Complex(0.9, 0) },
        };
        matrix.SetValues(transfers);

        // Act
        var result = _validator.Validate(matrix);

        // Assert
        result.HasWarnings.ShouldBeTrue();
        result.Warnings.ShouldContain(e => e.Message.Contains("energy conservation"));
    }

    [Fact]
    public void Validate_NaNValue_ReturnsError()
    {
        // Arrange
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[1]), new Complex(double.NaN, 0) },
        };
        matrix.SetValues(transfers);

        // Act
        var result = _validator.Validate(matrix);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("NaN"));
    }

    [Fact]
    public void Validate_InfinityValue_ReturnsError()
    {
        // Arrange
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());

        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[1]), new Complex(double.PositiveInfinity, 0) },
        };
        matrix.SetValues(transfers);

        // Act
        var result = _validator.Validate(matrix);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("Infinity"));
    }

    [Fact]
    public void Validate_NullMatrix_ReturnsError()
    {
        // Act
        var result = _validator.Validate(null!);

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("null"));
    }

    [Fact]
    public void Validate_EmptyMatrix_ReturnsNoErrors()
    {
        // Arrange - zero-size matrix
        var matrix = new SMatrix(new List<Guid>(), new List<(Guid, double)>());

        // Act
        var result = _validator.Validate(matrix);

        // Assert
        result.IsValid.ShouldBeTrue();
    }
}
