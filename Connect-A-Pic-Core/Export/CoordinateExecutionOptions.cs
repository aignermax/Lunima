using System.Collections.Immutable;

namespace CAP_Core.Export;

/// <summary>Immutable process configuration shared by coordinate extraction and comparison.</summary>
internal sealed record CoordinateExecutionOptions(
    TimeSpan Timeout,
    string? ScriptPath = null,
    ImmutableArray<string> ProbeArguments = default);
