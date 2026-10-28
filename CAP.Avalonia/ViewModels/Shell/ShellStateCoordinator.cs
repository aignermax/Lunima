using System.ComponentModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.ComponentRegistry.RegistryBrowser;
using CAP.Avalonia.ViewModels.Panels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>Projects file, routing and history state into shell status and command availability.</summary>
internal sealed partial class ShellStateCoordinator : ObservableObject, IDisposable
{
    private readonly FileOperationsViewModel _files;
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commands;
    private readonly RegistryBrowserViewModel _registry;
    private readonly MonteCarloViewModel _monteCarlo;
    private readonly Action _processChanged;
    private readonly Action<string> _status;
    private readonly Action _historyChanged;

    [ObservableProperty] private string _windowTitle = "Lunima";

    public ShellStateCoordinator(FileOperationsViewModel files, DesignCanvasViewModel canvas,
        CommandManager commands, RegistryBrowserViewModel registry, MonteCarloViewModel monteCarlo,
        Action processChanged, Action<string> status, Action historyChanged)
    {
        _files = files;
        _canvas = canvas;
        _commands = commands;
        _registry = registry;
        _monteCarlo = monteCarlo;
        _processChanged = processChanged;
        _status = status;
        _historyChanged = historyChanged;
        files.PropertyChanged += FilesChanged;
        canvas.PropertyChanged += CanvasChanged;
        commands.PropertyChanged += HistoryChanged;
        commands.Executing += DocumentCommandStarting;
        commands.Undoing += DocumentCommandStarting;
        commands.HistoryCleared += DocumentHistoryCleared;
        RefreshRegistry();
        monteCarlo.RefreshTolerancesFromProcess();
    }

    public void Dispose()
    {
        _files.PropertyChanged -= FilesChanged;
        _canvas.PropertyChanged -= CanvasChanged;
        _commands.PropertyChanged -= HistoryChanged;
        _commands.Executing -= DocumentCommandStarting;
        _commands.Undoing -= DocumentCommandStarting;
        _commands.HistoryCleared -= DocumentHistoryCleared;
    }

    private void DocumentCommandStarting(IUndoableCommand command) => _canvas.Document.Invalidate();
    private void DocumentHistoryCleared() => _canvas.Document.Invalidate();

    private void FilesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(FileOperationsViewModel.CurrentFilePath) or nameof(FileOperationsViewModel.HasUnsavedChanges))
        {
            var marker = _files.HasUnsavedChanges ? "*" : "";
            WindowTitle = _files.CurrentFilePath is { } path
                ? $"{System.IO.Path.GetFileName(path)}{marker} — Lunima"
                : _files.HasUnsavedChanges ? "Untitled* — Lunima" : "Lunima";
        }
        if (args.PropertyName == nameof(FileOperationsViewModel.ActiveProcess))
        {
            // Bend radii, widths and models come from the process: results computed for the old one are stale.
            _canvas.Document.Invalidate();
            RefreshRegistry();
            _monteCarlo.RefreshTolerancesFromProcess();
            _processChanged();
        }
    }

    private void RefreshRegistry() => _registry.ActiveProcessId =
        _files.ActiveProcess is { IsPlayground: false } process ? process.DisplayName : null;

    private void CanvasChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DesignCanvasViewModel.RoutingStatusText)
            && !string.IsNullOrEmpty(_canvas.RoutingStatusText)) _status(_canvas.RoutingStatusText);
    }

    private void HistoryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CommandManager.CanUndo) or nameof(CommandManager.CanRedo)) _historyChanged();
    }
}
