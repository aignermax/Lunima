using System.Text.Json.Nodes;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Test-only builder that programmatically assembles a word×bit RAM network from the
/// shipped gate prefabs of <c>examples/Logic Gate Register 2-bit.lun</c> (the NOT / NAND /
/// COPY shapes), exactly the way <c>scripts/generate_ram_2x2.py</c> composed the shipped
/// RAM 2x2 (#1142/#1147) — scaled to a parametric word count and bit width. The write path
/// demultiplexes LOAD by the decoded word select (EN = NAND(select, LOAD), IW = NOT(EN));
/// each word is the shipped register pattern (hold arm H = NAND(R, EN), load arm
/// LE = NAND(D, IW), register REG = NAND(H, LE)) with a read-tap copy; the read path is a
/// W-to-1 NAND mux tree per data bit. Gate-output fan-outs are served by copy cascades —
/// one waveguide per driven signal; network inputs reach their consumers through the
/// persisted signal-name merge (#1025), capped at two merged pins like the shipped RAM.
/// The result is a .lun JSON document with no cached route geometry, so loading it routes
/// every wire fresh — the spike's route measurement (issue #1337).
/// </summary>
internal sealed class RamScaleDesignBuilder
{
    private const string SourceExampleFileName = "Logic Gate Register 2-bit.lun";
    private const string CopyShape = "COPY";
    private const string NotShape = "NOT";
    private const string NandShape = "NAND";

    private const int OriginX = 400;
    private const int OriginY = 100;
    private const int PitchX = 1200;
    private const int PitchY = 400;
    private const int ChipMargin = 500;

    private readonly int _wordCount;
    private readonly int _bitCount;
    private readonly Dictionary<string, JsonObject> _templates;
    private readonly string _formatVersion;
    private readonly double _gateWidth;
    private readonly List<JsonObject> _groups = new();
    private readonly Dictionary<string, JsonObject> _groupsByName = new();
    private readonly List<(string Start, string StartPin, string End, string EndPin)> _wires = new();
    private readonly Dictionary<int, int> _nextRowPerColumn = new();
    private readonly Dictionary<string, int> _copyCounters = new();
    private readonly HashSet<string> _usedIdentifiers = new();

    private RamScaleDesignBuilder(int wordCount, int bitCount, JsonObject source)
    {
        _wordCount = wordCount;
        _bitCount = bitCount;
        _formatVersion = source["FormatVersion"]!.GetValue<string>();
        _templates = new Dictionary<string, JsonObject>();
        foreach (var group in source["Groups"]!.AsArray())
        {
            var name = group!["GroupDto"]!["GroupName"]!.GetValue<string>();
            var shape = name == "CPNL" ? CopyShape : name == "NOTL" ? NotShape : NandShape;
            _templates.TryAdd(shape, (JsonObject)group.DeepClone());
        }
        _gateWidth = _templates[NandShape]["GroupDto"]!["ExternalPins"]!.AsArray()
            .Max(pin => pin!["RelativeX"]!.GetValue<double>());
    }

