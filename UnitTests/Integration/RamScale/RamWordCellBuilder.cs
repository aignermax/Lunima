using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Test-only builder for ONE word cell of the hierarchical RAM spike (issue #1366): the
/// register bits with their write/read gates of a single word — enable chain
/// (EN = NAND(select, LOAD), IW = NOT(EN)), the hold/load distribution trees, one
/// register with read-tap copy per bit, and the word's read-mux arms
/// (M = NAND(stored bit, select)) — exactly the per-word slice of the flat
/// <see cref="RamScaleDesignBuilder"/> pattern, laid out standalone. The emitted
/// document carries the intra-cell wires as plain connections with no cached geometry,
/// so the test loads it, routes it once, and freezes the result into the instanced
/// group template (<see cref="RamWordCellTemplate"/>). Which gate pin forms which cell
/// port is reported in <see cref="RamWordCellDesign.Ports"/>.
/// <para>
/// Floorplan (issue #1400 — the compact auto-row layout left 17 of 44 wires on the
/// blocked fallback): every bit slice owns a main row (H, REG, CP, M — forward flow
/// left to right) plus the channel row below it, which carries the backward hold
/// feedback and the tree-to-arm wires; row 0 stays empty as a horizontal highway for
/// the two cell-spanning select wires. LE sits in the channel row of the arms column
/// so its output reaches REG.B without crossing H. The distribution-tree copies are
/// hand-placed next to their targets: the hold/load pair copies in the corridor column
/// left of the arms, the select copies in the mux column (the pair copies between their
/// two arms, the intermediate copy between them) — only the root → intermediate select
/// wire still spans the cell, plus the short forward root → EN.B hop.
/// </para>
/// </summary>
internal sealed class RamWordCellBuilder
{
    private const int SelTreeColumn = 0;
    private const int EnColumn = 1;
    private const int CpeaColumn = 3;
    private const int IwColumn = 4;
    private const int HoldTreeRootColumn = 4;
    private const int LoadTreeRootColumn = 6;
    private const int TreeLeafColumn = 7;
    private const int ArmsColumn = 8;
    private const int RegColumn = 9;
    private const int TapColumn = 10;
    private const int MuxArmColumn = 11;
    private const int SupportedBitCount = 4;
    private const double ChipMargin = 500;

    private readonly int _bitCount;
    private readonly RamGateFactory _factory = new();
    private readonly Dictionary<string, string> _gateRoles = new();

    private RamWordCellBuilder(int bitCount) => _bitCount = bitCount;

    /// <summary>Builds the standalone, unrouted word-cell document.</summary>
    public static RamWordCellDesign Build(int bitCount)
    {
        if (bitCount != SupportedBitCount)
            throw new NotSupportedException(
                $"the re-floorplanned word cell (issue #1400) supports exactly the {SupportedBitCount}-bit word");
        return new RamWordCellBuilder(bitCount).Build();
    }

    /// <summary>
    /// The instance gate name for a cell gate: cell gates carry the flat RAM's word-0
    /// names (<c>LE03</c>, <c>CSEL0_2</c>, <c>EN0</c>); instancing swaps the word digit.
    /// </summary>
    public static string InstanceGateName(string cellGateName, int word) =>
        Regex.Replace(cellGateName, @"^([A-Z]+)0(.*)$", $"${{1}}{word}$2");

    private RamWordCellDesign Build()
    {
        EmitGates();
        // Wire order is route priority: the contention repair stamps the LATER wire of a
        // crossing blocked, so the short local hops — least routing freedom — come first
        // and the cell-spanning select trunks, which may detour through the empty highway
        // row 0, the empty left column and the empty bottom row, come last.
        WireSlices();
        WireTreeLeaves();
        WireTreeTrunks();
        WireSelectTrunks();

        var document = BuildDocumentWithBounds();
        return new RamWordCellDesign
        {
            Json = document.ToJsonString(),
            GateCount = _factory.Groups.Count,
            WireCount = _factory.WireCount,
            ChipWidthMicrometers = document["ChipWidthMicrometers"]!.GetValue<double>(),
            ChipHeightMicrometers = document["ChipHeightMicrometers"]!.GetValue<double>(),
            GateRoles = _gateRoles,
            Ports = BuildPorts(),
        };
    }

    /// <summary>
    /// All cell gates: the enable chain (EN = NAND(select, LOAD), its copy CPEA, the IW
    /// inverter), the hold/load distribution trees (a root copy near the source and one
    /// pair copy per two bits in the corridor column, each at the channel row between its
    /// two targets), the select tree (root, the left copy feeding EN.B and the first pair
    /// copy, and one pair copy per two mux arms in the mux column), and the register bits.
    /// </summary>
    private void EmitGates()
    {
        Emit(RamGateFactory.NandShape, "EN0", EnColumn, "EN", "inverted load enable of the word: EN = NAND(select, LOAD)", row: 1);
        Emit(RamGateFactory.CopyShape, "CPEA0", CpeaColumn, "CPEA", "copy: the inverted enable onto the inverter and the hold-arm tree", row: 1);
        Emit(RamGateFactory.NotShape, "IW0", IwColumn, "IW", "inverter: the true word load enable", row: 1);
        Emit(RamGateFactory.CopyShape, "CPEB0_0", HoldTreeRootColumn, "CPEB_0", "fan-out copy of CPEA0 onto the hold-arm pair copies", row: 2);
        Emit(RamGateFactory.CopyShape, "CPEB0_1", TreeLeafColumn, "CPEB_1", "fan-out pair copy of the hold arms of bits 0 and 1", row: 2);
        Emit(RamGateFactory.CopyShape, "CPEB0_2", TreeLeafColumn, "CPEB_2", "fan-out pair copy of the hold arms of bits 2 and 3", row: 6);
        Emit(RamGateFactory.CopyShape, "CPI0_0", LoadTreeRootColumn, "CPI_0", "fan-out copy of IW0 onto the load-arm pair copies", row: 1);
        Emit(RamGateFactory.CopyShape, "CPI0_1", TreeLeafColumn, "CPI_1", "fan-out pair copy of the load arms of bits 0 and 1", row: 3);
        Emit(RamGateFactory.CopyShape, "CPI0_2", TreeLeafColumn, "CPI_2", "fan-out pair copy of the load arms of bits 2 and 3", row: 7);
        Emit(RamGateFactory.CopyShape, "CSEL0R", SelTreeColumn, "CSELR",
            "root of the word-select fan-out tree (its A pin is the cell's SEL port)", row: 1);
        Emit(RamGateFactory.CopyShape, "CSEL0_0", MuxArmColumn, "CSEL_0",
            "select copy: feeds the two mux-arm pair copies", row: 4);
        Emit(RamGateFactory.CopyShape, "CSEL0_1", MuxArmColumn, "CSEL_1",
            "select pair copy of mux arms 0 and 1", row: 2);
        Emit(RamGateFactory.CopyShape, "CSEL0_2", MuxArmColumn, "CSEL_2",
            "select pair copy of mux arms 2 and 3", row: 6);
        for (int i = 0; i < _bitCount; i++)
        {
            int mainRow = 2 * i + 1;
            int channelRow = mainRow + 1;
            Emit(RamGateFactory.NandShape, H(i), ArmsColumn, $"H{i}", $"hold arm of bit D{i}: H = NAND(R{i}, EN)", row: mainRow);
            Emit(RamGateFactory.NandShape, Le(i), ArmsColumn, $"LE{i}", $"load arm of bit D{i}: LE = NAND(D{i}, IW)", row: channelRow);
            Emit(RamGateFactory.NandShape, Reg(i), RegColumn, $"REG{i}", $"register of bit D{i}: REG = NAND(H, LE)", row: mainRow, isRegister: true);
            Emit(RamGateFactory.CopyShape, Cp(i), TapColumn, $"CP{i}", $"read tap of stored bit D{i}: one arm the hold feedback, one the read mux", row: mainRow);
            Emit(RamGateFactory.NandShape, M(i), MuxArmColumn, $"M{i}", $"read-mux arm of bit D{i}: M = NAND(stored bit, select)", row: mainRow);
        }
    }

    /// <summary>The register-loop wires of every bit slice — the shortest, least flexible hops.</summary>
    private void WireSlices()
    {
        for (int i = 0; i < _bitCount; i++)
        {
            _factory.Wire(H(i), "Y", Reg(i), "A");
            _factory.Wire(Le(i), "Y", Reg(i), "B");
            _factory.Wire(Reg(i), "Y", Cp(i), "A");
            _factory.Wire(Cp(i), "Y1", H(i), "A");
            _factory.Wire(Cp(i), "Y2", M(i), "A");
        }
    }

    /// <summary>The tree leaf wires: the pair copies onto their arm gates, select included.</summary>
    private void WireTreeLeaves()
    {
        for (int pair = 0; pair < _bitCount / 2; pair++)
        {
            string holdLeaf = $"CPEB0_{pair + 1}";
            _factory.Wire(holdLeaf, "Y1", H(2 * pair), "B");
            _factory.Wire(holdLeaf, "Y2", H(2 * pair + 1), "B");
            string loadLeaf = $"CPI0_{pair + 1}";
            _factory.Wire(loadLeaf, "Y1", Le(2 * pair), "B");
            _factory.Wire(loadLeaf, "Y2", Le(2 * pair + 1), "B");
            string selectLeaf = $"CSEL0_{pair + 1}";
            _factory.Wire(selectLeaf, "Y1", M(2 * pair), "B");
            _factory.Wire(selectLeaf, "Y2", M(2 * pair + 1), "B");
        }
    }

    /// <summary>The tree trunk wires: sources onto the root copies, root copies onto the pair copies.</summary>
    private void WireTreeTrunks()
    {
        _factory.Wire("EN0", "Y", "CPEA0", "A");
        _factory.Wire("CPEA0", "Y1", "IW0", "A");
        _factory.Wire("CPEA0", "Y2", "CPEB0_0", "A");
        _factory.Wire("IW0", "Y", "CPI0_0", "A");
        _factory.Wire("CPEB0_0", "Y1", "CPEB0_1", "A");
        _factory.Wire("CPEB0_0", "Y2", "CPEB0_2", "A");
        _factory.Wire("CPI0_0", "Y1", "CPI0_1", "A");
        _factory.Wire("CPI0_0", "Y2", "CPI0_2", "A");
    }

    /// <summary>
    /// The select trunks, routed last: the short forward hop onto EN.B, the two verticals
    /// down the mux column, and the one cell-spanning wire (root → CSEL0_0) very last, so
    /// the contention repair sacrifices it — the wire with the whole highway row to detour
    /// through — rather than a local hop.
    /// </summary>
    private void WireSelectTrunks()
    {
        _factory.Wire("CSEL0R", "Y1", "EN0", "B");
        _factory.Wire("CSEL0_0", "Y1", "CSEL0_1", "A");
        _factory.Wire("CSEL0_0", "Y2", "CSEL0_2", "A");
        _factory.Wire("CSEL0R", "Y2", "CSEL0_0", "A");
    }

    private void Emit(string shape, string name, int column, string role, string description, int? row = null, bool isRegister = false)
    {
        _factory.Emit(shape, name, column, row, description, isRegister: isRegister);
        _gateRoles.Add(role, name);
    }

    private Dictionary<string, (string Role, string Pin)> BuildPorts()
    {
        var ports = new Dictionary<string, (string, string)>
        {
            ["SEL"] = ("CSELR", "A"),
            ["LOAD"] = ("EN", "A"),
        };
        for (int i = 0; i < _bitCount; i++)
        {
            ports[$"D{i}"] = ($"LE{i}", "A");
            ports[$"M{i}"] = ($"M{i}", "Y");
        }
        return ports;
    }

    private JsonObject BuildDocumentWithBounds()
    {
        double chipWidth = _factory.Groups.Max(g => g["CanvasX"]!.GetValue<double>()) + _factory.GateWidth + ChipMargin;
        double chipHeight = _factory.Groups.Max(g => g["CanvasY"]!.GetValue<double>()) + RamGateFactory.RowPitch + ChipMargin;
        return _factory.BuildDocument(chipWidth, chipHeight);
    }

    private static string H(int bit) => $"H0{bit}";
    private static string Le(int bit) => $"LE0{bit}";
    private static string Reg(int bit) => $"REG0{bit}";
    private static string Cp(int bit) => $"CP0{bit}";
    private static string M(int bit) => $"M0{bit}";
}

/// <summary>The built word-cell document plus the role/port map the template extraction needs.</summary>
public sealed class RamWordCellDesign
{
    public required string Json { get; init; }
    public required int GateCount { get; init; }
    public required int WireCount { get; init; }
    public required double ChipWidthMicrometers { get; init; }
    public required double ChipHeightMicrometers { get; init; }

    /// <summary>Semantic role key → cell gate name (e.g. <c>LE2</c> → <c>LE02</c>, <c>CSEL_0</c> → <c>CSEL0_0</c>).</summary>
    public required IReadOnlyDictionary<string, string> GateRoles { get; init; }

    /// <summary>Cell port name → the gate role and pin behind it (SEL, LOAD, D0–D3 in, M0–M3 out).</summary>
    public required IReadOnlyDictionary<string, (string Role, string Pin)> Ports { get; init; }

    /// <summary>Writes the design JSON to a fresh temp .lun file and returns its path.</summary>
    public string WriteToTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ram-word-cell-{Guid.NewGuid():N}.lun");
        File.WriteAllText(path, Json);
        return path;
    }
}
