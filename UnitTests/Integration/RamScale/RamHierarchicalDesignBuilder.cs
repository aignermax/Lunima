using System.Text.Json.Nodes;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Test-only builder for the hierarchical RAM of issue #1366: the 2×4 / 4×4 RAM as
/// <see cref="RamWordCellTemplate"/> instances (the word cell routed once, then frozen)
/// plus a top level of only the address stage, the LOAD/data distribution and the
/// read-mux combine — the inter-cell wires are the whole top-level route. The topology
/// matches the flat <see cref="RamScaleDesignBuilder"/> exactly (same gates, same signal
/// names, same read taps), so the behavioural assertions and the census compare like for
/// like; only the intra-word wiring moved into the frozen cell. The emitted document
/// carries no cached geometry for the top-level wires, so loading it routes exactly the
/// inter-cell wires fresh — the spike's second route measurement.
/// </summary>
internal sealed partial class RamHierarchicalDesignBuilder
{
    private const int OriginX = 400;
    private const int OriginY = 100;
    private const int PitchX = 1200;
    private const int CellGapY = 600;
    private const double ChipMargin = 500;

    private readonly RamWordCellTemplate _template;
    private readonly int _wordCount;
    private readonly int _bitCount;
    private readonly RamGateFactory _factory = new();
    private readonly List<RamWordCellInstance> _instances = new();

    private RamHierarchicalDesignBuilder(RamWordCellTemplate template, int wordCount, int bitCount)
    {
        _template = template;
        _wordCount = wordCount;
        _bitCount = bitCount;
    }

    /// <summary>Builds the hierarchical RAM design JSON for the given word count (2 or 4).</summary>
    public static RamScaleDesign Build(RamWordCellTemplate template, int wordCount, int bitCount)
    {
        if (wordCount != 2 && wordCount != 4)
            throw new ArgumentOutOfRangeException(nameof(wordCount), "only the 1- and 2-address-bit RAMs are modelled");
        return new RamHierarchicalDesignBuilder(template, wordCount, bitCount).Build();
    }

    private int CellAnchorColumn => _wordCount == 4 ? 7 : 3;
    private int MuxCombineColumn => CellAnchorColumn + 14;
    private int MuxOutputColumn => CellAnchorColumn + 15;

    private RamScaleDesign Build()
    {
        int addressBits = _wordCount == 4 ? 2 : 1;
        EmitInstances();
        EmitAddressStage(addressBits);
        if (_wordCount == 4)
            EmitLoadAndDataTrees();
        else
            MergeLoadAndData();
        EmitReadPath();
        return AssembleDocument(addressBits);
    }

    /// <summary>Stamps the cell instances onto a vertical stack right of the address stage.</summary>
    private void EmitInstances()
    {
        double anchorX = OriginX + CellAnchorColumn * PitchX;
        for (int w = 0; w < _wordCount; w++)
        {
            var instance = _template.EmitInstance(w, anchorX, OriginY + w * (_template.Height + CellGapY));
            _instances.Add(instance);
            _factory.RegisterEndpoint(Cell(w), instance.Identifier);
        }
    }

    /// <summary>Four words: LOAD and each data bit fan out onto four cell ports — a wired copy tree per signal.</summary>
    private void EmitLoadAndDataTrees()
    {
        _factory.DistributeInput("LOAD", CellPorts("LOAD"), 0, "CPL");
        for (int i = 0; i < _bitCount; i++)
            _factory.DistributeInput($"D{i}", CellPorts($"D{i}"), 0, $"CPD{i}");
    }

    /// <summary>Two words: LOAD and each data bit merge onto the two instances' internal gate pins (#1025).</summary>
    private void MergeLoadAndData()
    {
        foreach (var instance in _instances)
            SetInputSignal(instance.GateEntriesByRole["EN"], "A", "LOAD");
        for (int i = 0; i < _bitCount; i++)
            foreach (var instance in _instances)
                SetInputSignal(instance.GateEntriesByRole[$"LE{i}"], "A", $"D{i}");
    }

