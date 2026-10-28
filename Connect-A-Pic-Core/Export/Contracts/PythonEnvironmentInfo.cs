namespace CAP_Core.Export.Contracts;

/// <summary>
/// Information about Python environment availability.
/// </summary>
public class PythonEnvironmentInfo
{
    /// <summary>
    /// True if Python executable was found.
    /// </summary>
    public bool PythonAvailable { get; set; }

    /// <summary>
    /// Python version string (e.g., "3.10.5").
    /// </summary>
    public string? PythonVersion { get; set; }

    /// <summary>
    /// True if Nazca package is installed.
    /// </summary>
    public bool NazcaAvailable { get; set; }

    /// <summary>
    /// Nazca version string (e.g., "0.5.10").
    /// </summary>
    public string? NazcaVersion { get; set; }

    /// <summary>
    /// True if both Python and Nazca are available.
    /// </summary>
    public bool IsReady => PythonAvailable && NazcaAvailable;

    /// <summary>
    /// Descriptive status message for UI display.
    /// </summary>
    public string StatusMessage
    {
        get
        {
            if (!PythonAvailable)
                return "Python not found";
            if (!NazcaAvailable)
                return "Nazca not installed";
            return $"Python {PythonVersion}, Nazca {NazcaVersion}";
        }
    }
}
