using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Storage;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>
/// Owns compact primitive matrix copies. Capture copies storage only; GUID transfer expansion
/// and iteration happen on the worker, without retaining a MathNet matrix or its mutable arrays.
/// </summary>
internal sealed class MatrixValuesSnapshot
{
    private readonly int[] _rows;
    private readonly int[] _columns;
    private readonly Complex[] _values;
    private readonly bool _compressedRows;

    private MatrixValuesSnapshot(int[] rows, int[] columns, Complex[] values, bool compressedRows)
    {
        _rows = rows;
        _columns = columns;
        _values = values;
        _compressedRows = compressedRows;
    }

    internal static MatrixValuesSnapshot Capture(Matrix<Complex> matrix)
    {
        if (matrix.Storage is SparseCompressedRowMatrixStorage<Complex> sparse)
            return new(sparse.RowPointers.AsSpan().ToArray(),
                sparse.ColumnIndices.AsSpan(0, sparse.ValueCount).ToArray(),
                sparse.Values.AsSpan(0, sparse.ValueCount).ToArray(), compressedRows: true);

        var values = matrix.EnumerateIndexed(Zeros.AllowSkip).ToArray();
        return new(values.Select(value => value.Item1).ToArray(),
            values.Select(value => value.Item2).ToArray(),
            values.Select(value => value.Item3).ToArray(), compressedRows: false);
    }

    internal int Count => _values.Length;

    internal IEnumerable<(int Row, int Column, Complex Value)> Entries()
    {
        if (_compressedRows)
        {
            for (var row = 0; row < _rows.Length - 1; row++)
                for (var index = _rows[row]; index < _rows[row + 1]; index++)
                    yield return (row, _columns[index], _values[index]);
        }
        else
        {
            for (var index = 0; index < _values.Length; index++)
                yield return (_rows[index], _columns[index], _values[index]);
        }
    }
}
