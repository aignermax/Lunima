namespace UnitTests.Integration.RamScale;

/// <summary>
/// The address-stage half of <see cref="RamHierarchicalDesignBuilder"/> (issue #1366):
/// for one address bit the shipped RAM 2x2 pattern (word 0 takes the inverted, word 1
/// the raw address); for two the 2→4 decode SEL = NOT(NAND(s1, s0)) with one select wire
/// per cell. The per-word select fan-out lives inside the cell now — the flat design
/// distributes each select onto five pins, here it is one wire onto the cell's SEL port.
/// </summary>
internal sealed partial class RamHierarchicalDesignBuilder
{
    private void EmitAddressStage(int addressBits)
    {
        if (addressBits == 1)
        {
            _factory.Emit(RamGateFactory.NotShape, "NOTA0", 0, "inversion of address bit A0");
            _factory.SetInputSignal("NOTA0", "A", "A0");
            _factory.Wire("NOTA0", "Y", Cell(0), "SEL");
            SetInputSignal(_instances[1].GateEntriesByRole["CSELR"], "A", "A0");
            return;
        }

        for (int b = 0; b < addressBits; b++)
        {
            _factory.Emit(RamGateFactory.NotShape, NotA(b), 1, $"inversion of address bit A{b}");
            var selectConsumers = SelectDecodeConsumers(b);
            selectConsumers.Add((NotA(b), "A"));
            _factory.DistributeInput($"A{b}", selectConsumers, 0, $"CPA{b}");
            _factory.Distribute(NotA(b), "Y", InvertedSelectConsumers(b), 2, $"CPNA{b}");
        }

        for (int w = 0; w < _wordCount; w++)
        {
            _factory.Emit(RamGateFactory.NandShape, SelNd(w), 3, $"select decode of word {w}: NAND of the two address arms");
            _factory.Emit(RamGateFactory.NotShape, SelInv(w), 4, $"select of word {w}: SEL = NOT(NAND(s1, s0))");
            _factory.Wire(SelNd(w), "Y", SelInv(w), "A");
            _factory.Wire(SelInv(w), "Y", Cell(w), "SEL");
        }
    }

    /// <summary>Consumers of address bit b: the select-decode NANDs of the words whose bit b is set.</summary>
    private List<(string, string)> SelectDecodeConsumers(int addressBit)
    {
        var pin = addressBit == 0 ? "A" : "B";
        return Enumerable.Range(0, _wordCount)
            .Where(w => (w & (1 << addressBit)) != 0)
            .Select(w => (SelNd(w), pin))
            .ToList();
    }

    /// <summary>Consumers of the inverted address bit b: the select-decode NANDs of the words whose bit b is clear.</summary>
    private List<(string, string)> InvertedSelectConsumers(int addressBit)
    {
        var pin = addressBit == 0 ? "A" : "B";
        return Enumerable.Range(0, _wordCount)
            .Where(w => (w & (1 << addressBit)) == 0)
            .Select(w => (SelNd(w), pin))
            .ToList();
    }

    private static string NotA(int bit) => $"NOTA{bit}";
    private static string SelNd(int word) => $"SELND{word}";
    private static string SelInv(int word) => $"SELINV{word}";
}
