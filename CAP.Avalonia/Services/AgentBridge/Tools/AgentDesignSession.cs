using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;

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

    /// <summary>Creates the session.</summary>
    /// <param name="canvas">The design canvas the user sees.</param>
    /// <param name="commands">The undo/redo history the main window's Ctrl+Z uses.</param>
    /// <param name="fileOperations">Resolves the main window's file operations (null before it exists).</param>
    /// <param name="examples">Lists the shipped example designs.</param>
    public AgentDesignSession(
        DesignCanvasViewModel canvas,
        CommandManager commands,
        Func<FileOperationsViewModel?> fileOperations,
        ExampleDesignsService examples)
    {
        Canvas = canvas;
        Commands = commands;
        _fileOperations = fileOperations;
        Examples = examples;
    }

    /// <summary>The design canvas.</summary>
    public DesignCanvasViewModel Canvas { get; }

    /// <summary>The shared undo/redo history.</summary>
    public CommandManager Commands { get; }

    /// <summary>The shipped example designs.</summary>
    public ExampleDesignsService Examples { get; }

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
