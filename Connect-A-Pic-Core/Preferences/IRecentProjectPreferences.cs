namespace CAP_Core.Preferences;

/// <summary>The persisted recent-project list consumed by the MRU ordering and pinning workflow.</summary>
public interface IRecentProjectPreferences
{
    List<RecentProjectEntry> GetRecentProjects();
    void SetRecentProjects(IEnumerable<RecentProjectEntry> entries);
}