    /// <summary>The W-to-1 read mux per data bit over the cell M ports — the flat pair-tree, arms now cell ports.</summary>
    private void EmitReadPath()
    {
        for (int i = 0; i < _bitCount; i++)
        {
            for (int w = 0; w < _wordCount; w++)
                SetOutputSignal(_instances[w].GateEntriesByRole[$"CP{i}"], "Y2", $"W{w}D{i}");

            var level = _instances.Select((_, w) => (Cell(w), $"M{i}")).ToList();
            int combineIndex = 0;
            bool inputsAreArms = true;
            while (level.Count > 1)
            {
                var next = new List<(string, string)>();
                for (int pair = 0; pair < level.Count; pair += 2)
                {
                    bool isFinal = level.Count == 2;
                    var combine = isFinal ? $"OUT{i}" : $"P{i}_{combineIndex++}";
                    _factory.Emit(RamGateFactory.NandShape, combine,
                        isFinal ? MuxOutputColumn : MuxCombineColumn,
                        isFinal
                            ? $"read output of data bit {i}: the last mux combine"
                            : $"read-mux combine of data bit {i}",
                        outputSignals: isFinal ? new Dictionary<string, string> { ["Y"] = $"R{i}" } : null);
                    var (left, right) = (level[pair], level[pair + 1]);
                    if (inputsAreArms)
                    {
                        _factory.Wire(left.Item1, left.Item2, combine, "A");
                        _factory.Wire(right.Item1, right.Item2, combine, "B");
                    }
                    else
                    {
                        var notLeft = $"N{i}_{combineIndex}";
                        var notRight = $"NR{i}_{combineIndex}";
                        combineIndex++;
                        _factory.Emit(RamGateFactory.NotShape, notLeft, MuxCombineColumn, $"read-mux OR inverter of data bit {i}");
                        _factory.Emit(RamGateFactory.NotShape, notRight, MuxCombineColumn, $"read-mux OR inverter of data bit {i}");
                        _factory.Wire(left.Item1, left.Item2, notLeft, "A");
                        _factory.Wire(right.Item1, right.Item2, notRight, "A");
                        _factory.Wire(notLeft, "Y", combine, "A");
                        _factory.Wire(notRight, "Y", combine, "B");
                    }
                    next.Add((combine, "Y"));
                }
                inputsAreArms = false;
                level = next;
            }
        }
    }

    /// <summary>Assembles the document: top-level gates and wires from the factory, cell and gate entries appended.</summary>
    private RamScaleDesign AssembleDocument(int addressBits)
    {
        double maxX = _instances.Max(i => i.Right);
        double maxY = _instances.Max(i => i.Bottom);
        foreach (var gate in _factory.Groups)
        {
            maxX = Math.Max(maxX, gate["CanvasX"]!.GetValue<double>() + _factory.GateWidth);
            maxY = Math.Max(maxY, gate["CanvasY"]!.GetValue<double>() + RamGateFactory.RowPitch);
        }
        double chipWidth = maxX + ChipMargin;
        double chipHeight = maxY + ChipMargin;
        var document = _factory.BuildDocument(chipWidth, chipHeight);
        var groups = document["Groups"]!.AsArray();
        int gateCount = _factory.Groups.Count;
        foreach (var instance in _instances)
        {
            groups.Add(instance.CellGroupEntry.DeepClone());
            foreach (var gate in instance.GateEntriesByRole.Values)
            {
                groups.Add(gate.DeepClone());
                gateCount++;
            }
        }
        return new RamScaleDesign
        {
            Json = document.ToJsonString(),
            GateCount = gateCount,
            WireCount = _factory.WireCount,
            ChipWidthMicrometers = chipWidth,
            ChipHeightMicrometers = chipHeight,
            AddressSignals = Enumerable.Range(0, addressBits).Select(b => $"A{b}").ToArray(),
            DataSignals = Enumerable.Range(0, _bitCount).Select(i => $"D{i}").ToArray(),
            ReadTaps = Enumerable.Range(0, _bitCount).Select(i => $"R{i}").ToArray(),
        };
    }

    private List<(string, string)> CellPorts(string port) =>
        Enumerable.Range(0, _wordCount).Select(w => (Cell(w), port)).ToList();

    private static void SetInputSignal(JsonObject gateEntry, string pin, string signal) =>
        SetSignal(gateEntry, "InputSignalNames", pin, signal);

    private static void SetOutputSignal(JsonObject gateEntry, string pin, string signal) =>
        SetSignal(gateEntry, "OutputSignalNames", pin, signal);

    private static void SetSignal(JsonObject gateEntry, string property, string pin, string signal)
    {
        var assignment = gateEntry["TruthTablePinAssignment"]!.AsObject();
        var signals = assignment[property]?.AsObject() ?? new JsonObject();
        signals[pin] = signal;
        assignment[property] = signals;
    }

    private static string Cell(int word) => $"CELL{word}";
}
