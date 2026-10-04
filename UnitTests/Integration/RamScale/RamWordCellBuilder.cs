using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Test-only builder for ONE word cell of the hierarchical RAM spike (issue #1366): the
/// register bits with their write/read gates of a single word — enable chain
/// (EN = NAND(select, LOAD), IW = NOT(EN)), the hold/load distribution trees, one
/// register with read-tap copy per bit, and the word's read-mux arms
/// (M = NAND(stored bit, select)) — exactly the per-word slice of the flat
/// <see cref="RamScaleDesignBuilder"/> pattern, laid out standalone and compact. The
/// emitted document carries the intra-cell wires as plain connections with no cached
/// geometry, so the test loads it, routes it once, and freezes the result into the
/// instanced group template (<see cref="RamWordCellTemplate"/>). Which gate pin forms
/// which cell port is reported in <see cref="RamWordCellDesign.Ports"/>.
/// </summary>
internal sealed class RamWordCellBuilder
{
    private const int SelTreeColumn = 0;
    private const int EnColumn = 1;
    private const int CpeaColumn = 3;
    private const int IwColumn = 4;
    private const int HoldTreeColumn = 4;
    private const int LoadTreeColumn = 5;
    private const int LoadColumn = 7;
    private const int HoldColumn = 8;
    private const int RegColumn = 9;
    private const int TapColumn = 10;
    private const int MuxArmColumn = 12;
    private const double ChipMargin = 500;

    private readonly int _bitCount;
    private readonly RamGateFactory _factory = new();
    private readonly Dictionary<string, string> _gateRoles = new();

    private RamWordCellBuilder(int bitCount) => _bitCount = bitCount;

    /// <summary>Builds the standalone, unrouted word-cell document.</summary>
    public static RamWordCellDesign Build(int bitCount) => new RamWordCellBuilder(bitCount).Build();

    /// <summary>
    /// The instance gate name for a cell gate: cell gates carry the flat RAM's word-0
    /// names (<c>LE03</c>, <c>CSEL0_2</c>, <c>EN0</c>); instancing swaps the word digit.
    /// </summary>
    public static string InstanceGateName(string cellGateName, int word) =>
        Regex.Replace(cellGateName, @"^([A-Z]+)0(.*)$", $"${{1}}{word}$2");

    private RamWordCellDesign Build()
    {
        EmitEnableChain();
        EmitSelectTree();
        EmitRegisterBits();

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

    /// <summary>EN = NAND(select, LOAD) with its copy onto the hold tree and the IW inverter.</summary>
    private void EmitEnableChain()
    {
        Emit(RamGateFactory.NandShape, "EN0", EnColumn, "EN", "inverted load enable of the word: EN = NAND(select, LOAD)");
        Emit(RamGateFactory.CopyShape, "CPEA0", CpeaColumn, "CPEA", "copy: the inverted enable onto the inverter and the hold-arm tree");
        Emit(RamGateFactory.NotShape, "IW0", IwColumn, "IW", "inverter: the true word load enable");
        _factory.Wire("EN0", "Y", "CPEA0", "A");
        _factory.Wire("CPEA0", "Y1", "IW0", "A");
        _factory.Distribute("CPEA0", "Y2",
            Enumerable.Range(0, _bitCount).Select(i => (H(i), "B")).ToList(),
            HoldTreeColumn, "CPEB0");
        TrackCopies("CPEB", 3);
        _factory.Distribute("IW0", "Y",
            Enumerable.Range(0, _bitCount).Select(i => (Le(i), "B")).ToList(),
            LoadTreeColumn, "CPI0");
        TrackCopies("CPI", 3);
    }

    /// <summary>
    /// The word-select fan-out tree (EN.B plus every mux arm) rooted at an unconnected
    /// copy whose A pin becomes the cell's SEL port. The root is named outside the
    /// copy counter's space (<c>CSEL0R</c>, not <c>CSEL0_0</c>) so the tree's first
    /// emitted copy can take <c>CSEL0_0</c> without a name collision.
    /// </summary>
    private void EmitSelectTree()
    {
        Emit(RamGateFactory.CopyShape, "CSEL0R", SelTreeColumn, "CSELR",
            "root of the word-select fan-out tree (its A pin is the cell's SEL port)");
        var destinations = new List<(string, string)> { ("EN0", "B") };
        destinations.AddRange(Enumerable.Range(0, _bitCount).Select(i => (M(i), "B")));
        int half = (destinations.Count + 1) / 2;
        _factory.Distribute("CSEL0R", "Y1", destinations.Take(half).ToList(), SelTreeColumn + 1, "CSEL0");
        _factory.Distribute("CSEL0R", "Y2", destinations.Skip(half).ToList(), SelTreeColumn + 1, "CSEL0");
        TrackCopies("CSEL", 3);
    }

    /// <summary>The register bits: hold/load arms, the register, the read-tap copy, the mux arm.</summary>
    private void EmitRegisterBits()
    {
        for (int i = 0; i < _bitCount; i++)
        {
            Emit(RamGateFactory.NandShape, H(i), HoldColumn, $"H{i}", $"hold arm of bit D{i}: H = NAND(R{i}, EN)");
            Emit(RamGateFactory.NandShape, Le(i), LoadColumn, $"LE{i}", $"load arm of bit D{i}: LE = NAND(D{i}, IW)");
            Emit(RamGateFactory.NandShape, Reg(i), RegColumn, $"REG{i}", $"register of bit D{i}: REG = NAND(H, LE)", isRegister: true);
            Emit(RamGateFactory.CopyShape, Cp(i), TapColumn, $"CP{i}", $"read tap of stored bit D{i}: one arm the hold feedback, one the read mux");
            Emit(RamGateFactory.NandShape, M(i), MuxArmColumn, $"M{i}", $"read-mux arm of bit D{i}: M = NAND(stored bit, select)");
            _factory.Wire(H(i), "Y", Reg(i), "A");
            _factory.Wire(Le(i), "Y", Reg(i), "B");
            _factory.Wire(Reg(i), "Y", Cp(i), "A");
            _factory.Wire(Cp(i), "Y1", H(i), "A");
            _factory.Wire(Cp(i), "Y2", M(i), "A");
        }
    }

    private void Emit(string shape, string name, int column, string role, string description, bool isRegister = false)
    {
        _factory.Emit(shape, name, column, description, isRegister: isRegister);
        _gateRoles.Add(role, name);
    }

    /// <summary>Records the copy gates a distribution tree emitted under their role keys.</summary>
    private void TrackCopies(string prefix, int count)
    {
        for (int k = 0; k < count; k++)
            _gateRoles.Add($"{prefix}_{k}", $"{prefix}0_{k}");
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
