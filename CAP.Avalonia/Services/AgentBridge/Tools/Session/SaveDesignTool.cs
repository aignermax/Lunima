using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Session;

/// <summary>
/// <c>save_design</c>: saves the open design through the main window's own save path — to its
/// current file, to an absolute path, or (bare file name) into the examples folder, which is how
/// repaired examples are persisted. An existing file outside the examples folder is only
/// replaced with <c>overwrite=true</c>, so an agent never silently clobbers a user's file.
/// </summary>
public sealed class SaveDesignTool : IAgentBridgeTool
{
    private const string DesignExtension = ".lun";

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public SaveDesignTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "save_design";

    /// <inheritdoc/>
    public string Description =>
        "Save the open design as a .lun file. Without path: to its current file. path may be absolute, or a bare " +
        "file name (e.g. \"Logic Gate Counter 2-bit.lun\") which is saved into Lunima's examples folder — use that " +
        "to persist a repaired example (load_example opens examples as untitled copies). Replacing an existing " +
        "file outside the examples folder requires overwrite=true.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Absolute .lun path, or a bare file name for the examples folder. Omit to save to the current file." },
            overwrite = new { type = "boolean", description = "Allow replacing an existing file outside the examples folder. Default false." },
        },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var fileOps = _session.FileOperations;
        if (fileOps == null)
            return AgentToolResult.Error("The main window is not ready yet.");

        var examplesDirectory = _session.Examples.FindExamplesDirectory();
        var target = ResolveTarget(ToolArguments.GetString(arguments, "path"), fileOps.CurrentFilePath, examplesDirectory, out var error);
        if (target == null)
            return AgentToolResult.Error(error!);
        if (File.Exists(target) && !IsInside(target, examplesDirectory) && !ToolArguments.GetBool(arguments, "overwrite"))
            return AgentToolResult.Error($"'{target}' exists and is outside the examples folder. Pass overwrite=true to replace it.");
        if (Path.GetDirectoryName(target) is { } directory && !Directory.Exists(directory))
            return AgentToolResult.Error($"The folder '{directory}' does not exist.");

        if (!await fileOps.SaveDesignToPathAsync(target))
            return AgentToolResult.Error($"Saving to '{target}' failed (see Lunima's error console).");

        var report = ConnectionSnapshot.Report(_session);
        return AgentToolResult.Json(new
        {
            Saved = target,
            Bytes = new FileInfo(target).Length,
            ComponentCount = _session.Canvas.Components.Count,
            report.ConnectionCount,
            report.BlockedCount,
        });
    }

    /// <summary>
    /// The absolute .lun path to save to, or null with an <paramref name="error"/>: an explicit
    /// absolute path, a bare file name inside <paramref name="examplesDirectory"/>, or the current file.
    /// </summary>
    public string? ResolveTarget(string? path, string? currentFile, string? examplesDirectory, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            if (currentFile != null)
                return currentFile;
            error = "The design has no file yet (examples open as untitled copies). Pass path"
                + (_session.LoadedExamplePath is { } example ? $", e.g. \"{Path.GetFileName(example)}\" to update the example." : ".");
            return null;
        }

        path = path.Trim();
        if (!path.EndsWith(DesignExtension, StringComparison.OrdinalIgnoreCase))
            path += DesignExtension;
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        if (Path.GetFileName(path) != path)
        {
            error = "path must be absolute or a bare file name (saved into the examples folder).";
            return null;
        }
        if (examplesDirectory == null)
        {
            error = "No examples folder was found; pass an absolute path.";
            return null;
        }
        return Path.Combine(examplesDirectory, path);
    }

    private static bool IsInside(string file, string? directory)
    {
        if (directory == null)
            return false;
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return Path.GetFullPath(file).StartsWith(root, comparison);
    }
}
