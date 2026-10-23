using System.Text.Json;
using CAP_Contracts;

namespace CAP.Avalonia.Services.AiTools;

/// <summary>
/// Represents a single AI tool invocable by Claude.
/// Implement this interface to add a new tool without modifying any existing files.
/// Register the implementation in App.axaml.cs as <c>services.AddTransient&lt;IAiTool, YourTool&gt;()</c>.
/// </summary>
public interface IAiTool
{
    /// <summary>Tool name used in the Claude API (e.g., "copy_component").</summary>
    string Name { get; }

    /// <summary>Human-readable description shown to Claude to guide tool selection.</summary>
    string Description { get; }

    /// <summary>JSON schema object describing the tool's input parameters.</summary>
    object InputSchema { get; }

    /// <summary>Executes the tool with the given JSON input and returns a result string.</summary>
    Task<string> ExecuteAsync(JsonElement input);

    /// <summary>Runs an externally admitted call. Async tools override this to guard later publication.</summary>
    Task<string> ExecuteAsync(JsonElement input, ICommitPermission permission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<string> execution = null!;
        permission.Commit(() => execution = ExecuteAsync(input));
        return execution;
    }
}
