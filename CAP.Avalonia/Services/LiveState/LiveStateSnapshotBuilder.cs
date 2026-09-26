using CAP_Core;
using CAP_Core.Components.Core;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Services.LiveState;

/// <summary>
/// Translates live ViewModel state into serializable DTOs for the agent API.
/// All methods must be called on the UI thread (the API layer marshals).
/// </summary>
public static class LiveStateSnapshotBuilder
{
    /// <summary>Builds a snapshot of the design currently shown on the canvas.</summary>
    public static LiveDesignDto BuildDesign(MainViewModel vm)
    {
        var canvas = vm.Canvas;
        var components = canvas.Components.Select(BuildComponent).ToList();
        var connections = canvas.Connections.Select(BuildConnection).ToList();
        return new LiveDesignDto(
            vm.FileOperations.CurrentFilePath,
            vm.ActiveProcessLabel,
            canvas.IsRouting,
            canvas.CanvasFrozenPaths.Count,
            components,
            connections);
    }

    /// <summary>Builds the DTO for one placed component or group.</summary>
    public static LiveComponentDto BuildComponent(ComponentViewModel componentVm)
    {
        var component = componentVm.Component;
        var group = component as ComponentGroup;
        return new LiveComponentDto(
            component.Identifier,
            componentVm.DisplayName,
            componentVm.ComponentTypeName,
            componentVm.X,
            componentVm.Y,
            component.RotationDegrees,
            component.WidthMicrometers,
            component.HeightMicrometers,
            component.IsLocked,
            group != null,
            group?.GroupName,
            group?.ChildComponents.Count ?? 0,
            componentVm.IsLightSource);
    }

    /// <summary>Builds the DTO for one waveguide connection incl. routing state.</summary>
    public static LiveConnectionDto BuildConnection(WaveguideConnectionViewModel connectionVm)
    {
        var connection = connectionVm.Connection;
        return new LiveConnectionDto(
            DescribePin(connection.StartPin),
            DescribePin(connection.EndPin),
            connectionVm.StartX,
            connectionVm.StartY,
            connectionVm.EndX,
            connectionVm.EndY,
            connectionVm.PathLength,
            connectionVm.LossDb,
            connectionVm.IsBlockedFallback);
    }

    /// <summary>Builds the error-console entry list, oldest first.</summary>
    public static IReadOnlyList<LiveLogEntryDto> BuildErrors(ErrorConsoleService errorConsole) =>
        errorConsole.Entries
            .Select(log => new LiveLogEntryDto(
                log.Level.ToString(),
                log.Message,
                log.TimeStamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

    /// <summary>Builds the simulation state DTO from the most recent run (if any).</summary>
    public static LiveSimulationDto BuildSimulation(MainViewModel vm, SimulationResult? lastResult)
    {
        if (lastResult == null)
        {
            return new LiveSimulationDto(
                false, false, "No simulation has been run yet", Array.Empty<int>(),
                0, 0, 0, vm.Canvas.ShowPowerFlow);
        }

        return new LiveSimulationDto(
            true,
            lastResult.Success,
            lastResult.ErrorMessage,
            lastResult.WavelengthsUsed,
            lastResult.LightSourceCount,
            lastResult.ComponentCount,
            lastResult.ConnectionCount,
            vm.Canvas.ShowPowerFlow);
    }

    /// <summary>Human-readable "componentId.pinName" endpoint description.</summary>
    private static string DescribePin(CAP_Core.Components.Core.PhysicalPin pin) =>
        $"{pin.ParentComponent?.Identifier ?? "?"}.{pin.Name}";
}
