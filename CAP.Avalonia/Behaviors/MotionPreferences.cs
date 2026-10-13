using System;

namespace CAP.Avalonia.Behaviors;

/// <summary>
/// Whether decorative motion (the workspace-hue cross-fade) should run. Off when the
/// headless screenshot host disables animations or when the user sets the
/// <c>LUNIMA_REDUCE_MOTION</c> environment variable to <c>1</c> / <c>true</c>; the variable
/// works identically on macOS, Linux and Windows.
/// </summary>
public static class MotionPreferences
{
    /// <summary>Environment variable that turns decorative motion off.</summary>
    public const string ReduceMotionVariable = "LUNIMA_REDUCE_MOTION";

    /// <summary>True when decorative motion should be skipped.</summary>
    public static bool ReduceMotion =>
        !RevealOnShowBehavior.AnimationsEnabled || IsSet(Environment.GetEnvironmentVariable(ReduceMotionVariable));

    /// <summary>True for the values that switch the preference on ("1", "true", case-insensitive).</summary>
    public static bool IsSet(string? value) =>
        value is not null
        && (value.Trim() == "1" || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
}
