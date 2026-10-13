using System.Text.Json;
using CAP.Avalonia.Commands.Dies;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Dies;

/// <summary>
/// <c>create_die</c>: adds a die of a process and size to the right of the existing dies —
/// the New die command, undoable. In a single-chip design the chip becomes "Die 1" first.
/// </summary>
public sealed class CreateDieTool : IAgentBridgeTool
{
    private const double DefaultSizeMicrometers = 2000;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public CreateDieTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "create_die";

    /// <inheritdoc/>
    public string Description =>
        "Create a die (chiplet) to the right of the existing dies. Arguments: process (display name or a PDK " +
        "name, see list_dies), width/height in µm (default 2000), optional name. In a single-chip design the " +
        "current chip becomes 'Die 1' with its content in the same undo step.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            process = new { type = "string", description = "Process display name or PDK name." },
            width = new { type = "number", description = "Die width in µm (default 2000)." },
            height = new { type = "number", description = "Die height in µm (default 2000)." },
            name = new { type = "string", description = "Die name (default 'Die N')." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var processName = ToolArguments.GetString(arguments, "process");
        var process = DieToolSupport.FindProcess(_session, processName);
        if (processName != null && process == null)
            return Task.FromResult(AgentToolResult.Error(
                $"Unknown process '{processName}'. Known: {DieToolSupport.ProcessNames(_session)}."));

        double width = ToolArguments.GetDouble(arguments, "width") ?? DefaultSizeMicrometers;
        double height = ToolArguments.GetDouble(arguments, "height") ?? DefaultSizeMicrometers;
        if (!DieDefinition.IsValidDimension(width) || !DieDefinition.IsValidDimension(height))
            return Task.FromResult(AgentToolResult.Error(
                $"Die size must be {DieDefinition.MinDimensionMicrometers}–{DieDefinition.MaxDimensionMicrometers} µm per side."));

        var canvas = _session.Canvas;
        if (canvas.IsInGroupEditMode)
            canvas.ExitToRoot();
        var taken = canvas.Dies.RootDies.Select(d => d.GroupName).Append("Die 1");
        var name = ToolArguments.GetString(arguments, "name") ?? DieAssembly.NextDieName(taken);
        var chipProcess = _session.FileOperations?.ActiveProcess;
        var command = new CreateDieCommand(canvas,
            new DieSpec(name, process, width, height),
            new DieSpec("Die 1", chipProcess is { IsPlayground: false } ? chipProcess : null,
                canvas.Dies.RootChipWidthMicrometers, canvas.Dies.RootChipHeightMicrometers));
        _session.Commands.ExecuteCommand(command);
        return Task.FromResult(AgentToolResult.Json(new
        {
            Created = DieToolSupport.Describe(command.CreatedDie!),
            ConvertedChip = command.ConvertedChipDie?.GroupName,
        }));
    }
}
