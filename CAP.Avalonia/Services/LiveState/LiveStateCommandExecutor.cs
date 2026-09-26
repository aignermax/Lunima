using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Services.LiveState;

/// <summary>
/// Executes state-changing agent commands (move, load, simulate, screenshot)
/// against the live application. All methods must run on the UI thread —
/// the API layer marshals every call.
/// </summary>
public class LiveStateCommandExecutor
{
    /// <summary>Result of the most recent agent-triggered simulation run.</summary>
    public SimulationResult? LastSimulationResult { get; private set; }

    /// <summary>
    /// Moves the component with the given identifier to an absolute canvas position,
    /// going through the regular undoable command pipeline (re-routes connections).
    /// </summary>
    public LiveCommandResultDto MoveComponent(MainViewModel vm, string componentId, double x, double y)
    {
        var componentVm = vm.Canvas.Components
            .FirstOrDefault(c => c.Component.Identifier == componentId);
        if (componentVm == null)
            return new LiveCommandResultDto(false, $"Component '{componentId}' not found");
        if (componentVm.IsLocked)
            return new LiveCommandResultDto(false, $"Component '{componentId}' is locked");

        var command = new MoveComponentCommand(
            vm.Canvas, componentVm, componentVm.X, componentVm.Y, x, y);
        vm.CommandManager.ExecuteCommand(command);
        return new LiveCommandResultDto(true, FormattableString.Invariant(
            $"Moved '{componentId}' to ({componentVm.X}, {componentVm.Y})"));
    }

    /// <summary>Loads a .lun design file into the running app.</summary>
    public async Task<LiveCommandResultDto> LoadDesignAsync(MainViewModel vm, string filePath)
    {
        if (!File.Exists(filePath))
            return new LiveCommandResultDto(false, $"File not found: {filePath}");

        bool loaded = await vm.FileOperations.LoadDesignFromPathAsync(filePath);
        return loaded
            ? new LiveCommandResultDto(true, $"Loaded {filePath}")
            : new LiveCommandResultDto(false, $"Failed to load {filePath} (see error console)");
    }

    /// <summary>Runs the CW S-Matrix simulation and caches the result for GET /simulation.</summary>
    public async Task<LiveSimulationDto> RunSimulationAsync(MainViewModel vm)
    {
        LastSimulationResult = await vm.Simulation.RunAsync(vm.Canvas);
        return LiveStateSnapshotBuilder.BuildSimulation(vm, LastSimulationResult);
    }

    /// <summary>
    /// Renders the main window to a PNG file and returns its path, so an external
    /// agent can visually verify what the running app shows.
    /// </summary>
    public LiveCommandResultDto CaptureScreenshot(string? requestedPath)
    {
        var window = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null)
            return new LiveCommandResultDto(false, "No main window available (headless run?)");

        var pixelSize = new PixelSize(
            Math.Max(1, (int)window.Bounds.Width), Math.Max(1, (int)window.Bounds.Height));
        var path = requestedPath ?? Path.Combine(Path.GetTempPath(),
            $"lunima-agent-shot-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.png");

        using var bitmap = new RenderTargetBitmap(pixelSize);
        bitmap.Render(window);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path);
        return new LiveCommandResultDto(true, path);
    }
}
