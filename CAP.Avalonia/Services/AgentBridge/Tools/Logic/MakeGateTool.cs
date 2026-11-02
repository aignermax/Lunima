using System.Text.Json;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Logic;

/// <summary>
/// <c>make_gate</c>: turns a group into a logic gate — the Make Gate panel's flow. The optical
/// truth table is extracted by simulating every input combination (inputs and bias pins inject
/// light, outputs are thresholded), then the pin roles are stored on the group so logic networks
/// built from the design treat it as that gate. Undoable.
/// </summary>
public sealed class MakeGateTool : IAgentBridgeTool
{
    private const double DefaultThreshold = 0.5;
    private const int DefaultWavelengthNm = 1550;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public MakeGateTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "make_gate";

    /// <inheritdoc/>
    public string Description =>
        "Make a group a logic gate: name its external pins as inputs (max 4), outputs and optional always-on bias " +
        "pins; the optical truth table is simulated (power >= threshold is logic 1; by default the threshold is placed " +
        "in the widest gap between the output powers) and returned with each row's output power and the noise margin. register=true makes it a clocked register (commits on logic_step). " +
        "Logic networks (logic_build) are made of such gates. Undoable.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Group id or unique name." },
            inputs = new { type = "array", items = new { type = "string" }, description = "External pin names used as logic inputs." },
            outputs = new { type = "array", items = new { type = "string" }, description = "External pin names used as logic outputs." },
            bias = new { type = "array", items = new { type = "string" }, description = "External pins that always receive light." },
            threshold = new { type = "number", description = "Normalised power threshold in (0, 1). Default: the gate's stored threshold, else the middle of the widest gap between the simulated output powers." },
            name = new { type = "string", description = "Gate name (group name)." },
            wavelength_nm = new { type = "integer", description = $"Simulation wavelength (default {DefaultWavelengthNm})." },
            register = new { type = "boolean", description = "Clocked register gate. Default false." },
        },
        required = new[] { "id", "inputs", "outputs" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        if (_session.FindComponent(id)?.Component is not ComponentGroup { IsDie: false } group)
            return AgentToolResult.Error($"No top-level group with id '{id}'. Group the parts first (create_group).");
        var inputs = ToolArguments.GetStringList(arguments, "inputs");
        var outputs = ToolArguments.GetStringList(arguments, "outputs");
        var bias = ToolArguments.GetStringList(arguments, "bias");
        var requested = ToolArguments.GetDouble(arguments, "threshold") ?? group.TruthTablePinAssignment?.Threshold;
        var error = Validate(group, inputs, outputs, bias, requested ?? DefaultThreshold);
        if (error != null)
            return AgentToolResult.Error(error);

        var wavelength = (int)(ToolArguments.GetDouble(arguments, "wavelength_nm") ?? DefaultWavelengthNm);
        var extractor = new TruthTableExtractor();
        var token = context?.CancellationToken ?? default;
        var table = await extractor.ExtractAsync(group, inputs, outputs, bias, requested ?? DefaultThreshold, wavelength, token);
        var (suggested, margin) = WidestGap(table);
        var threshold = requested ?? suggested;
        if (requested == null && Math.Abs(threshold - DefaultThreshold) > double.Epsilon)
            table = await extractor.ExtractAsync(group, inputs, outputs, bias, threshold, wavelength, token);
        var assignment = new TruthTablePinAssignment
        {
            InputPinNames = inputs.ToList(),
            OutputPinNames = outputs.ToList(),
            BiasPinNames = bias.ToList(),
            Threshold = threshold,
            IsRegister = ToolArguments.GetBool(arguments, "register"),
        };

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        edit.Commit(() => _session.Commands.ExecuteCommand(new ApplyGateDefinitionCommand(
            group, new Dictionary<string, string>(), assignment, ToolArguments.GetString(arguments, "name"))));
        return AgentToolResult.Json(new
        {
            Gate = group.GroupName,
            Inputs = inputs,
            Outputs = outputs,
            Bias = bias,
            Threshold = Math.Round(threshold, 4),
            NoiseMarginOfGap = Math.Round(margin, 4),
            assignment.IsRegister,
            TruthTable = LogicDescriber.Rows(table),
        });
    }

    /// <summary>
    /// Middle of the widest gap between the sorted output powers, and that gap's half width:
    /// the threshold that separates logic 0 from logic 1 with the most margin.
    /// </summary>
    private static (double Threshold, double Margin) WidestGap(TruthTable table)
    {
        var powers = table.Rows.SelectMany(r => r.Outputs.Values.Select(v => v.Power)).Distinct().OrderBy(p => p).ToList();
        var best = (Threshold: DefaultThreshold, Margin: 0.0);
        for (var i = 1; i < powers.Count; i++)
        {
            var half = (powers[i] - powers[i - 1]) / 2;
            if (half > best.Margin)
                best = (powers[i - 1] + half, half);
        }
        return best.Threshold is > 0 and < 1 ? best : (DefaultThreshold, best.Margin);
    }

    private static string? Validate(ComponentGroup group, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs,
        IReadOnlyList<string> bias, double threshold)
    {
        var pins = group.ExternalPins.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = inputs.Concat(outputs).Concat(bias).Where(p => !pins.Contains(p)).ToList();
        if (unknown.Count > 0)
            return $"'{group.GroupName}' has no external pin {string.Join(", ", unknown)}. Pins: {string.Join(", ", pins)}.";
        if (inputs.Count == 0 || outputs.Count == 0)
            return "Name at least one input and one output pin.";
        if (inputs.Count > TruthTableExtractor.MaxLogicInputs)
            return $"A gate has at most {TruthTableExtractor.MaxLogicInputs} inputs; compose wider logic from several gates.";
        if (inputs.Concat(outputs).Concat(bias).Distinct().Count() != inputs.Count + outputs.Count + bias.Count)
            return "A pin can have only one role.";
        return threshold is > 0 and < 1 ? null : "threshold must lie strictly between 0 and 1.";
    }
}
