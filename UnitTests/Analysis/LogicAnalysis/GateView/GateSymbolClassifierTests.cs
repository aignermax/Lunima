using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using Shouldly;

namespace UnitTests.Analysis.LogicAnalysis.GateView;

/// <summary>Truth table → schematic symbol: every standard function, fan-out copies, registers, and the fall-backs.</summary>
public class GateSymbolClassifierTests
{
    [Theory]
    [InlineData(new[] { false, false, false, true }, GateSymbolKind.And)]
    [InlineData(new[] { true, true, true, false }, GateSymbolKind.Nand)]
    [InlineData(new[] { false, true, true, true }, GateSymbolKind.Or)]
    [InlineData(new[] { true, false, false, false }, GateSymbolKind.Nor)]
    [InlineData(new[] { false, true, true, false }, GateSymbolKind.Xor)]
    [InlineData(new[] { true, false, false, true }, GateSymbolKind.Xnor)]
    [InlineData(new[] { false, true, false, false }, GateSymbolKind.Unknown)]
    public void TwoInputTables_MapToTheirStandardFunction(bool[] outputs, GateSymbolKind expected)
    {
        var table = Table(2, ["Y"], row => [outputs[row]]);

        GateSymbolClassifier.FromTruthTable(table, isRegister: false).ShouldBe(expected);
    }

    [Theory]
    [InlineData(new[] { true, false }, GateSymbolKind.Not)]
    [InlineData(new[] { false, true }, GateSymbolKind.Buffer)]
    [InlineData(new[] { false, false }, GateSymbolKind.Unknown)]
    public void SingleInputTables_AreNotOrBuffer(bool[] outputs, GateSymbolKind expected)
    {
        var table = Table(1, ["Y"], row => [outputs[row]]);

        GateSymbolClassifier.FromTruthTable(table, isRegister: false).ShouldBe(expected);
    }

    [Fact]
    public void ThreeInputNand_IsRecognised()
    {
        var table = Table(3, ["Y"], row => [row != 0b111]);

        GateSymbolClassifier.FromTruthTable(table, isRegister: false).ShouldBe(GateSymbolKind.Nand);
    }

    [Fact]
    public void OneInputCopiedToTwoOutputs_IsFanOut()
    {
        var table = Table(1, ["Y1", "Y2"], row => [row == 1, row == 1]);

        GateSymbolClassifier.FromTruthTable(table, isRegister: false).ShouldBe(GateSymbolKind.FanOut);
    }

    [Fact]
    public void OutputsComputingDifferentFunctions_StayAnUnknownBox()
    {
        // A half-adder cell: Sum = XOR, Carry = AND — no single symbol fits.
        var table = Table(2, ["S", "C"], row => [row is 1 or 2, row == 3]);

        GateSymbolClassifier.FromTruthTable(table, isRegister: false).ShouldBe(GateSymbolKind.Unknown);
    }

    [Fact]
    public void RegisterFlag_WinsOverTheTable()
    {
        var table = Table(2, ["Y"], row => [row != 3]);

        GateSymbolClassifier.FromTruthTable(table, isRegister: true).ShouldBe(GateSymbolKind.Register);
        GateSymbolClassifier.FromStructure(2, 1, isRegister: true).ShouldBe(GateSymbolKind.Register);
    }

    [Theory]
    [InlineData(1, 2, GateSymbolKind.FanOut)]
    [InlineData(2, 1, GateSymbolKind.Unknown)]
    [InlineData(1, 1, GateSymbolKind.Unknown)]
    public void Structure_GivesAFirstGuessBeforeClassification(int inputs, int outputs, GateSymbolKind expected) =>
        GateSymbolClassifier.FromStructure(inputs, outputs, isRegister: false).ShouldBe(expected);

    private static TruthTable Table(int inputCount, string[] outputPins, Func<int, bool[]> outputsOf)
    {
        var inputs = Enumerable.Range(0, inputCount).Select(i => $"I{i}").ToList();
        var rows = new List<TruthTableRow>();
        for (var row = 0; row < 1 << inputCount; row++)
        {
            var bits = inputs.Select((pin, i) => (pin, bit: (row & (1 << i)) != 0)).ToDictionary(p => p.pin, p => p.bit);
            var values = outputsOf(row);
            var outputs = outputPins.Select((pin, i) => (pin, value: new LogicOutputValue(values[i], values[i] ? 1.0 : 0.0)))
                .ToDictionary(p => p.pin, p => p.value);
            rows.Add(new TruthTableRow(bits, outputs));
        }
        return new TruthTable("G", inputs, outputPins, 0.5, 1550, rows);
    }
}
