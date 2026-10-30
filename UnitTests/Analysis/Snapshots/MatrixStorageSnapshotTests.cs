using System.Numerics;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using MathNet.Numerics.LinearAlgebra.Storage;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;

namespace UnitTests.Analysis.Snapshots;

public sealed class MatrixStorageSnapshotTests
{
    [Fact]
    public async Task CapturedSparseStorage_OwnsValuesColumnIndicesAndRowPointers()
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        var captured = TruthTableSnapshot.Capture(group, ["a", "b"], ["y"], [], 0.25, 1550);
        var matrix = group.ChildComponents.Single().WaveLengthToSMatrixMap.Values.Single();
        var storage = matrix.SMat.Storage.ShouldBeOfType<SparseCompressedRowMatrixStorage<Complex>>();
        storage.Values.AsSpan().Clear();
        storage.ColumnIndices.AsSpan().Clear();
        storage.RowPointers.AsSpan().Clear();

        var table = await captured.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new[] { 0d, 0.5, 0.5, 1d };
        for (var row = 0; row < expected.Length; row++)
            table.Rows[row].Outputs["y"].Power.ShouldBe(expected[row], 0.001);
    }

    [Fact]
    public async Task AliasedSourcePins_KeepOneUnitDriveAtTheSharedFlowEndpoint()
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        group.AddExternalPin(new GroupPin
        {
            Name = "alias", InternalPin = group.ExternalPins.Single(pin => pin.Name == "a").InternalPin
        });
        var captured = TruthTableSnapshot.Capture(group, ["a", "alias"], ["y"], [], 0.25, 1550);
        var table = await captured.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new[] { 0d, 0.5, 0.5, 0.5 };
        for (var row = 0; row < expected.Length; row++)
            table.Rows[row].Outputs["y"].Power.ShouldBe(expected[row], 0.001);
    }
}
