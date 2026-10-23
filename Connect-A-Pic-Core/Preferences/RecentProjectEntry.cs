namespace CAP_Core.Preferences;

/// <summary>
/// A single entry in the recent-projects list shown on the Home screen.
/// </summary>
public class RecentProjectEntry
{
    /// <summary>Absolute path to the .lun design file.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>UTC timestamp of when the project was last opened or saved.</summary>
    public DateTime LastOpenedUtc { get; set; }

    /// <summary>
    /// True when the user pinned this project. Pinned entries survive cap
    /// eviction and are shown first on the Home screen.
    /// </summary>
    public bool Pinned { get; set; }
}
