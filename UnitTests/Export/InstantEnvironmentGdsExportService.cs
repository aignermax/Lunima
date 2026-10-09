using CAP_Core.Export;

namespace UnitTests.Export;

/// <summary>
/// A <see cref="GdsExportService"/> whose environment check answers at once with a fixed
/// result. ViewModel tests about interpreter selection need the check to happen, not a real
/// Python probe — spawning python and importing Nazca cost about three seconds per test.
/// </summary>
internal sealed class InstantEnvironmentGdsExportService : GdsExportService
{
    /// <summary>Creates the service; <paramref name="ready"/> decides whether Python and Nazca report as available.</summary>
    public InstantEnvironmentGdsExportService(bool ready = true) => Ready = ready;

    /// <summary>What the next probe reports; may change between probes.</summary>
    public bool Ready { get; set; }

    /// <summary>How often the environment was actually probed.</summary>
    public int ProbeCount { get; private set; }

    /// <inheritdoc />
    protected override Task<PythonEnvironmentInfo> ProbeEnvironmentAsync()
    {
        ProbeCount++;
        bool ready = Ready;
        return Task.FromResult(new PythonEnvironmentInfo
        {
            PythonAvailable = ready,
            PythonVersion = ready ? "3.11.0" : null,
            NazcaAvailable = ready,
            NazcaVersion = ready ? "0.6.1" : null,
        });
    }
}
