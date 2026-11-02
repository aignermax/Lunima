using System.Text.Json;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Logic;

/// <summary>
/// <c>isa_run</c>: assembles and runs a program for Lunima's 4-bit learning ISA in the ISA
/// playground — on the C# golden model, or with photonic=true on the units the built logic
/// network implements (adder, AND/NOT logic unit, zero flag, accumulator, RAM), exactly as the
/// playground's photonic toggle does. Returns the final machine state and a step trace.
/// </summary>
public sealed class IsaRunTool : IAgentBridgeTool
{
    private const int DefaultMaxSteps = 200;
    private const int MaxTraceSteps = 64;

    private readonly IsaPlaygroundViewModel _playground;

    /// <summary>Creates the tool.</summary>
    public IsaRunTool(IsaPlaygroundViewModel playground) => _playground = playground;

    /// <inheritdoc/>
    public string Name => "isa_run";

    /// <inheritdoc/>
    public string Description =>
        "Run a program on Lunima's 4-bit accumulator CPU (docs/ISA.md): LOAD imm | ADD addr | AND addr | NOT | " +
        "STORE addr | JMP addr | JZ addr | HALT; 4-bit wrap-around, 4 RAM words, 16 ROM words, labels 'name:', " +
        "';' comments. Pass program (source) or sample (file name; omit both to list samples). photonic=true executes " +
        "the operations the built logic network implements (logic_build first) — the photonic CPU check.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            program = new { type = "string", description = "Assembly source." },
            sample = new { type = "string", description = "Shipped sample program file name (e.g. count-to-5.asm)." },
            photonic = new { type = "boolean", description = "Use the photonic units of the built network. Default false." },
            max_steps = new { type = "integer", description = $"Step limit (default {DefaultMaxSteps})." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        if (ToolArguments.GetString(arguments, "program") == null && ToolArguments.GetString(arguments, "sample") == null)
            return Task.FromResult(AgentToolResult.Json(new
            {
                Samples = _playground.Samples.Select(s => new { s.FileName, s.Source }).ToList(),
            }));
        var source = ResolveSource(arguments, out var error);
        if (source == null)
            return Task.FromResult(error!);

        var photonic = ToolArguments.GetBool(arguments, "photonic");
        if (photonic && !_playground.IsAnyPhotonicAvailable)
            return Task.FromResult(AgentToolResult.Error(
                "No photonic ISA unit in the built network. Open a design that implements one (e.g. 'Logic Gate 4-Bit Adder.lun') and call logic_build."));
        _playground.UsePhotonicAdder = photonic;
        _playground.ProgramText = source;
        _playground.AssembleCommand.Execute(null);
        if (!_playground.IsAssembled)
            return Task.FromResult(AgentToolResult.Error($"Assembly failed: {_playground.ErrorText}"));

        var maxSteps = Math.Max(1, (int)(ToolArguments.GetDouble(arguments, "max_steps") ?? DefaultMaxSteps));
        var trace = new List<object>();
        var steps = 0;
        while (steps < maxSteps && !_playground.IsHalted && _playground.StepCommand.CanExecute(null))
        {
            _playground.StepCommand.Execute(null);
            steps++;
            if (trace.Count < MaxTraceSteps)
                trace.Add(new { Pc = _playground.ProgramCounter, Acc = _playground.Accumulator, Ram = _playground.RamText });
            if (_playground.ErrorText.Length > 0)
                break;
        }

        return Task.FromResult(AgentToolResult.Json(new
        {
            Steps = steps,
            Halted = _playground.IsHalted,
            Error = _playground.ErrorText.Length > 0 ? _playground.ErrorText : null,
            Pc = _playground.ProgramCounter,
            Accumulator = _playground.Accumulator,
            Ram = _playground.RamText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
            Photonic = photonic,
            PhotonicUnits = new
            {
                Add = _playground.IsPhotonicAddAvailable,
                And = _playground.IsPhotonicAndAvailable,
                Not = _playground.IsPhotonicNotAvailable,
                ZeroFlag = _playground.IsPhotonicZeroFlagAvailable,
                Accumulator = _playground.IsPhotonicAccumulatorAvailable,
                DataMemory = _playground.IsPhotonicDataMemoryAvailable,
            },
            PhotonicStatus = string.IsNullOrEmpty(_playground.PhotonicStatusText) ? null : _playground.PhotonicStatusText,
            Trace = trace,
        }));
    }

    private string? ResolveSource(JsonElement arguments, out AgentToolResult? error)
    {
        error = null;
        var program = ToolArguments.GetString(arguments, "program");
        if (!string.IsNullOrWhiteSpace(program))
            return program;
        var sampleName = ToolArguments.GetString(arguments, "sample");
        var samples = _playground.Samples;
        var sample = samples.FirstOrDefault(s => string.Equals(s.FileName, sampleName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(s.FileName), sampleName, StringComparison.OrdinalIgnoreCase));
        if (sample != null)
            return sample.Source;
        error = AgentToolResult.Error($"No sample '{sampleName}'. Samples: {string.Join(", ", samples.Select(s => s.FileName))}.");
        return null;
    }
}
