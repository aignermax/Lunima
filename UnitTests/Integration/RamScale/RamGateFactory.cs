using System.Text.Json.Nodes;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Test-only emission engine shared by the hierarchical-RAM spike builders (issue #1366):
/// instantiates gates from the shipped NOT / NAND / COPY group templates of
/// <c>examples/Logic Gate Register 2-bit.lun</c> with every identifier re-rolled and the
/// body translated onto a column/row grid — the same idiom
/// <see cref="RamScaleDesignBuilder"/> uses for the flat RAM (#1337), factored so the
/// word-cell builder and the hierarchical top level can place gates without duplicating
/// the JSON mechanics. Also resolves raw endpoint identifiers (cell instances) in wires,
/// so copy-tree distributions can terminate on group external pins.
/// </summary>
internal sealed partial class RamGateFactory
{
    /// <summary>Shape key of the fan-out copy gate template (the shipped CPNL group).</summary>
    public const string CopyShape = "COPY";
    /// <summary>Shape key of the inverter gate template (the shipped NOTL group).</summary>
    public const string NotShape = "NOT";
    /// <summary>Shape key of the NAND gate template.</summary>
    public const string NandShape = "NAND";

    private const string SourceExampleFileName = "Logic Gate Register 2-bit.lun";
    private const int OriginX = 400;
    private const int OriginY = 100;
    private const int PitchX = 1200;
    private const int PitchY = 400;

    private readonly Dictionary<string, JsonObject> _templates;
    private readonly string _formatVersion;
    private readonly List<JsonObject> _groups = new();
    private readonly Dictionary<string, JsonObject> _groupsByName = new();
    private readonly Dictionary<string, string> _endpointIds = new();
    private readonly List<(string Start, string StartPin, string End, string EndPin)> _wires = new();
    private readonly Dictionary<int, int> _nextRowPerColumn = new();
    private readonly Dictionary<string, int> _copyCounters = new();
    private readonly HashSet<string> _usedIdentifiers = new();

    /// <summary>Loads the shipped gate templates from the register example.</summary>
    public RamGateFactory()
    {
        var sourcePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), SourceExampleFileName);
        var source = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
        _formatVersion = source["FormatVersion"]!.GetValue<string>();
        _templates = new Dictionary<string, JsonObject>();
        foreach (var group in source["Groups"]!.AsArray())
        {
            var name = group!["GroupDto"]!["GroupName"]!.GetValue<string>();
            var shape = name == "CPNL" ? CopyShape : name == "NOTL" ? NotShape : NandShape;
            _templates.TryAdd(shape, (JsonObject)group.DeepClone());
        }
        GateWidth = _templates[NandShape]["GroupDto"]!["ExternalPins"]!.AsArray()
            .Max(pin => pin!["RelativeX"]!.GetValue<double>());
    }

    /// <summary>Width of one gate body in micrometers (rightmost external pin offset).</summary>
    public double GateWidth { get; }

    /// <summary>The emitted gate groups in emission order.</summary>
    public IReadOnlyList<JsonObject> Groups => _groups;

    /// <summary>The number of wires emitted so far.</summary>
    public int WireCount => _wires.Count;

    /// <summary>Vertical pitch of the placement grid in micrometers.</summary>
    public static int RowPitch => PitchY;

    /// <summary>Registers an external endpoint alias (e.g. a cell instance) wires may target.</summary>
    public void RegisterEndpoint(string alias, string identifier) => _endpointIds.Add(alias, identifier);

    /// <summary>The identifier a wire endpoint resolves to: an alias, else an emitted gate.</summary>
    public string EndpointIdentifier(string name) =>
        _endpointIds.TryGetValue(name, out var identifier)
            ? identifier
            : _groupsByName[name]["GroupDto"]!["Identifier"]!.GetValue<string>();

    /// <summary>Emits one wire between two endpoints (gate names or registered aliases).</summary>
    public void Wire(string start, string startPin, string end, string endPin) =>
        _wires.Add((start, startPin, end, endPin));

    /// <summary>
    /// Distributes a gate output pin onto every destination with a binary copy tree — one
    /// waveguide per driven signal (load honesty). A single destination wires directly.
    /// </summary>
    public void Distribute(string source, string sourcePin, IReadOnlyList<(string Group, string Pin)> destinations, int column, string copyPrefix)
    {
        if (destinations.Count == 1)
        {
            Wire(source, sourcePin, destinations[0].Group, destinations[0].Pin);
            return;
        }
        var copy = Emit(CopyShape, NextCopyName(copyPrefix), column, $"fan-out copy of {source} (one waveguide per driven signal)");
        Wire(source, sourcePin, copy, "A");
        if (destinations.Count == 2)
        {
            Wire(copy, "Y1", destinations[0].Group, destinations[0].Pin);
            Wire(copy, "Y2", destinations[1].Group, destinations[1].Pin);
            return;
        }
        int half = (destinations.Count + 1) / 2;
        Distribute(copy, "Y1", destinations.Take(half).ToList(), column + 1, copyPrefix);
        Distribute(copy, "Y2", destinations.Skip(half).ToList(), column + 1, copyPrefix);
    }

    /// <summary>
    /// Distributes a network input signal onto its consumers: at most two pins take the
    /// persisted signal-name merge directly (the shipped RAM's cap); wider fan-outs merge
    /// onto a root copy and continue as a wired copy tree.
    /// </summary>
    public void DistributeInput(string signal, IReadOnlyList<(string Group, string Pin)> destinations, int column, string copyPrefix)
    {
        if (destinations.Count <= 2)
        {
            foreach (var (group, pin) in destinations)
                SetInputSignal(group, pin, signal);
            return;
        }
        int half = (destinations.Count + 1) / 2;
        var copy = Emit(CopyShape, NextCopyName(copyPrefix), column, $"fan-out copy of network input {signal}");
        SetInputSignal(copy, "A", signal);
        Distribute(copy, "Y1", destinations.Take(half).ToList(), column + 1, copyPrefix);
        Distribute(copy, "Y2", destinations.Skip(half).ToList(), column + 1, copyPrefix);
    }

    /// <summary>Assigns a network input signal name to one emitted gate's input pin.</summary>
    public void SetInputSignal(string groupName, string pin, string signal)
    {
        var assignment = _groupsByName[groupName]["TruthTablePinAssignment"]!.AsObject();
        var signals = assignment["InputSignalNames"]?.AsObject() ?? new JsonObject();
        signals[pin] = signal;
        assignment["InputSignalNames"] = signals;
    }

    /// <summary>Assigns a signal name to one emitted gate's output pin.</summary>
    public void SetOutputSignal(string groupName, string pin, string signal)
    {
        var assignment = _groupsByName[groupName]["TruthTablePinAssignment"]!.AsObject();
        var signals = assignment["OutputSignalNames"]?.AsObject() ?? new JsonObject();
        signals[pin] = signal;
        assignment["OutputSignalNames"] = signals;
    }

    /// <summary>Assembles the .lun document: the emitted groups, the wires, the chip bounds.</summary>
    public JsonObject BuildDocument(double chipWidth, double chipHeight)
    {
        var document = new JsonObject
        {
            ["FormatVersion"] = _formatVersion,
            ["Components"] = new JsonArray(),
            ["Connections"] = new JsonArray(_wires.Select(w => (JsonNode)new JsonObject
            {
                ["StartComponentIndex"] = 0,
                ["StartPinName"] = w.StartPin,
                ["EndComponentIndex"] = 0,
                ["EndPinName"] = w.EndPin,
                ["StartComponentId"] = EndpointIdentifier(w.Start),
                ["EndComponentId"] = EndpointIdentifier(w.End),
                ["WidthMicrometers"] = 0.5,
                ["BendRadiusMicrometers"] = 10,
            }).ToArray()),
            ["Groups"] = new JsonArray(_groups.Select(g => (JsonNode)g.DeepClone()).ToArray()),
            ["Metadata"] = new JsonObject
            {
                ["PdkVersions"] = new JsonObject(),
                ["Authorship"] = new JsonObject
                {
                    ["Created"] = "2026-10-04",
                    ["Modified"] = "2026-10-04T00:00:00.0000000Z",
                },
            },
            ["ChipWidthMicrometers"] = chipWidth,
            ["ChipHeightMicrometers"] = chipHeight,
        };
        return document;
    }

}
