namespace CAP_Core.Solvers.Fdtd;

/// <summary>
/// Result of a quick "can the FDTD solver run here?" probe — checked before the
/// (long) solve so the user gets immediate, actionable feedback instead of a
/// failure deep into the run.
/// </summary>
public class FdtdAvailability
{
    /// <summary>True when the solver backend (e.g. Docker engine) is ready to use.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Human-readable status / how to fix it when not available.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Creates an available result.</summary>
    public static FdtdAvailability Available(string message) =>
        new() { IsAvailable = true, Message = message };

    /// <summary>Creates an unavailable result with an actionable message.</summary>
    public static FdtdAvailability Unavailable(string message) =>
        new() { IsAvailable = false, Message = message };
}
