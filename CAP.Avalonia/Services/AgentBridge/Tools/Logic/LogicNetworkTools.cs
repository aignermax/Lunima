using System.Text.Json;
using CAP_Core.Analysis.LogicAnalysis;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Logic;

/// <summary>
/// The logic level of a design (gates wired into a network) for agents: build it through the
/// Logic panel — so the ISA playground's photonic units see the same network — then evaluate
/// input combinations, enumerate the truth table, clock registers and reset them.
/// </summary>
public abstract class LogicNetworkToolBase : IAgentBridgeTool
{
    /// <summary>The tool's session.</summary>
    protected AgentDesignSession Session { get; }

    /// <summary>Holds the network the Logic panel built last.</summary>
    protected BuiltLogicNetworkProvider Networks { get; }

    /// <summary>Creates the tool.</summary>
    protected LogicNetworkToolBase(AgentDesignSession session, BuiltLogicNetworkProvider networks)
    {
        Session = session;
        Networks = networks;
    }

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract string Description { get; }

    /// <inheritdoc/>
    public abstract object InputSchema { get; }

    /// <inheritdoc/>
    public abstract Task<AgentToolResult> ExecuteAsync(JsonElement arguments);

    /// <summary>The current network, or an error result telling the agent to build it.</summary>
    protected LogicNetworkEvaluator? CurrentNetwork(out AgentToolResult? error)
    {
        var network = Session.Main?.RightPanel.Logic.HasNetwork == true ? Networks.Network : null;
        error = network == null
            ? AgentToolResult.Error("No logic network is built (or the design changed since). Call logic_build.")
            : null;
        return network;
    }

    /// <summary>Reads the <c>inputs</c> object (name → 0/1/true/false); missing inputs are 0.</summary>
    protected static Dictionary<string, bool>? ReadInputs(LogicNetworkEvaluator network, JsonElement arguments, out string? error)
    {
        error = null;
        var bits = network.InputPinNames.ToDictionary(n => n, _ => false);
        if (!arguments.TryGetProperty("inputs", out var given) || given.ValueKind == JsonValueKind.Null)
            return bits;
        if (given.ValueKind != JsonValueKind.Object)
        {
            error = "inputs must be an object such as {\"A\": 1, \"B\": 0}.";
            return null;
        }
        foreach (var property in given.EnumerateObject())
        {
            if (!bits.ContainsKey(property.Name))
            {
                error = $"Unknown input '{property.Name}'. Inputs: {string.Join(", ", network.InputPinNames)}.";
                return null;
            }
            bits[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => property.Value.GetDouble() != 0,
                JsonValueKind.String => property.Value.GetString() is "1" or "true",
                _ => false,
            };
        }
        return bits;
    }

    /// <summary>The input-bits schema shared by evaluate and step.</summary>
    protected static object InputsSchema => new
    {
        type = "object",
        description = "Input signal levels by name, e.g. {\"A\": 1, \"B\": 0}; omitted inputs are 0.",
    };
}

/// <summary><c>logic_build</c>: assembles the design's gates into a network (Logic panel → Build).</summary>
public sealed class LogicBuildTool : LogicNetworkToolBase
{
    /// <summary>Creates the tool.</summary>
    public LogicBuildTool(AgentDesignSession session, BuiltLogicNetworkProvider networks) : base(session, networks) { }

    /// <inheritdoc/>
    public override string Name => "logic_build";

    /// <inheritdoc/>
    public override string Description =>
        "Build the logic network from the design's gates (groups made with make_gate, nested in any cells) and the " +
        "wires between them: returns network inputs/outputs (named by the unconnected gate pins), gates, registers, " +
        "critical-path delay (ps) and fan-out warnings. Required before logic_evaluate/truth_table/step and isa_run photonic=true.";

    /// <inheritdoc/>
    public override object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public override async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var logic = Session.Main?.RightPanel.Logic;
        if (logic == null)
            return AgentToolResult.Error("The main window is not ready yet.");
        await logic.BuildNetworkCommand.ExecuteAsync(null);
        var network = logic.HasNetwork ? Networks.Network : null;
        return network != null
            ? AgentToolResult.Json(LogicDescriber.Network(network))
            : AgentToolResult.Error($"No logic network: {logic.StatusText}");
    }
}

/// <summary><c>logic_evaluate</c>: settles the network for one input combination.</summary>
public sealed class LogicEvaluateTool : LogicNetworkToolBase
{
    /// <summary>Creates the tool.</summary>
    public LogicEvaluateTool(AgentDesignSession session, BuiltLogicNetworkProvider networks) : base(session, networks) { }

