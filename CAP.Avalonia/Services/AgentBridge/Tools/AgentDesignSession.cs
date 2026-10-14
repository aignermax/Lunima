using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>
/// The open design as the agent-bridge tools see it: the canvas, the shared undo history
/// and the file operations of the main window. File operations are resolved lazily because
/// they belong to the main view-model, which is built after the bridge's DI registration.
/// Only touched from the UI thread.
/// </summary>
public sealed class AgentDesignSession
{
    private readonly Func<FileOperationsViewModel?> _fileOperations;
    private readonly Func<IReadOnlyList<ComponentTemplate>> _templates;
    private readonly Func<IReadOnlyList<CAP_Core.Components.Process.ProcessGroup>> _processes;

    /// <summary>Creates the session.</summary>
    /// <param name="canvas">The design canvas the user sees.</param>
    /// <param name="commands">The undo/redo history the main window's Ctrl+Z uses.</param>
    /// <param name="fileOperations">Resolves the main window's file operations (null before it exists).</param>
    /// <param name="examples">Lists the shipped example designs.</param>
    /// <param name="templates">The loaded component library (crossing cell for cuts); empty when null.</param>
    public AgentDesignSession(
        DesignCanvasViewModel canvas,
        CommandManager commands,
        Func<FileOperationsViewModel?> fileOperations,
        ExampleDesignsService examples,
        Func<IReadOnlyList<ComponentTemplate>>? templates = null,
        Func<IReadOnlyList<CAP_Core.Components.Process.ProcessGroup>>? processes = null)
    {
        _processes = processes ?? (() => Array.Empty<CAP_Core.Components.Process.ProcessGroup>());
        Canvas = canvas;
        Commands = commands;
        _fileOperations = fileOperations;
        Examples = examples;
        _templates = templates ?? (() => Array.Empty<ComponentTemplate>());
    }

    /// <summary>The design canvas.</summary>
    public DesignCanvasViewModel Canvas { get; }

    /// <summary>The fabrication processes of the loaded PDKs (a die is bound to one of them).</summary>
    public IReadOnlyList<CAP_Core.Components.Process.ProcessGroup> Processes => _processes();

    /// <summary>The shared undo/redo history.</summary>
    public CommandManager Commands { get; }

    /// <summary>The shipped example designs.</summary>
    public ExampleDesignsService Examples { get; }

    /// <summary>The loaded component library (PDK templates).</summary>
    public IReadOnlyList<ComponentTemplate> Templates => _templates();

    /// <summary>Path of the example last opened through <c>load_example</c> (an untitled copy), or null.</summary>
    public string? LoadedExamplePath { get; set; }

    /// <summary>Ranked repair suggestions from the last <c>suggest_fixes</c> call, or null.</summary>
    public Repair.Suggest.RepairSuggestionSet? Suggestions { get; set; }

    /// <summary>File operations of the main window, or null while it is not built yet.</summary>
    public FileOperationsViewModel? FileOperations => _fileOperations();

    /// <summary>Finds a top-level component by identifier (exact, then case-insensitive) or by GUID.</summary>
    public ComponentViewModel? FindComponent(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return Canvas.Components.FirstOrDefault(c => c.Component.Identifier == id)
            ?? Canvas.Components.FirstOrDefault(c =>
                string.Equals(c.Component.Identifier, id, StringComparison.OrdinalIgnoreCase))
            ?? (Guid.TryParse(id, out var guid)
                ? Canvas.Components.FirstOrDefault(c => c.Component.Id == guid)
                : null);
    }

    /// <summary>Finds a connection view-model by its GUID.</summary>
    public WaveguideConnectionViewModel? FindConnection(string id) =>
        Guid.TryParse(id, out var guid)
            ? Canvas.Connections.FirstOrDefault(c => c.Connection.Id == guid)
            : null;
}
