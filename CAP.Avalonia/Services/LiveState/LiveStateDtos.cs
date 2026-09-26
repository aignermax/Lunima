namespace CAP.Avalonia.Services.LiveState;

/// <summary>
/// One placed component (or group) on the canvas, as seen by an external agent.
/// </summary>
public record LiveComponentDto(
    string Id,
    string Name,
    string? Type,
    double X,
    double Y,
    double RotationDegrees,
    double WidthMicrometers,
    double HeightMicrometers,
    bool IsLocked,
    bool IsGroup,
    string? GroupName,
    int ChildCount,
    bool IsLightSource);

/// <summary>
/// One waveguide connection with its live routing state.
/// </summary>
public record LiveConnectionDto(
    string StartPin,
    string EndPin,
    double StartX,
    double StartY,
    double EndX,
    double EndY,
    double PathLengthMicrometers,
    double LossDb,
    bool IsBlockedFallback);

/// <summary>
/// Snapshot of the whole design currently shown on the canvas.
/// </summary>
public record LiveDesignDto(
    string? DesignFile,
    string ActiveProcess,
    bool IsRouting,
    int FrozenPathCount,
    IReadOnlyList<LiveComponentDto> Components,
    IReadOnlyList<LiveConnectionDto> Connections);

/// <summary>
/// One error-console entry.
/// </summary>
public record LiveLogEntryDto(string Level, string Message, string TimeStamp);

/// <summary>
/// State of the most recent CW simulation run (if any).
/// </summary>
public record LiveSimulationDto(
    bool HasResult,
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<int> WavelengthsNm,
    int LightSourceCount,
    int ComponentCount,
    int ConnectionCount,
    bool PowerFlowOverlayVisible);

/// <summary>
/// Result of a state-changing command (move, load, screenshot).
/// </summary>
public record LiveCommandResultDto(bool Success, string Message);