    /// <inheritdoc/>
    public override string Name => "logic_evaluate";

    /// <inheritdoc/>
    public override string Description =>
        "Evaluate the built logic network for one input combination: every output bit. Registers keep their " +
        "committed values (clock them with logic_step).";

    /// <inheritdoc/>
    public override object InputSchema => new { type = "object", properties = new { inputs = InputsSchema } };

    /// <inheritdoc/>
    public override Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var network = CurrentNetwork(out var error);
        if (network == null)
            return Task.FromResult(error!);
        var inputs = ReadInputs(network, arguments, out var reason);
        if (inputs == null)
            return Task.FromResult(AgentToolResult.Error(reason!));
        var outputs = network.Evaluate(inputs);
        return Task.FromResult(AgentToolResult.Json(new
        {
            Inputs = LogicDescriber.Bits(network.InputPinNames, inputs),
            Outputs = LogicDescriber.Bits(network.OutputPinNames, outputs),
        }));
    }
}

/// <summary><c>logic_truth_table</c>: every input combination of the built network.</summary>
public sealed class LogicTruthTableTool : LogicNetworkToolBase
{
    private const int MaxInputs = 12;

    /// <summary>Creates the tool.</summary>
    public LogicTruthTableTool(AgentDesignSession session, BuiltLogicNetworkProvider networks) : base(session, networks) { }

    /// <inheritdoc/>
    public override string Name => "logic_truth_table";

    /// <inheritdoc/>
    public override string Description =>
        $"Enumerate all 2^n input combinations of the built network (n <= {MaxInputs}) and return the outputs as " +
        "bit strings in input/output order — compact enough to compare with the intended function (adder, mux, …).";

    /// <inheritdoc/>
    public override object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public override Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var network = CurrentNetwork(out var error);
        if (network == null)
            return Task.FromResult(error!);
        var inputs = network.InputPinNames;
        if (inputs.Count > MaxInputs)
            return Task.FromResult(AgentToolResult.Error($"{inputs.Count} inputs; enumerate at most {MaxInputs} (use logic_evaluate for single cases)."));

        var outputs = network.OutputPinNames;
        var rows = new List<string>();
        for (var combination = 0; combination < 1 << inputs.Count; combination++)
        {
            var bits = inputs.Select((name, i) => (name, on: (combination >> i & 1) == 1)).ToDictionary(x => x.name, x => x.on);
            var result = network.Evaluate(bits);
            rows.Add(string.Concat(inputs.Select(n => bits[n] ? '1' : '0')) + " -> " + string.Concat(outputs.Select(n => result[n] ? '1' : '0')));
        }
        return Task.FromResult(AgentToolResult.Json(new { InputOrder = inputs, OutputOrder = outputs, Rows = rows }));
    }
}

/// <summary><c>logic_step</c>: one clock edge — settle with the inputs, then commit every register.</summary>
public sealed class LogicStepTool : LogicNetworkToolBase
{
    /// <summary>Creates the tool.</summary>
    public LogicStepTool(AgentDesignSession session, BuiltLogicNetworkProvider networks) : base(session, networks) { }

    /// <inheritdoc/>
    public override string Name => "logic_step";

    /// <inheritdoc/>
    public override string Description =>
        "One clock cycle of the built network: settle with the given inputs, commit every register, settle again " +
        "and return outputs and register state after the edge. reset=true clears all registers first.";

    /// <inheritdoc/>
    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            inputs = InputsSchema,
            reset = new { type = "boolean", description = "Clear all registers before this cycle." },
        },
    };

    /// <inheritdoc/>
    public override Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var network = CurrentNetwork(out var error);
        if (network == null)
            return Task.FromResult(error!);
        var inputs = ReadInputs(network, arguments, out var reason);
        if (inputs == null)
            return Task.FromResult(AgentToolResult.Error(reason!));

        if (ToolArguments.GetBool(arguments, "reset"))
            network.ResetRegisters();
        network.Evaluate(inputs);
        var switched = network.Step();
        var outputs = network.Evaluate(inputs);
        return Task.FromResult(AgentToolResult.Json(new
        {
            Inputs = LogicDescriber.Bits(network.InputPinNames, inputs),
            Outputs = LogicDescriber.Bits(network.OutputPinNames, outputs),
            Registers = LogicDescriber.Registers(network),
            SwitchEvents = switched.Count,
        }));
    }
}