    /// <summary>Builds the RAM design JSON for the given geometry (word count must be 2 or 4).</summary>
    public static RamScaleDesign Build(int wordCount, int bitCount)
    {
        if (wordCount != 2 && wordCount != 4)
            throw new ArgumentOutOfRangeException(nameof(wordCount), "only the 1- and 2-address-bit RAMs are modelled");
        var sourcePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), SourceExampleFileName);
        var source = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
        return new RamScaleDesignBuilder(wordCount, bitCount, source).Build();
    }

    private RamScaleDesign Build()
    {
        int addressBits = _wordCount == 4 ? 2 : 1;
        var columns = new RamScaleLayout(addressBits);

        EmitWordColumns(columns);
        EmitAddressStage(addressBits, columns);
        EmitLoadAndDataDistribution(columns);
        EmitReadPath(columns);

        var document = new JsonObject
        {
            ["FormatVersion"] = _formatVersion,
            ["Components"] = new JsonArray(),
            ["Connections"] = BuildConnections(),
            ["Groups"] = new JsonArray(_groups.Select(g => (JsonNode)g.DeepClone()).ToArray()),
            ["Metadata"] = new JsonObject
            {
                ["PdkVersions"] = new JsonObject(),
                ["Authorship"] = new JsonObject
                {
                    ["Created"] = "2026-10-03",
                    ["Modified"] = "2026-10-03T00:00:00.0000000Z",
                },
            },
        };
        double chipWidth = _groups.Max(g => g["CanvasX"]!.GetValue<double>()) + _gateWidth + ChipMargin;
        double chipHeight = _groups.Max(g => g["CanvasY"]!.GetValue<double>()) + PitchY + ChipMargin;
        document["ChipWidthMicrometers"] = chipWidth;
        document["ChipHeightMicrometers"] = chipHeight;

        return new RamScaleDesign
        {
            Json = document.ToJsonString(),
            GateCount = _groups.Count,
            WireCount = _wires.Count,
            ChipWidthMicrometers = chipWidth,
            ChipHeightMicrometers = chipHeight,
            AddressSignals = Enumerable.Range(0, addressBits).Select(b => $"A{b}").ToArray(),
            DataSignals = Enumerable.Range(0, _bitCount).Select(i => $"D{i}").ToArray(),
            ReadTaps = Enumerable.Range(0, _bitCount).Select(i => $"R{i}").ToArray(),
        };
    }

    private JsonArray BuildConnections() =>
        new(_wires.Select(w => (JsonNode)new JsonObject
        {
            ["StartComponentIndex"] = 0,
            ["StartPinName"] = w.StartPin,
            ["EndComponentIndex"] = 0,
            ["EndPinName"] = w.EndPin,
            ["StartComponentId"] = GroupIdentifier(w.Start),
            ["EndComponentId"] = GroupIdentifier(w.End),
            ["WidthMicrometers"] = 0.5,
            ["BendRadiusMicrometers"] = 10,
        }).ToArray());

    private string GroupIdentifier(string groupName) =>
        _groupsByName[groupName]["GroupDto"]!["Identifier"]!.GetValue<string>();

    /// <summary>Emits every gate of the write/store columns: EN, CPEA, IW, and the per-bit H/LE/REG/CP.</summary>
    private void EmitWordColumns(RamScaleLayout columns)
    {
        for (int w = 0; w < _wordCount; w++)
        {
            Emit(NandShape, En(w), columns.EnColumn, $"inverted load enable of word {w}: EN = NAND(select, LOAD)");
            Emit(CopyShape, Cpea(w), columns.CpeaColumn, $"copy: the inverted enable of word {w} onto the inverter and the hold-arm tree");
            Emit(NotShape, Iw(w), columns.IwColumn, $"inverter: the true word-{w} load enable");
            for (int i = 0; i < _bitCount; i++)
            {
                Emit(NandShape, H(w, i), columns.HoldColumn, $"hold arm of bit D{i}, word {w}: H = NAND(R{i}, EN{w})");
                Emit(NandShape, Le(w, i), columns.LoadColumn, $"load arm of bit D{i}, word {w}: LE = NAND(D{i}, IW{w})");
                Emit(NandShape, Reg(w, i), columns.RegColumn, $"register of bit D{i}, word {w}: REG = NAND(H, LE)", isRegister: true);
                Emit(CopyShape, Cp(w, i), columns.TapColumn, $"read tap of stored word {w} bit D{i}: one arm the hold feedback, one the read mux");
            }
        }
    }

    /// <summary>
    /// Emits the address stage and distributes each word select onto its consumers (the word's
    /// EN gate and the word's read-mux arms). For one address bit the selects are ADDR/NA
    /// themselves (the shipped RAM 2x2 pattern); for two, a 2→4 decode: SEL = NOT(NAND(s1, s0)).
    /// </summary>
    private void EmitAddressStage(int addressBits, RamScaleLayout columns)
    {
        if (addressBits == 1)
        {
            Emit(NotShape, NotA(0), columns.NotColumn, "inversion of address bit A0");
            SetInputSignal(NotA(0), "A", "A0");
            var naDestinations = new List<(string, string)> { (En(0), "B") };
            naDestinations.AddRange(Enumerable.Range(0, _bitCount).Select(i => (MuxArm(0, i), "B")));
            Distribute(NotA(0), "Y", naDestinations, columns.SelectTreeColumn, "CPN");
            var addressDestinations = new List<(string, string)> { (En(1), "B") };
            addressDestinations.AddRange(Enumerable.Range(0, _bitCount).Select(i => (MuxArm(1, i), "B")));
            DistributeInput("A0", addressDestinations, columns.InputCopiesColumn, "CPAX");
            return;
        }

        for (int b = 0; b < addressBits; b++)
        {
            Emit(NotShape, NotA(b), columns.NotColumn, $"inversion of address bit A{b}");
            var selectConsumers = SelectDecodeConsumers(b);
            selectConsumers.Add((NotA(b), "A"));
            DistributeInput($"A{b}", selectConsumers, columns.InputCopiesColumn, $"CPA{b}");
            Distribute(NotA(b), "Y", InvertedSelectConsumers(b), columns.InvertedCopiesColumn, $"CPNA{b}");
        }

        for (int w = 0; w < _wordCount; w++)
        {
            Emit(NandShape, SelNd(w), columns.SelectDecodeColumn, $"select decode of word {w}: NAND of the two address arms");
            Emit(NotShape, SelInv(w), columns.SelectInvertColumn, $"select of word {w}: SEL = NOT(NAND(s1, s0))");
            Wire(SelNd(w), "Y", SelInv(w), "A");
            var selectDestinations = new List<(string, string)> { (En(w), "B") };
            selectDestinations.AddRange(Enumerable.Range(0, _bitCount).Select(i => (MuxArm(w, i), "B")));
            Distribute(SelInv(w), "Y", selectDestinations, columns.SelectTreeColumn, $"CSEL{w}");
        }
    }

    /// <summary>Consumers of address bit b: the select-decode NANDs of the words whose bit b is set (pin A for b=0, B for b=1).</summary>
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

    /// <summary>Distributes LOAD onto the EN gates, each data bit onto its per-word load arms, and wires the register columns.</summary>
    private void EmitLoadAndDataDistribution(RamScaleLayout columns)
    {
        DistributeInput("LOAD",
            Enumerable.Range(0, _wordCount).Select(w => (En(w), "A")).ToList(),
            columns.InputCopiesColumn, "CPL");
        for (int i = 0; i < _bitCount; i++)
        {
            DistributeInput($"D{i}",
                Enumerable.Range(0, _wordCount).Select(w => (Le(w, i), "A")).ToList(),
                columns.InputCopiesColumn, $"CPD{i}");
        }

        for (int w = 0; w < _wordCount; w++)
        {
            Wire(En(w), "Y", Cpea(w), "A");
            Wire(Cpea(w), "Y1", Iw(w), "A");
            Distribute(Cpea(w), "Y2",
                Enumerable.Range(0, _bitCount).Select(i => (H(w, i), "B")).ToList(),
                columns.HoldTreeColumn, $"CPEB{w}");
            Distribute(Iw(w), "Y",
                Enumerable.Range(0, _bitCount).Select(i => (Le(w, i), "B")).ToList(),
                columns.LoadTreeColumn, $"CPI{w}");
            for (int i = 0; i < _bitCount; i++)
            {
                Wire(H(w, i), "Y", Reg(w, i), "A");
                Wire(Le(w, i), "Y", Reg(w, i), "B");
                Wire(Reg(w, i), "Y", Cp(w, i), "A");
                Wire(Cp(w, i), "Y1", H(w, i), "A");
            }
        }
    }

    /// <summary>Emits the W-to-1 read mux per data bit: one select arm per word, NAND pair-tree combine, output tap R.</summary>
    private void EmitReadPath(RamScaleLayout columns)
    {
        for (int i = 0; i < _bitCount; i++)
        {
            for (int w = 0; w < _wordCount; w++)
            {
                Emit(NandShape, MuxArm(w, i), columns.MuxArmColumn,
                    $"read-mux arm of word {w}, data bit {i}: M = NAND(W{w}D{i}, select)");
                Wire(Cp(w, i), "Y2", MuxArm(w, i), "A");
                SetOutputSignal(Cp(w, i), "Y2", $"W{w}D{i}");
            }

            // The arms are active-low (M = NAND(word, select)); a NAND of two arms is their
            // sum, but combining two sums needs OR = NAND(NOT a, NOT b) — the tree alternates.
            var level = Enumerable.Range(0, _wordCount).Select(w => (MuxArm(w, i), "Y")).ToList();
            int combineIndex = 0;
            bool inputsAreArms = true;
            while (level.Count > 1)
            {
                var next = new List<(string, string)>();
                for (int pair = 0; pair < level.Count; pair += 2)
                {
                    bool isFinal = level.Count == 2;
                    var combine = isFinal ? $"OUT{i}" : $"P{i}_{combineIndex++}";
                    Emit(NandShape, combine,
                        isFinal ? columns.MuxOutputColumn : columns.MuxCombineColumn,
                        isFinal
                            ? $"read output of data bit {i}: the last mux combine"
                            : $"read-mux combine of data bit {i}",
                        outputSignals: isFinal ? new Dictionary<string, string> { ["Y"] = $"R{i}" } : null);
                    var (left, right) = (level[pair], level[pair + 1]);
                    if (inputsAreArms)
                    {
                        Wire(left.Item1, left.Item2, combine, "A");
                        Wire(right.Item1, right.Item2, combine, "B");
                    }
                    else
                    {
                        var notLeft = $"N{i}_{combineIndex}";
                        var notRight = $"NR{i}_{combineIndex}";
                        combineIndex++;
                        Emit(NotShape, notLeft, columns.MuxCombineColumn, $"read-mux OR inverter of data bit {i}");
                        Emit(NotShape, notRight, columns.MuxCombineColumn, $"read-mux OR inverter of data bit {i}");
                        Wire(left.Item1, left.Item2, notLeft, "A");
                        Wire(right.Item1, right.Item2, notRight, "A");
                        Wire(notLeft, "Y", combine, "A");
                        Wire(notRight, "Y", combine, "B");
                    }
                    next.Add((combine, "Y"));
                }
                inputsAreArms = false;
                level = next;
            }
        }
    }

    /// <summary>
    /// Distributes a gate output pin onto every destination with a binary copy tree — one
    /// waveguide per driven signal (load honesty). A single destination wires directly.
    /// </summary>
    private void Distribute(string sourceGroup, string sourcePin, IReadOnlyList<(string Group, string Pin)> destinations, int column, string copyPrefix)
    {
        if (destinations.Count == 1)
        {
            Wire(sourceGroup, sourcePin, destinations[0].Group, destinations[0].Pin);
            return;
        }
        var copy = Emit(CopyShape, NextCopyName(copyPrefix), column, $"fan-out copy of {sourceGroup} (one waveguide per driven signal)");
        Wire(sourceGroup, sourcePin, copy, "A");
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
    private void DistributeInput(string signal, IReadOnlyList<(string Group, string Pin)> destinations, int column, string copyPrefix)
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

    private string NextCopyName(string prefix)
    {
        _copyCounters.TryGetValue(prefix, out int count);
        _copyCounters[prefix] = count + 1;
        return $"{prefix}_{count}";
    }

    private void Wire(string startGroup, string startPin, string endGroup, string endPin) =>
        _wires.Add((startGroup, startPin, endGroup, endPin));

    private void SetInputSignal(string groupName, string pin, string signal)
    {
        var assignment = _groupsByName[groupName]["TruthTablePinAssignment"]!.AsObject();
        var signals = assignment["InputSignalNames"]?.AsObject() ?? new JsonObject();
        signals[pin] = signal;
        assignment["InputSignalNames"] = signals;
    }

    private void SetOutputSignal(string groupName, string pin, string signal)
    {
        var assignment = _groupsByName[groupName]["TruthTablePinAssignment"]!.AsObject();
        var signals = assignment["OutputSignalNames"]?.AsObject() ?? new JsonObject();
        signals[pin] = signal;
        assignment["OutputSignalNames"] = signals;
    }

    /// <summary>
    /// Instantiates one gate from its shipped template with every identifier re-rolled
    /// (duplicate-identifier defect #1049) and the body, external pins and frozen internal
    /// paths translated onto this instance's canvas anchor — the C# port of the
    /// <c>generate_ram_2x2.py</c> instantiate step.
    /// </summary>
    private string Emit(string shape, string name, int column, string description,
        Dictionary<string, string>? outputSignals = null, bool isRegister = false)
    {
        var template = _templates[shape];
        var groupDto = template["GroupDto"]!.DeepClone().AsObject();
        var assignment = template["TruthTablePinAssignment"]!.DeepClone().AsObject();

        var guidMap = groupDto["ChildComponentGuids"]!.AsArray()
            .ToDictionary(g => g!.GetValue<string>(), _ => NewIdentifier(Guid.NewGuid().ToString()));
        var idMap = groupDto["ChildComponentIds"]!.AsArray()
            .ToDictionary(id => id!.GetValue<string>(), id => NewIdentifier($"{id!.GetValue<string>().Split('_')[0]}_{Guid.NewGuid():N}"));

        groupDto["Identifier"] = NewIdentifier($"group_{Guid.NewGuid():N}");
        groupDto["GroupName"] = name;
        groupDto["Description"] = description;
        groupDto["IdGuid"] = NewIdentifier(Guid.NewGuid().ToString());
        groupDto["ChildComponentGuids"] = new JsonArray(groupDto["ChildComponentGuids"]!.AsArray()
            .Select(g => (JsonNode)guidMap[g!.GetValue<string>()]).ToArray());
        groupDto["ChildComponentIds"] = new JsonArray(groupDto["ChildComponentIds"]!.AsArray()
            .Select(id => (JsonNode)idMap[id!.GetValue<string>()]).ToArray());

        int row = _nextRowPerColumn.GetValueOrDefault(column);
        _nextRowPerColumn[column] = row + 1;
        double canvasX = OriginX + column * PitchX;
        double canvasY = OriginY + row * PitchY;
        double dx = canvasX - template["CanvasX"]!.GetValue<double>();
        double dy = canvasY - template["CanvasY"]!.GetValue<double>();

        foreach (var path in groupDto["InternalPaths"]!.AsArray())
        {
            var pathObject = path!.AsObject();
            pathObject["PathId"] = NewIdentifier(Guid.NewGuid().ToString());
            pathObject["StartComponentId"] = idMap[pathObject["StartComponentId"]!.GetValue<string>()];
            pathObject["EndComponentId"] = idMap[pathObject["EndComponentId"]!.GetValue<string>()];
            RemapGuid(pathObject, guidMap, "StartComponentGuid");
            RemapGuid(pathObject, guidMap, "EndComponentGuid");
            foreach (var segment in pathObject["Segments"]!.AsArray())
            {
                var segmentObject = segment!.AsObject();
                Shift(segmentObject, "StartX", dx);
                Shift(segmentObject, "EndX", dx);
                Shift(segmentObject, "StartY", dy);
                Shift(segmentObject, "EndY", dy);
            }
        }
        foreach (var pin in groupDto["ExternalPins"]!.AsArray())
        {
            var pinObject = pin!.AsObject();
            pinObject["PinId"] = NewIdentifier(Guid.NewGuid().ToString());
            pinObject["InternalComponentId"] = idMap[pinObject["InternalComponentId"]!.GetValue<string>()];
            RemapGuid(pinObject, guidMap, "InternalComponentGuid");
        }

        var children = new JsonArray();
        foreach (var child in template["ChildComponents"]!.AsArray())
        {
            var childObject = child!.DeepClone().AsObject();
            childObject["Identifier"] = idMap[childObject["Identifier"]!.GetValue<string>()];
            childObject["ComponentGuid"] = guidMap[childObject["ComponentGuid"]!.GetValue<string>()];
            childObject["X"] = childObject["X"]!.GetValue<double>() + dx;
            childObject["Y"] = childObject["Y"]!.GetValue<double>() + dy;
            children.Add(childObject);
        }

        groupDto["PhysicalX"] = canvasX;
        groupDto["PhysicalY"] = canvasY;
        if (isRegister)
            assignment["IsRegister"] = true;
        else
            assignment.Remove("IsRegister");
        if (outputSignals != null)
            assignment["OutputSignalNames"] = new JsonObject(outputSignals.Select(kv =>
                new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value)).ToArray());

        var group = new JsonObject
        {
            ["GroupDto"] = groupDto,
            ["ChildComponents"] = children,
            ["CanvasX"] = canvasX,
            ["CanvasY"] = canvasY,
            ["TruthTablePinAssignment"] = assignment,
        };
        _groups.Add(group);
        _groupsByName.Add(name, group);
        return name;
    }

    private void RemapGuid(JsonObject node, IReadOnlyDictionary<string, string> guidMap, string property)
    {
        var value = node[property]?.GetValue<string>();
        if (value != null && guidMap.TryGetValue(value, out var mapped))
            node[property] = mapped;
    }

    private string NewIdentifier(string candidate)
    {
        if (!_usedIdentifiers.Add(candidate))
            throw new InvalidOperationException($"duplicate identifier generated: {candidate}");
        return candidate;
    }

    private static void Shift(JsonObject segment, string property, double delta)
    {
        if (segment[property] is JsonValue value && value.TryGetValue<double>(out double coordinate))
            segment[property] = coordinate + delta;
    }

    private static string NotA(int bit) => $"NOTA{bit}";
    private static string SelNd(int word) => $"SELND{word}";
    private static string SelInv(int word) => $"SELINV{word}";
    private static string En(int word) => $"EN{word}";
    private static string Cpea(int word) => $"CPEA{word}";
    private static string Iw(int word) => $"IW{word}";
    private static string H(int word, int bit) => $"H{word}{bit}";
    private static string Le(int word, int bit) => $"LE{word}{bit}";
    private static string Reg(int word, int bit) => $"REG{word}{bit}";
    private static string Cp(int word, int bit) => $"CP{word}{bit}";
    private static string MuxArm(int word, int bit) => $"M{word}{bit}";

    /// <summary>Stage columns of the left-to-right layout; deeper copy-tree levels spill into the next columns.</summary>
    private sealed class RamScaleLayout
    {
        public RamScaleLayout(int addressBits)
        {
            bool twoBitAddress = addressBits == 2;
            InputCopiesColumn = 0;
            NotColumn = twoBitAddress ? 1 : 0;
            InvertedCopiesColumn = twoBitAddress ? 2 : 1;
            SelectDecodeColumn = 3;
            SelectInvertColumn = 4;
            SelectTreeColumn = twoBitAddress ? 5 : 1;
            EnColumn = twoBitAddress ? 6 : 4;
            CpeaColumn = EnColumn + 2;
            IwColumn = CpeaColumn + 1;
            HoldTreeColumn = IwColumn;
            LoadTreeColumn = IwColumn + 1;
            LoadColumn = LoadTreeColumn + 2;
            HoldColumn = LoadColumn + 1;
            RegColumn = HoldColumn + 1;
            TapColumn = RegColumn + 1;
            MuxArmColumn = TapColumn + 1;
            MuxCombineColumn = MuxArmColumn + 1;
            MuxOutputColumn = twoBitAddress ? MuxCombineColumn + 1 : MuxArmColumn + 1;
        }

        public int InputCopiesColumn { get; }
        public int NotColumn { get; }
        public int InvertedCopiesColumn { get; }
        public int SelectDecodeColumn { get; }
        public int SelectInvertColumn { get; }
        public int SelectTreeColumn { get; }
        public int EnColumn { get; }
        public int CpeaColumn { get; }
        public int IwColumn { get; }
        public int HoldTreeColumn { get; }
        public int LoadTreeColumn { get; }
        public int LoadColumn { get; }
        public int HoldColumn { get; }
        public int RegColumn { get; }
        public int TapColumn { get; }
        public int MuxArmColumn { get; }
        public int MuxCombineColumn { get; }
        public int MuxOutputColumn { get; }
    }
}
