using System.Numerics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>
/// Exact, length-delimited identity of detached optical definitions. Flow and slider identities
/// are numbered by captured traversal, so cloned cells share a key without sharing live objects.
/// </summary>
internal sealed class SnapshotPhysicsIdentity : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly Dictionary<Guid, int> _identities = new();

    internal int Index(Guid identity)
    {
        if (!_identities.TryGetValue(identity, out var index))
            _identities.Add(identity, index = _identities.Count);
        return index;
    }

    internal void Id(Guid identity) => Write(Index(identity));
    internal void Write(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _hash.AppendData(bytes);
    }
    internal void Write(double value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
        _hash.AppendData(bytes);
    }
    internal void Write(Complex value) { Write(value.Real); Write(value.Imaginary); }
    internal void Write(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Write(bytes.Length);
        _hash.AppendData(bytes);
    }

    internal void Component(ComponentMatrixSnapshot component)
    {
        Write(component.Pins.Length);
        foreach (var pin in component.Pins)
        {
            Write(pin.Name); Write(pin.Number); Id(pin.In); Id(pin.Out);
        }
        Write(component.Sliders.Length);
        foreach (var slider in component.Sliders)
        {
            Id(slider.Id); Write(slider.Number); Write(slider.Value);
            Write(slider.Minimum); Write(slider.Maximum);
        }
        Write(component.Matrices.Length);
        foreach (var pair in component.Matrices.OrderBy(pair => pair.Key))
        {
            Write(pair.Key);
            Matrix(pair.Value);
        }
    }

    private void Matrix(MatrixDefinitionSnapshot matrix)
    {
        Write(matrix.FlowIds.Length);
        foreach (var id in matrix.FlowIds) Id(id);
        Write(matrix.SliderValues.Length);
        foreach (var pair in matrix.SliderValues.OrderBy(pair => Index(pair.Key)))
        {
            Id(pair.Key); Write(pair.Value);
        }
        Write(matrix.Numeric.Count);
        foreach (var transfer in matrix.Numeric.Entries())
        {
            Id(matrix.FlowIds[transfer.Column]); Id(matrix.FlowIds[transfer.Row]); Write(transfer.Value);
        }
        Write(matrix.Formulas.Length);
        foreach (var formula in matrix.Formulas)
        {
            Id(formula.From); Id(formula.To); Write(formula.Expression);
        }
        Write(matrix.Parameters.Length);
        foreach (var parameter in matrix.Parameters)
        {
            Write(parameter.Name); Write(parameter.DefaultValue); Write(parameter.MinValue);
            Write(parameter.MaxValue); Write(parameter.SliderNumber ?? -1);
        }
        Write(matrix.ParametricConnections.Length);
        foreach (var connection in matrix.ParametricConnections)
        {
            Write(connection.FromPin); Write(connection.ToPin);
            Write(connection.MagnitudeFormula); Write(connection.PhaseDegFormula);
        }
    }

    internal void Transfers(IReadOnlyDictionary<(Guid, Guid), Complex> transfers)
    {
        Write(transfers.Count);
        foreach (var pair in transfers.OrderBy(pair => Index(pair.Key.Item1)).ThenBy(pair => Index(pair.Key.Item2)))
        {
            Id(pair.Key.Item1); Id(pair.Key.Item2); Write(pair.Value);
        }
    }

    internal string Finish() => Convert.ToHexString(_hash.GetHashAndReset());
    public void Dispose() => _hash.Dispose();
}
